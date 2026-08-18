using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VibeDesk.Infrastructure.Configuration;

namespace VibeDesk.Infrastructure.Persistence;

/// <summary>
/// Wires <see cref="AppDbContext"/> to whichever of the four providers is configured. All four are
/// referenced by the project, so switching backends is a config change and not a rebuild.
/// </summary>
public static class DatabaseRegistration
{
    public static IServiceCollection AddVibeDeskDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
                      ?? new DatabaseOptions();

        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        var connectionString = ResolveConnectionString(configuration, options);

        services.AddDbContextPool<AppDbContext>(builder =>
        {
            ConfigureProvider(builder, options.Provider, connectionString);

            if (options.EnableSensitiveDataLogging) builder.EnableSensitiveDataLogging();
            if (options.EnableDetailedErrors) builder.EnableDetailedErrors();

            // Tracking stays on by default. Every read path in the services opts out explicitly with
            // AsNoTracking(), and the write paths load an entity, mutate it and call SaveChanges —
            // which is a silent no-op against a detached entity. A global no-tracking default made
            // every update in the application return the edited object and persist nothing.
        });

        return services;
    }

    /// <summary>
    /// Falls back through <c>Database:ConnectionString</c>, a connection string named after the
    /// provider, then <c>DefaultConnection</c>, then a local SQLite file so a fresh clone just runs.
    /// </summary>
    public static string ResolveConnectionString(IConfiguration configuration, DatabaseOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.ConnectionString;

        var named = configuration.GetConnectionString(options.Provider.ToString());
        if (!string.IsNullOrWhiteSpace(named)) return named;

        var fallback = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(fallback)) return fallback;

        if (options.Provider != DatabaseProviderKind.Sqlite)
        {
            throw new InvalidOperationException(
                $"No connection string found for database provider '{options.Provider}'. " +
                $"Set ConnectionStrings:{options.Provider} or Database:ConnectionString.");
        }

        return "Data Source=App_Data/vibedesk.db";
    }

    /// <summary>
    /// Assembly holding the migration set for a provider. Each provider gets its own because EF
    /// discovers every <c>Migration</c> in the migrations assembly — one shared project would mean
    /// four colliding model snapshots.
    /// </summary>
    public static string MigrationsAssemblyFor(DatabaseProviderKind provider) => provider switch
    {
        DatabaseProviderKind.SqlServer => "VibeDesk.Migrations.SqlServer",
        DatabaseProviderKind.MySql => "VibeDesk.Migrations.MySql",
        DatabaseProviderKind.PostgreSql => "VibeDesk.Migrations.PostgreSql",
        _ => "VibeDesk.Migrations.Sqlite",
    };

    public static void ConfigureProvider(
        DbContextOptionsBuilder builder,
        DatabaseProviderKind provider,
        string connectionString)
    {
        var migrationsAssembly = MigrationsAssemblyFor(provider);

        switch (provider)
        {
            case DatabaseProviderKind.SqlServer:
                builder.UseSqlServer(connectionString, sql =>
                {
                    sql.EnableRetryOnFailure(3);
                    sql.MigrationsAssembly(migrationsAssembly);
                    sql.MigrationsHistoryTable("__VibeDeskMigrations");
                });
                break;

            case DatabaseProviderKind.MySql:
                builder.UseMySQL(connectionString, sql =>
                {
                    sql.EnableRetryOnFailure(3);
                    sql.MigrationsAssembly(migrationsAssembly);
                    sql.MigrationsHistoryTable("__VibeDeskMigrations");
                });
                break;

            case DatabaseProviderKind.PostgreSql:
                builder.UseNpgsql(connectionString, sql =>
                {
                    sql.EnableRetryOnFailure(3);
                    sql.MigrationsAssembly(migrationsAssembly);
                    sql.MigrationsHistoryTable("__VibeDeskMigrations");
                });
                break;

            default:
                // Make sure the directory behind the file exists before SQLite tries to open it.
                EnsureSqliteDirectory(connectionString);
                builder.UseSqlite(connectionString, sql =>
                {
                    sql.MigrationsAssembly(migrationsAssembly);
                    sql.MigrationsHistoryTable("__VibeDeskMigrations");
                });
                break;
        }
    }

    private static void EnsureSqliteDirectory(string connectionString)
    {
        try
        {
            var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
            var path = builder.DataSource;
            if (string.IsNullOrWhiteSpace(path) || path == ":memory:") return;

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        }
        catch (Exception)
        {
            // A malformed connection string is the provider's error to report, not ours.
        }
    }
}
