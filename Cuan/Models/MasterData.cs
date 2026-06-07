using System.ComponentModel.DataAnnotations;

namespace Cuan.Models;

/// <summary>
/// Gudang & Cabang
/// </summary>
public class Warehouse
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Address { get; set; }

    public bool IsBranch { get; set; } // True jika ini cabang, false jika hanya gudang
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Mata Uang
/// </summary>
public class Currency
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(3)]
    public string Code { get; set; } = string.Empty; // IDR, USD, SGD, etc.

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(5)]
    public string? Symbol { get; set; } // Rp, $, S$

    public bool IsBaseCurrency { get; set; }
    public decimal ExchangeRate { get; set; } = 1; // Kurs terhadap mata uang dasar
    public bool IsActive { get; set; } = true;

    public ICollection<ExchangeRateHistory> RateHistories { get; set; } = new List<ExchangeRateHistory>();
}

/// <summary>
/// Riwayat kurs
/// </summary>
public class ExchangeRateHistory
{
    [Key]
    public int Id { get; set; }

    public int CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    public decimal Rate { get; set; }
    public DateTime EffectiveDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Pajak
/// </summary>
public class Tax
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Code { get; set; } = string.Empty; // PPN, PPh21, PPh23, etc.

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public decimal Rate { get; set; } // Persentase pajak, e.g., 11 for PPN 11%
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Rekening Bank
/// </summary>
public class BankAccount
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string BankName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string AccountNumber { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? AccountHolder { get; set; }

    public int? ChartOfAccountId { get; set; }
    public ChartOfAccount? ChartOfAccount { get; set; }

    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    public decimal Balance { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Satuan Barang
/// </summary>
public class UnitOfMeasure
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Code { get; set; } = string.Empty; // PCS, BOX, KG, LTR, etc.

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Kategori Barang
/// </summary>
public class ItemCategory
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    public int? ParentId { get; set; }
    public ItemCategory? Parent { get; set; }
    public ICollection<ItemCategory> Children { get; set; } = new List<ItemCategory>();

    [MaxLength(200)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}
