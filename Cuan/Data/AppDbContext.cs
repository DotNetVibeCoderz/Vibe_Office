using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Cuan.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cuan.Data;

/// <summary>
/// Main Application Database Context
/// Combines Identity and Business tables
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private bool _isSavingAudit;

    public AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor? httpContextAccessor = null) : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    // Master Data
    public DbSet<ChartOfAccount> ChartOfAccounts => Set<ChartOfAccount>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemStock> ItemStocks => Set<ItemStock>();
    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();
    public DbSet<UnitOfMeasure> UnitOfMeasures => Set<UnitOfMeasure>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PaymentTerm> PaymentTerms => Set<PaymentTerm>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<ExchangeRateHistory> ExchangeRateHistories => Set<ExchangeRateHistory>();
    public DbSet<Tax> Taxes => Set<Tax>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    // Transactions
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryDetail> JournalEntryDetails => Set<JournalEntryDetail>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesInvoiceDetail> SalesInvoiceDetails => Set<SalesInvoiceDetail>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoiceDetail> PurchaseInvoiceDetails => Set<PurchaseInvoiceDetail>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<GiroTransaction> GiroTransactions => Set<GiroTransaction>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<BankTransfer> BankTransfers => Set<BankTransfer>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    // System
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ChartOfAccount self-referencing
        builder.Entity<ChartOfAccount>()
            .HasOne(c => c.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // ItemCategory self-referencing
        builder.Entity<ItemCategory>()
            .HasOne(c => c.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Item relations
        builder.Entity<Item>()
            .HasOne(i => i.PurchaseUnit)
            .WithMany()
            .HasForeignKey(i => i.PurchaseUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Item>()
            .HasOne(i => i.SalesUnit)
            .WithMany()
            .HasForeignKey(i => i.SalesUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // JournalEntryDetail
        builder.Entity<JournalEntryDetail>()
            .HasOne(jd => jd.JournalEntry)
            .WithMany(je => je.Details)
            .HasForeignKey(jd => jd.JournalEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        // SalesInvoice
        builder.Entity<SalesInvoice>()
            .HasOne(s => s.JournalEntry)
            .WithMany()
            .HasForeignKey(s => s.JournalEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SalesInvoiceDetail>()
            .HasOne(sd => sd.SalesInvoice)
            .WithMany(s => s.Details)
            .HasForeignKey(sd => sd.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        // PurchaseInvoice
        builder.Entity<PurchaseInvoice>()
            .HasOne(p => p.JournalEntry)
            .WithMany()
            .HasForeignKey(p => p.JournalEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseInvoiceDetail>()
            .HasOne(pd => pd.PurchaseInvoice)
            .WithMany(p => p.Details)
            .HasForeignKey(pd => pd.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique constraints
        builder.Entity<ChartOfAccount>().HasIndex(c => c.AccountCode).IsUnique();
        builder.Entity<Item>().HasIndex(i => i.ItemCode).IsUnique();
        builder.Entity<Customer>().HasIndex(c => c.CustomerCode).IsUnique();
        builder.Entity<Supplier>().HasIndex(s => s.SupplierCode).IsUnique();
        builder.Entity<Currency>().HasIndex(c => c.Code).IsUnique();
        builder.Entity<Warehouse>().HasIndex(w => w.Code).IsUnique();
        builder.Entity<JournalEntry>().HasIndex(j => j.JournalNumber).IsUnique();
        builder.Entity<SalesInvoice>().HasIndex(s => s.InvoiceNumber).IsUnique();
        builder.Entity<PurchaseInvoice>().HasIndex(p => p.InvoiceNumber).IsUnique();

        // Indexes for performance
        builder.Entity<JournalEntry>().HasIndex(j => j.TransactionDate);
        builder.Entity<SalesInvoice>().HasIndex(s => s.InvoiceDate);
        builder.Entity<PurchaseInvoice>().HasIndex(p => p.InvoiceDate);
        builder.Entity<StockMovement>().HasIndex(s => new { s.ItemId, s.WarehouseId, s.MovementDate });
        builder.Entity<AuditLog>().HasIndex(a => new { a.EntityName, a.CreatedAt });
        builder.Entity<Notification>().HasIndex(n => new { n.UserId, n.IsRead });
    }

    public override int SaveChanges()
    {
        return SaveChangesAsync().GetAwaiter().GetResult();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_isSavingAudit)
        {
            return await base.SaveChangesAsync(cancellationToken);
        }

        var auditEntries = BuildAuditEntries();
        var result = await base.SaveChangesAsync(cancellationToken);

        if (auditEntries.Count > 0)
        {
            _isSavingAudit = true;
            foreach (var item in auditEntries)
            {
                if (string.IsNullOrWhiteSpace(item.Log.EntityId))
                {
                    var pk = item.Entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue;
                    item.Log.EntityId = pk?.ToString();
                }
            }

            AuditLogs.AddRange(auditEntries.Select(a => a.Log));
            await base.SaveChangesAsync(cancellationToken);
            _isSavingAudit = false;
        }

        return result;
    }

    private List<(AuditLog Log, Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Entry)> BuildAuditEntries()
    {
        var logs = new List<(AuditLog, Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry)>();

        var entries = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Entity is not AuditLog)
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
            var entityId = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString();

            object? oldValues = null;
            object? newValues = null;

            if (entry.State == EntityState.Modified)
            {
                var changes = new Dictionary<string, object?>();
                var originals = new Dictionary<string, object?>();
                foreach (var prop in entry.Properties)
                {
                    if (!prop.IsModified) continue;
                    originals[prop.Metadata.Name] = entry.OriginalValues[prop.Metadata.Name];
                    changes[prop.Metadata.Name] = prop.CurrentValue;
                }
                oldValues = originals;
                newValues = changes;
            }
            else if (entry.State == EntityState.Added)
            {
                newValues = entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
            }
            else if (entry.State == EntityState.Deleted)
            {
                oldValues = entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);
            }

            var log = new AuditLog
            {
                UserId = _httpContextAccessor?.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                UserName = _httpContextAccessor?.HttpContext?.User?.Identity?.Name,
                Action = action,
                EntityName = entityName,
                EntityId = entityId,
                Description = $"{action} {entityName}",
                OldValues = oldValues != null ? JsonSerializer.Serialize(oldValues) : null,
                NewValues = newValues != null ? JsonSerializer.Serialize(newValues) : null,
                IpAddress = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString(),
                CreatedAt = DateTime.UtcNow
            };

            logs.Add((log, entry));
        }

        return logs;
    }
}

/// <summary>
/// Extended Application User dengan field bisnis
/// </summary>
public class ApplicationUser : IdentityUser
{
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    [MaxLength(50)]
    public string? Theme { get; set; } = "light";
}

/// <summary>
/// Extended Application Role
/// </summary>
public class ApplicationRole : IdentityRole
{
    [MaxLength(200)]
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
