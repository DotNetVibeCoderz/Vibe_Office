using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cuan.Models;

/// <summary>
/// Jurnal Umum Entry
/// </summary>
public class JournalEntry
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string JournalNumber { get; set; } = string.Empty; // Auto-generated e.g., "JU-2024-0001"

    public DateTime TransactionDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string? Reference { get; set; } // Nomor referensi dokumen sumber

    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public decimal ExchangeRate { get; set; } = 1;

    public string? CreatedBy { get; set; }
    public bool IsPosted { get; set; } // Sudah diposting ke buku besar
    public DateTime? PostedAt { get; set; }
    public string? PostedBy { get; set; }

    public JournalSource SourceType { get; set; } = JournalSource.Manual;

    public ICollection<JournalEntryDetail> Details { get; set; } = new List<JournalEntryDetail>();
}

/// <summary>
/// Detail Jurnal
/// </summary>
public class JournalEntryDetail
{
    [Key]
    public int Id { get; set; }

    public int JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public int ChartOfAccountId { get; set; }
    public ChartOfAccount? ChartOfAccount { get; set; }

    [MaxLength(300)]
    public string? Description { get; set; }

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public int? ItemId { get; set; }
    public Item? Item { get; set; }
}

public enum JournalSource
{
    Manual,         // Jurnal manual
    SalesInvoice,   // Dari faktur penjualan
    PurchaseInvoice,// Dari faktur pembelian
    CashIn,         // Kas masuk
    CashOut,        // Kas keluar
    BankIn,         // Bank masuk
    BankOut,        // Bank keluar
    GiroIn,         // Giro masuk
    GiroOut,        // Giro keluar
    StockAdj,       // Penyesuaian stok
    Transfer        // Transfer antar bank
}
