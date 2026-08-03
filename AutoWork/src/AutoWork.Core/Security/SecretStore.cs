using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoWork.Core.Security;

/// <summary>
/// Where API keys live. Config.json holds only a <em>reference</em> to a secret, so a user can
/// share or version their config without leaking credentials.
/// </summary>
public interface ISecretStore
{
    /// <summary>Resolves a reference. "env:NAME" reads the environment; anything else is a stored name.</summary>
    string? Resolve(string? reference);

    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
    IReadOnlyCollection<string> Names { get; }
}

/// <summary>
/// File-backed secret store.
///
/// Contents are encrypted with AES-GCM. On Windows the AES key is itself wrapped with DPAPI
/// (current-user scope), so the file cannot be read by another account or moved to another
/// machine. On Linux and macOS there is no DPAPI equivalent here, so the key file is written
/// with owner-only permissions and the protection is effectively "whoever can read your
/// home directory as you can read your keys" — the same bar as an SSH private key.
/// This is stated plainly in the security documentation rather than oversold.
///
/// Users who want a stronger guarantee should reference keys as "env:NAME" and let a real
/// secret manager inject them.
/// </summary>
public sealed class FileSecretStore : ISecretStore
{
    private const string EnvPrefix = "env:";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    private readonly string _path;
    private readonly string _keyPath;
    private readonly Lock _gate = new();
    private Dictionary<string, string> _cache;

    public FileSecretStore(string? path = null)
    {
        _path = path ?? AppPaths.SecretsFile;
        _keyPath = _path + ".key";
        _cache = Load();
    }

    public IReadOnlyCollection<string> Names
    {
        get { lock (_gate) return _cache.Keys.ToArray(); }
    }

    public string? Resolve(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;

        if (reference.StartsWith(EnvPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var variable = reference[EnvPrefix.Length..].Trim();
            var value = Environment.GetEnvironmentVariable(variable);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return Get(reference);
    }

    public string? Get(string name)
    {
        lock (_gate)
            return _cache.TryGetValue(name, out var value) ? value : null;
    }

    public void Set(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Secret name cannot be empty.", nameof(name));

        lock (_gate)
        {
            _cache[name] = value;
            Persist();
        }
    }

    public void Delete(string name)
    {
        lock (_gate)
        {
            if (_cache.Remove(name))
                Persist();
        }
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);

            var payload = File.ReadAllBytes(_path);
            if (payload.Length < 29) return new(StringComparer.OrdinalIgnoreCase);

            var key = LoadOrCreateKey();

            // Layout: [12-byte nonce][16-byte tag][ciphertext]
            var nonce = payload.AsSpan(0, 12);
            var tag = payload.AsSpan(12, 16);
            var cipher = payload.AsSpan(28);
            var plain = new byte[cipher.Length];

            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);

            var json = Encoding.UTF8.GetString(plain);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return parsed is null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (CryptographicException)
        {
            // Wrong key or tampered file. Starting empty beats crashing the app on launch;
            // the user re-enters keys and the old file is left in place for inspection.
            return new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var json = JsonSerializer.Serialize(_cache, SerializerOptions);
        var plain = Encoding.UTF8.GetBytes(json);

        var key = LoadOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];

        using (var aes = new AesGcm(key, 16))
            aes.Encrypt(nonce, plain, cipher, tag);

        var payload = new byte[nonce.Length + tag.Length + cipher.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, nonce.Length);
        cipher.CopyTo(payload, nonce.Length + tag.Length);

        WriteRestricted(_path, payload);
    }

    private byte[] LoadOrCreateKey()
    {
        if (File.Exists(_keyPath))
        {
            var stored = File.ReadAllBytes(_keyPath);
            return Unwrap(stored);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        WriteRestricted(_keyPath, Wrap(key));
        return key;
    }

    private static byte[] Wrap(byte[] key) =>
        OperatingSystem.IsWindows()
            ? System.Security.Cryptography.ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser)
            : key;

    private static byte[] Unwrap(byte[] stored)
    {
        if (!OperatingSystem.IsWindows()) return stored;

        try
        {
            return System.Security.Cryptography.ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            // Key was created by a different user or machine — treat as unreadable.
            throw;
        }
    }

    /// <summary>Writes a file only the current user can read.</summary>
    private static void WriteRestricted(string path, byte[] contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllBytes(path, contents);
            return;
        }

        // Create with 0600 before any bytes land on disk, so there is no readable window.
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };

        using var stream = new FileStream(path, options);
        stream.Write(contents);
    }
}
