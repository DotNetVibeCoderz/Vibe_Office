using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VibeDesk.Infrastructure.Configuration;
using VibeDesk.Infrastructure.Persistence;

namespace VibeDesk.Migrations.Sqlite;

/// <summary>
/// Design-time context for <c>dotnet ef</c>, with the provider baked in.
/// </summary>
/// <remarks>
/// One factory per migrations assembly rather than a shared one selected by an environment variable:
/// EF only scans the startup and target assemblies for a factory, and hard-coding the provider makes
/// it impossible to scaffold a migration against the wrong backend by forgetting to set a variable.
/// The connection string below is a placeholder — scaffolding never opens a connection.
/// </remarks>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string PlaceholderConnection = "Data Source=App_Data/vibedesk.db";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("VIBEDESK_MIGRATION_CONNECTION")
                               ?? PlaceholderConnection;

        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseRegistration.ConfigureProvider(builder, DatabaseProviderKind.Sqlite, connectionString);

        return new AppDbContext(builder.Options);
    }
}
