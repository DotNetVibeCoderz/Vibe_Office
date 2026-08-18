using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using VibeDesk.Application.Abstractions;
using VibeDesk.Infrastructure.Configuration;

namespace VibeDesk.Infrastructure.Storage;

/// <summary>Azure Blob Storage backend. The container is created on first use if it doesn't exist.</summary>
public sealed class AzureBlobStorageProvider : IStorageProvider
{
    private readonly BlobContainerClient _container;
    private readonly StorageOptions _options;

    /// <summary>Guards container creation so concurrent first requests only probe once.</summary>
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialised;

    public AzureBlobStorageProvider(IOptions<StorageOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException(
                "Storage:ConnectionString is required when Storage:Provider is AzureBlob.");
        }

        _container = new BlobContainerClient(_options.ConnectionString, _options.Bucket);
    }

    public string Name => "AzureBlob";

    private async Task<BlobContainerClient> ContainerAsync(CancellationToken ct)
    {
        if (_initialised) return _container;

        await _initGate.WaitAsync(ct);
        try
        {
            if (!_initialised)
            {
                await _container.CreateIfNotExistsAsync(cancellationToken: ct);
                _initialised = true;
            }
        }
        finally
        {
            _initGate.Release();
        }

        return _container;
    }

    public async Task<StorageObject> PutAsync(
        string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var container = await ContainerAsync(ct);
        var blob = container.GetBlobClient(key);

        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);

        var properties = await blob.GetPropertiesAsync(cancellationToken: ct);
        return new StorageObject(
            key,
            properties.Value.ContentLength,
            properties.Value.ContentType ?? contentType,
            properties.Value.LastModified,
            properties.Value.ETag.ToString());
    }

    public async Task<Stream?> GetAsync(string key, CancellationToken ct = default)
    {
        var container = await ContainerAsync(ct);
        var blob = container.GetBlobClient(key);

        try
        {
            var response = await blob.DownloadStreamingAsync(cancellationToken: ct);
            return response.Value.Content;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        var container = await ContainerAsync(ct);
        var response = await container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: ct);
        return response.Value;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        var container = await ContainerAsync(ct);
        return await container.GetBlobClient(key).ExistsAsync(ct);
    }

    public async Task<StorageObject?> StatAsync(string key, CancellationToken ct = default)
    {
        var container = await ContainerAsync(ct);

        try
        {
            var properties = await container.GetBlobClient(key).GetPropertiesAsync(cancellationToken: ct);
            return new StorageObject(
                key,
                properties.Value.ContentLength,
                properties.Value.ContentType ?? "application/octet-stream",
                properties.Value.LastModified,
                properties.Value.ETag.ToString());
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    public async Task<string> CopyAsync(
        string sourceKey, string destinationKey, CancellationToken ct = default)
    {
        var container = await ContainerAsync(ct);
        var source = container.GetBlobClient(sourceKey);
        var destination = container.GetBlobClient(destinationKey);

        var operation = await destination.StartCopyFromUriAsync(source.Uri, cancellationToken: ct);
        await operation.WaitForCompletionAsync(ct);

        return destinationKey;
    }

    public async Task<string> GetUrlAsync(
        string key, TimeSpan? validFor = null, CancellationToken ct = default)
    {
        var container = await ContainerAsync(ct);
        var blob = container.GetBlobClient(key);

        // SAS generation needs the account key, which is only available with a shared-key credential.
        // Without it, fall back to the app's own authenticated download route.
        if (!blob.CanGenerateSasUri) return $"/storage/{Uri.EscapeDataString(key)}";

        var expiry = DateTimeOffset.UtcNow.Add(validFor ?? TimeSpan.FromMinutes(_options.SignedUrlMinutes));
        return blob.GenerateSasUri(BlobSasPermissions.Read, expiry).ToString();
    }
}
