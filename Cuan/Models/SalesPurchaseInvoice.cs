using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cuan.Models;

/// <summary>
/// Faktur Penjualan
/// </summary>
public class SalesInvoice
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string InvoiceNumber { get; set; } = string.Empty; // Auto: "INV-2024-0001"

    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public decimal ExchangeRate { get; set; } = 1;

    [MaxLength(300)]
    public string? Description { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount => GrandTotal - PaidAmount;

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public bool IsPosted { get; set; }
    public int? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<SalesInvoiceDetail> Details { get; set; } = new List<SalesInvoiceDetail>();
}

public class SalesInvoiceDetail
{
    [Key]
    public int Id { get; set; }

    public int SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    [MaxLength(300)]
    public string? Description { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}

/// <summary>
/// Faktur Pembelian
/// </summary>
public class PurchaseInvoice
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string InvoiceNumber { get; set; } = string.Empty;

    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public decimal ExchangeRate { get; set; } = 1;

    [MaxLength(300)]
    public string? Description { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public bool IsPosted { get; set; }
    public int? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<PurchaseInvoiceDetail> Details { get; set; } = new List<PurchaseInvoiceDetail>();
}

public class PurchaseInvoiceDetail
{
    [Key]
    public int Id { get; set; }

    public int PurchaseInvoiceId { get; set; }
    public PurchaseInvoice? PurchaseInvoice { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    [MaxLength(300)]
    public string? Description { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public enum InvoiceStatus
{
    Draft,
    Confirmed,
    PartialPaid,
    Paid,
    Canceled
}
