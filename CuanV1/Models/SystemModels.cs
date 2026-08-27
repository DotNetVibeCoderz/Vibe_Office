using System.ComponentModel.DataAnnotations;

namespace Cuan.Models;

/// <summary>
/// Audit Trail - Catatan perubahan transaksi
/// </summary>
public class AuditLog
{
    [Key]
    public long Id { get; set; }

    [MaxLength(50)]
    public string? UserId { get; set; }

    [MaxLength(100)]
    public string? UserName { get; set; }

    [MaxLength(20)]
    public string Action { get; set; } = string.Empty; // CREATE, UPDATE, DELETE, LOGIN, LOGOUT

    [MaxLength(100)]
    public string? EntityName { get; set; } // Nama tabel/entity

    public string? EntityId { get; set; } // ID record

    [MaxLength(500)]
    public string? Description { get; set; }

    public string? OldValues { get; set; } // JSON
    public string? NewValues { get; set; } // JSON

    [MaxLength(50)]
    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// API Key untuk integrasi eksternal
/// </summary>
public class ApiKey
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string KeyName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string KeyValue { get; set; } = string.Empty; // Hashed

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    [MaxLength(200)]
    public string? AllowedIps { get; set; } // Comma-separated
}

/// <summary>
/// Pengaturan Sistem (bisa di-set dari UI)
/// </summary>
public class SystemSetting
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string SettingKey { get; set; } = string.Empty;

    public string SettingValue { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Group { get; set; } // General, Accounting, Tax, etc.

    [MaxLength(200)]
    public string? Description { get; set; }

    public bool IsEditable { get; set; } = true;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Notifikasi
/// </summary>
public class Notification
{
    [Key]
    public long Id { get; set; }

    [MaxLength(50)]
    public string? UserId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string? Message { get; set; }

    [MaxLength(50)]
    public string? Type { get; set; } // Info, Warning, Success, Error

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(200)]
    public string? Link { get; set; } // Optional link to related page
}

/// <summary>
/// Periode Pembukuan
/// </summary>
public class AccountingPeriod
{
    [Key]
    public int Id { get; set; }

    [MaxLength(50)]
    public string Name { get; set; } = string.Empty; // e.g., "Tahun Buku 2024"

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; }
    public bool IsClosed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
