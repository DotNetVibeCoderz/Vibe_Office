using System.ComponentModel.DataAnnotations;

namespace Cuan.Models;

/// <summary>
/// Transaksi Kas (Kas Masuk & Kas Keluar)
/// </summary>
public class CashTransaction
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string TransactionNumber { get; set; } = string.Empty;

    public DateTime TransactionDate { get; set; }
    public CashTransactionType Type { get; set; } // In / Out

    public int ChartOfAccountId { get; set; } // Akun kas
    public ChartOfAccount? ChartOfAccount { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public int? SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }

    public int? PurchaseInvoiceId { get; set; }
    public PurchaseInvoice? PurchaseInvoice { get; set; }

    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public decimal ExchangeRate { get; set; } = 1;

    public bool IsPosted { get; set; }
    public int? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Transaksi Giro Masuk & Keluar
/// </summary>
public class GiroTransaction
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string TransactionNumber { get; set; } = string.Empty;

    public DateTime TransactionDate { get; set; } // Tanggal terima/keluar giro
    public DateTime EffectiveDate { get; set; } // Tanggal efektif / jatuh tempo giro

    public GiroType Type { get; set; } // In / Out

    [Required, MaxLength(50)]
    public string GiroNumber { get; set; } = string.Empty; // Nomor cek/giro

    [Required, MaxLength(50)]
    public string BankName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public GiroStatus Status { get; set; } = GiroStatus.Pending; // Pending, Cleared, Bounced

    public bool IsPosted { get; set; }
    public int? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClearedAt { get; set; }
}

/// <summary>
/// Transaksi Bank Masuk & Keluar
/// </summary>
public class BankTransaction
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string TransactionNumber { get; set; } = string.Empty;

    public DateTime TransactionDate { get; set; }
    public BankTransactionType Type { get; set; }

    public int BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public decimal ExchangeRate { get; set; } = 1;

    public bool IsPosted { get; set; }
    public int? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Transfer Antar Bank
/// </summary>
public class BankTransfer
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string TransferNumber { get; set; } = string.Empty;

    public DateTime TransferDate { get; set; }

    public int SourceBankAccountId { get; set; }
    public BankAccount? SourceBankAccount { get; set; }

    public int DestinationBankAccountId { get; set; }
    public BankAccount? DestinationBankAccount { get; set; }

    public decimal Amount { get; set; }
    public decimal AdminFee { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsPosted { get; set; }
    public int? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Mutasi / Pergerakan Stok
/// </summary>
public class StockMovement
{
    [Key]
    public int Id { get; set; }

    public DateTime MovementDate { get; set; }
    public StockMovementType Type { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public decimal Quantity { get; set; } // Positif = masuk, Negatif = keluar
    public decimal UnitCost { get; set; }

    [MaxLength(300)]
    public string? Description { get; set; }

    public string? ReferenceType { get; set; } // SalesInvoice, PurchaseInvoice, etc.
    public int? ReferenceId { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum CashTransactionType { CashIn, CashOut }
public enum BankTransactionType { BankIn, BankOut }
public enum GiroType { GiroIn, GiroOut }
public enum GiroStatus { Pending, Cleared, Bounced, Canceled }
public enum StockMovementType { Purchase, Sales, Adjustment, Transfer, Return, Opening }
