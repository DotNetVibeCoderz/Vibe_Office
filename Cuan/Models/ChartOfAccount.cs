using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cuan.Models;

/// <summary>
/// Chart of Accounts - Daftar Akun (COA)
/// Mengikuti standar akuntansi dengan kode akun bertingkat
/// </summary>
public class ChartOfAccount
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string AccountCode { get; set; } = string.Empty; // e.g., "1-1000", "4-1000"

    [Required, MaxLength(200)]
    public string AccountName { get; set; } = string.Empty;

    public int AccountType { get; set; } // 1=Asset, 2=Liability, 3=Equity, 4=Revenue, 5=Expense

    public int? ParentId { get; set; }
    
    [ForeignKey(nameof(ParentId))]
    public ChartOfAccount? Parent { get; set; }
    public ICollection<ChartOfAccount> Children { get; set; } = new List<ChartOfAccount>();

    public bool IsActive { get; set; } = true;
    public bool IsHeader { get; set; } // True jika ini adalah header/grup, bukan akun transaksi
    public decimal OpeningBalance { get; set; }
    public decimal CurrentBalance { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
