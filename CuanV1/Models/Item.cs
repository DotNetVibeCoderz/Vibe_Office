using System.ComponentModel.DataAnnotations;

namespace Cuan.Models;

/// <summary>
/// Master Barang / Inventori
/// </summary>
public class Item
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string ItemCode { get; set; } = string.Empty; // SKU / Kode Barang

    [Required, MaxLength(200)]
    public string ItemName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int CategoryId { get; set; }
    public ItemCategory? Category { get; set; }

    public int UnitOfMeasureId { get; set; }
    public UnitOfMeasure? UnitOfMeasure { get; set; }

    public int? PurchaseUnitId { get; set; }
    public UnitOfMeasure? PurchaseUnit { get; set; }
    public decimal PurchaseConversionRate { get; set; } = 1; // Konversi satuan beli ke satuan dasar

    public int? SalesUnitId { get; set; }
    public UnitOfMeasure? SalesUnit { get; set; }
    public decimal SalesConversionRate { get; set; } = 1;

    // Harga
    public decimal CostPrice { get; set; } // HPP / Harga Beli rata-rata
    public decimal SellingPrice { get; set; } // Harga Jual default
    public decimal MinimumPrice { get; set; }
    public decimal MaximumPrice { get; set; }

    // Stok
    public decimal StockQuantity { get; set; }
    public decimal MinimumStock { get; set; }
    public decimal MaximumStock { get; set; }

    // Pajak
    public int? PurchaseTaxId { get; set; }
    public Tax? PurchaseTax { get; set; }
    public int? SalesTaxId { get; set; }
    public Tax? SalesTax { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsService { get; set; } // True jika jasa (tidak ada stok)
    public bool HasBatch { get; set; } // True jika pakai nomor batch
    public bool HasExpiry { get; set; } // True jika ada kadaluarsa

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigasi ke stok per gudang
    public ICollection<ItemStock> ItemStocks { get; set; } = new List<ItemStock>();
}

/// <summary>
/// Stok per gudang
/// </summary>
public class ItemStock
{
    [Key]
    public int Id { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public decimal Quantity { get; set; }
    public decimal ReservedQuantity { get; set; } // Sudah dipesan tapi belum dikirim

    public decimal AvailableQuantity => Quantity - ReservedQuantity;

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
