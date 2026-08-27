using System.ComponentModel.DataAnnotations;

namespace Cuan.Models;

/// <summary>
/// Customer / Pelanggan
/// </summary>
public class Customer
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string CustomerCode { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Province { get; set; }

    [MaxLength(20)]
    public string? PostalCode { get; set; }

    [MaxLength(100)]
    public string? Country { get; set; } = "Indonesia";

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(50)]
    public string? TaxNumber { get; set; } // NPWP

    public int? PaymentTermId { get; set; }
    public PaymentTerm? PaymentTerm { get; set; }

    public decimal CreditLimit { get; set; }
    public decimal Balance { get; set; } // Saldo piutang

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Supplier / Pemasok
/// </summary>
public class Supplier
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string SupplierCode { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Province { get; set; }

    [MaxLength(20)]
    public string? PostalCode { get; set; }

    [MaxLength(100)]
    public string? Country { get; set; } = "Indonesia";

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(50)]
    public string? TaxNumber { get; set; } // NPWP

    public int? PaymentTermId { get; set; }
    public PaymentTerm? PaymentTerm { get; set; }

    public decimal Balance { get; set; } // Saldo hutang

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Term pembayaran
/// </summary>
public class PaymentTerm
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty; // e.g., "Net 30", "COD", "2/10 Net 30"

    public int DueDays { get; set; } // Jatuh tempo dalam hari
    public decimal DiscountPercent { get; set; } // Diskon jika bayar lebih awal
    public int DiscountDays { get; set; } // Masa berlaku diskon

    public bool IsActive { get; set; } = true;
}
