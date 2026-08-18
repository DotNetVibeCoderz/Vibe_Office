namespace VibeDesk.Infrastructure.Configuration;

/// <summary>Which relational backend to use. Bound from <c>Database:Provider</c>.</summary>
public enum DatabaseProviderKind
{
    Sqlite = 0,
    SqlServer = 1,
    MySql = 2,
    PostgreSql = 3,
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public DatabaseProviderKind Provider { get; set; } = DatabaseProviderKind.Sqlite;

    /// <summary>Overrides the connection string named after the provider in <c>ConnectionStrings</c>.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Applies pending migrations at startup. Convenient in dev; leave off in production where
    /// migrations should be a deliberate deployment step.
    /// </summary>
    public bool MigrateOnStartup { get; set; } = true;

    /// <summary>Inserts the sample users and documents when the database is empty.</summary>
    public bool SeedSampleData { get; set; } = true;

    public bool EnableSensitiveDataLogging { get; set; }
    public bool EnableDetailedErrors { get; set; }

    /// <summary>Logs any query slower than this, to catch accidental N+1s early.</summary>
    public int SlowQueryThresholdMs { get; set; } = 500;
}

public enum StorageProviderKind
{
    FileSystem = 0,
    AzureBlob = 1,
    S3 = 2,
    MinIO = 3,
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public StorageProviderKind Provider { get; set; } = StorageProviderKind.FileSystem;

    /// <summary>Root directory for <see cref="StorageProviderKind.FileSystem"/>.</summary>
    public string RootPath { get; set; } = "App_Data/storage";

    /// <summary>Container/bucket name for the cloud backends.</summary>
    public string Bucket { get; set; } = "vibedesk";

    public string? ConnectionString { get; set; }

    /// <summary>Endpoint for S3-compatible services (MinIO, Wasabi, R2).</summary>
    public string? Endpoint { get; set; }
    public string? Region { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public bool UseSsl { get; set; } = true;

    public long MaxUploadBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>Lifetime of generated signed URLs.</summary>
    public int SignedUrlMinutes { get; set; } = 30;

    /// <summary>
    /// Encrypts object bodies at rest with AES-GCM before handing them to the backend. Requires
    /// <see cref="EncryptionKey"/>; the spec's "data encryption at rest" requirement.
    /// </summary>
    public bool EncryptAtRest { get; set; }

    /// <summary>Base64 32-byte key. Supply through user-secrets or the environment, never in appsettings.</summary>
    public string? EncryptionKey { get; set; }
}

public enum CacheProviderKind
{
    Memory = 0,
    Redis = 1,
}

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public CacheProviderKind Provider { get; set; } = CacheProviderKind.Memory;

    public string? ConnectionString { get; set; }

    /// <summary>Key prefix so several environments can share one Redis instance safely.</summary>
    public string InstanceName { get; set; } = "vibedesk:";

    public int DefaultTtlSeconds { get; set; } = 300;

    /// <summary>Cap on in-memory entries, to bound the dev cache.</summary>
    public long MemorySizeLimit { get; set; } = 8192;
}
