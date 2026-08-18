using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using VibeDesk.Application.Abstractions;
using VibeDesk.Infrastructure.Configuration;

namespace VibeDesk.Infrastructure.Storage;

/// <summary>
/// Decorator that encrypts object bodies with AES-GCM before they reach the real backend, satisfying
/// the "encryption at rest" requirement independently of whatever the storage service offers.
/// </summary>
/// <remarks>
/// Wire format: <c>[magic:4][nonce:12][tag:16][ciphertext…]</c>. The magic prefix lets
/// <see cref="GetAsync"/> pass through objects that were stored before encryption was switched on,
/// so enabling the feature does not orphan existing files.
/// </remarks>
public sealed class EncryptingStorageProvider : IStorageProvider
{
    private static readonly byte[] Magic = "VDE1"u8.ToArray();
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 4 + NonceSize + TagSize;

    private readonly IStorageProvider _inner;
    private readonly byte[] _key;

    public EncryptingStorageProvider(IStorageProvider inner, IOptions<StorageOptions> options)
    {
        _inner = inner;

        var configured = options.Value.EncryptionKey;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "Storage:EncryptAtRest is enabled but Storage:EncryptionKey is not set. " +
                "Provide a base64-encoded 32-byte key via user-secrets or the environment.");
        }

        try
        {
            _key = Convert.FromBase64String(configured);
        }
        catch (FormatException e)
        {
            throw new InvalidOperationException("Storage:EncryptionKey must be valid base64.", e);
        }

        if (_key.Length != 32)
        {
            throw new InvalidOperationException(
                $"Storage:EncryptionKey must decode to 32 bytes for AES-256-GCM; got {_key.Length}.");
        }
    }

    public string Name => $"{_inner.Name}+AES-GCM";

    public async Task<StorageObject> PutAsync(
        string key, Stream content, string contentType, CancellationToken ct = default)
    {
        // AES-GCM needs the whole plaintext to produce the tag, so the body is buffered. Uploads are
        // already capped by Storage:MaxUploadBytes, which bounds this.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var plaintext = buffer.ToArray();

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var payload = new byte[HeaderSize + ciphertext.Length];
        Magic.CopyTo(payload, 0);
        nonce.CopyTo(payload, 4);
        tag.CopyTo(payload, 4 + NonceSize);
        ciphertext.CopyTo(payload, HeaderSize);

        using var encrypted = new MemoryStream(payload, writable: false);
        var stored = await _inner.PutAsync(key, encrypted, contentType, ct);

        // Report the plaintext length; callers use this for quota and Content-Length.
        return stored with { SizeBytes = plaintext.Length };
    }

    public async Task<Stream?> GetAsync(string key, CancellationToken ct = default)
    {
        var source = await _inner.GetAsync(key, ct);
        if (source is null) return null;

        await using (source)
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, ct);
            var payload = buffer.ToArray();

            if (!LooksEncrypted(payload))
            {
                // Stored before encryption was enabled — hand it back untouched.
                return new MemoryStream(payload, writable: false);
            }

            var nonce = payload.AsSpan(4, NonceSize);
            var tag = payload.AsSpan(4 + NonceSize, TagSize);
            var ciphertext = payload.AsSpan(HeaderSize);
            var plaintext = new byte[ciphertext.Length];

            try
            {
                using var aes = new AesGcm(_key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
            }
            catch (CryptographicException e)
            {
                // Authentication failure means a wrong key or a tampered object; never return the bytes.
                throw new InvalidOperationException(
                    $"Stored object '{key}' failed authentication. The encryption key may have changed.", e);
            }

            return new MemoryStream(plaintext, writable: false);
        }
    }

    private static bool LooksEncrypted(byte[] payload) =>
        payload.Length >= HeaderSize && payload.AsSpan(0, 4).SequenceEqual(Magic);

    public Task<bool> DeleteAsync(string key, CancellationToken ct = default) =>
        _inner.DeleteAsync(key, ct);

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        _inner.ExistsAsync(key, ct);

    public async Task<StorageObject?> StatAsync(string key, CancellationToken ct = default)
    {
        var stat = await _inner.StatAsync(key, ct);
        if (stat is null) return null;

        // Subtract the envelope so the reported size matches the plaintext.
        var size = Math.Max(0, stat.SizeBytes - HeaderSize);
        return stat with { SizeBytes = size };
    }

    public Task<string> CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default) =>
        // Ciphertext copies verbatim: the nonce travels in the envelope, so no re-encryption is needed.
        _inner.CopyAsync(sourceKey, destinationKey, ct);

    /// <summary>
    /// Routes through the app's own download endpoint rather than a signed backend URL — a direct URL
    /// would hand the client ciphertext it cannot decrypt.
    /// </summary>
    public Task<string> GetUrlAsync(string key, TimeSpan? validFor = null, CancellationToken ct = default) =>
        Task.FromResult($"/storage/{Uri.EscapeDataString(key)}");

    /// <summary>Generates a fresh key in the format <c>Storage:EncryptionKey</c> expects.</summary>
    public static string GenerateKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
