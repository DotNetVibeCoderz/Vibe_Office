using System.Text.Json;
using Cuan.Models;
using Microsoft.EntityFrameworkCore;

namespace Cuan.Services;

/// <summary>
/// Auto audit logging service — mencatat setiap perubahan entity ke AuditLog
/// </summary>
public class AuditService
{
    private readonly Data.AppDbContext _db;
    private readonly IHttpContextAccessor _http;

    public AuditService(Data.AppDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task LogAsync(string action, string entityName, object? entityId = null,
        object? oldValues = null, object? newValues = null, string? description = null)
    {
        var log = new AuditLog
        {
            UserId = _http.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            UserName = _http.HttpContext?.User?.Identity?.Name,
            Action = action,
            EntityName = entityName,
            EntityId = entityId?.ToString(),
            Description = description,
            OldValues = oldValues != null ? JsonSerializer.Serialize(oldValues) : null,
            NewValues = newValues != null ? JsonSerializer.Serialize(newValues) : null,
            IpAddress = _http.HttpContext?.Connection?.RemoteIpAddress?.ToString(),
            CreatedAt = DateTime.UtcNow
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();
    }

    /// <summary>Hook Entity Framework untuk auto-log entity changes</summary>
    public async Task LogEntityChangesAsync()
    {
        var entries = _db.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            var action = entry.State switch
            {
                EntityState.Added => "CREATE",
                EntityState.Modified => "UPDATE",
                EntityState.Deleted => "DELETE",
                _ => "?"
            };

            var entityName = entry.Entity.GetType().Name;
            var entityId = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue;

            object? oldValues = null;
            object? newValues = null;

            if (entry.State == EntityState.Modified)
            {
                var changes = new Dictionary<string, object?>();
                var originalChanges = new Dictionary<string, object?>();
                foreach (var prop in entry.Properties)
                {
                    var original = entry.GetDatabaseValues()?.GetValue<object?>(prop.Metadata.Name);
                    var current = prop.CurrentValue;
                    if (!object.Equals(original, current))
                    {
                        originalChanges[prop.Metadata.Name] = original;
                        changes[prop.Metadata.Name] = current;
                    }
                }
                oldValues = originalChanges;
                newValues = changes;
            }
            else if (entry.State == EntityState.Added)
            {
                newValues = entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
            }
            else if (entry.State == EntityState.Deleted)
            {
                oldValues = entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
            }

            await LogAsync(action, entityName, entityId, oldValues, newValues);
        }
    }
}
