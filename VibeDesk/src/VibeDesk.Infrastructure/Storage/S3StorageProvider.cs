using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using VibeDesk.Application.Abstractions;
using VibeDesk.Infrastructure.Configuration;

namespace VibeDesk.Infrastructure.Storage;

/// <summary>
/// S3 backend, used for both AWS S3 and MinIO.
/// </summary>
/// <remarks>
/// MinIO implements the S3 API, so it needs no separate client library — pointing the AWS SDK at a
/// custom <c>ServiceURL</c> with path-style addressing is MinIO's own documented approach. Using one
/// code path for both means the storage layer has one fewer dependency and one fewer set of quirks,
/// and any S3-compatible service (Wasabi, Cloudflare R2, Ceph) works the same way.
/// </remarks>
public sealed class S3StorageProvider : IStorageProvider, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly StorageOptions _options;
    private readonly bool _isMinio;

    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialised;

    public S3StorageProvider(IOptions<StorageOptions> options)
    {
        _options = options.Value;
        _isMinio = _options.Provider == StorageProviderKind.MinIO;

        var config = new AmazonS3Config();

        if (!string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            config.ServiceURL = _options.Endpoint;
            // MinIO and most self-hosted gateways serve buckets as a path segment, not a subdomain.
            config.ForcePathStyle = true;
        }

        if (!string.IsNullOrWhiteSpace(_options.Region))
        {
            config.AuthenticationRegion = _options.Region;
        }

        if (string.IsNullOrWhiteSpace(_options.Endpoint) && !string.IsNullOrWhiteSpace(_options.Region))
        {
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(_options.Region);
        }

        // Explicit keys when configured; otherwise the default credential chain (IAM role, env vars,
        // shared profile), which is what you want on EC2/ECS.
        _client = !string.IsNullOrWhiteSpace(_options.AccessKey) && !string.IsNullOrWhiteSpace(_options.SecretKey)
            ? new AmazonS3Client(new BasicAWSCredentials(_options.AccessKey, _options.SecretKey), config)
            : new AmazonS3Client(config);
    }

    public string Name => _isMinio ? "MinIO" : "S3";

    /// <summary>
    /// MinIO deployments are usually created empty, so the bucket is provisioned on first use.
    /// On AWS the bucket is assumed to exist and be managed by infrastructure-as-code.
    /// </summary>
    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (_initialised || !_isMinio) return;

        await _initGate.WaitAsync(ct);
        try
        {
            if (_initialised) return;

            var exists = await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(_client, _options.Bucket);
            if (!exists)
            {
                await _client.PutBucketAsync(new PutBucketRequest { BucketName = _options.Bucket }, ct);
            }

            _initialised = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task<StorageObject> PutAsync(
        string key, Stream content, string contentType, CancellationToken ct = default)
    {
        await EnsureBucketAsync(ct);

        // The SDK needs a seekable stream to compute a payload signature; buffer when it isn't.
        Stream payload = content;
        MemoryStream? buffered = null;
        if (!content.CanSeek)
        {
            buffered = new MemoryStream();
            await content.CopyToAsync(buffered, ct);
            buffered.Position = 0;
            payload = buffered;
        }

        try
        {
            var response = await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = key,
                InputStream = payload,
                ContentType = contentType,
                DisablePayloadSigning = _isMinio && !_options.UseSsl,
            }, ct);

            return new StorageObject(
                key,
                payload.Length,
                contentType,
                DateTimeOffset.UtcNow,
                response.ETag);
        }
        finally
        {
            if (buffered is not null) await buffered.DisposeAsync();
        }
    }

    public async Task<Stream?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetObjectAsync(_options.Bucket, key, ct);

            // The response stream is tied to the response object, so copy it out before disposing.
            var buffer = new MemoryStream();
            using (response)
            {
                await response.ResponseStream.CopyToAsync(buffer, ct);
            }
            buffer.Position = 0;
            return buffer;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        if (!await ExistsAsync(key, ct)) return false;

        await _client.DeleteObjectAsync(_options.Bucket, key, ct);
        return true;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        await StatAsync(key, ct) is not null;

    public async Task<StorageObject?> StatAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetObjectMetadataAsync(_options.Bucket, key, ct);
            return new StorageObject(
                key,
                response.ContentLength,
                response.Headers.ContentType ?? "application/octet-stream",
                response.LastModified ?? DateTime.UtcNow,
                response.ETag);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string> CopyAsync(
        string sourceKey, string destinationKey, CancellationToken ct = default)
    {
        await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = _options.Bucket,
            SourceKey = sourceKey,
            DestinationBucket = _options.Bucket,
            DestinationKey = destinationKey,
        }, ct);

        return destinationKey;
    }

    public Task<string> GetUrlAsync(
        string key, TimeSpan? validFor = null, CancellationToken ct = default)
    {
        var expiry = DateTime.UtcNow.Add(validFor ?? TimeSpan.FromMinutes(_options.SignedUrlMinutes));

        var url = _client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Expires = expiry,
            Verb = HttpVerb.GET,
        });

        return Task.FromResult(url);
    }

    public void Dispose()
    {
        _client.Dispose();
        _initGate.Dispose();
    }
}
