using System.ComponentModel.DataAnnotations;

namespace Cuan.Models;

/// <summary>
/// Faktur Pajak keluaran — dokumen resmi PPN atas sebuah faktur penjualan.
/// Nomor seri (NSFP) diambil dari jatah yang diberikan DJP.
/// </summary>
public class TaxInvoice
{
    [Key]
    public int Id { get; set; }

    /// <summary>Nomor Seri Faktur Pajak, format 010.000-26.00000001</summary>
    [Required, MaxLength(30)]
    public string FakturNumber { get; set; } = string.Empty;

    /// <summary>Kode transaksi DJP: 01 penyerahan biasa, 02 kepada pemungut, dst.</summary>
    [MaxLength(2)]
    public string TransactionCode { get; set; } = "01";

    /// <summary>0 = normal, 1 = pengganti.</summary>
    [MaxLength(1)]
    public string StatusCode { get; set; } = "0";

    public DateTime FakturDate { get; set; }
    public int TaxPeriodMonth { get; set; }
    public int TaxPeriodYear { get; set; }

    public int? SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    [MaxLength(30)]
    public string? BuyerNpwp { get; set; }

    [MaxLength(200)]
    public string? BuyerName { get; set; }

    [MaxLength(400)]
    public string? BuyerAddress { get; set; }

    /// <summary>Dasar Pengenaan Pajak.</summary>
    public decimal Dpp { get; set; }
    public decimal PpnAmount { get; set; }
    public decimal PpnbmAmount { get; set; }
    public decimal PpnRate { get; set; }

    /// <summary>Nomor faktur pengganti, diisi bila StatusCode = 1.</summary>
    [MaxLength(30)]
    public string? ReplacedFakturNumber { get; set; }

    public TaxInvoiceStatus Status { get; set; } = TaxInvoiceStatus.Draft;

    /// <summary>Nomor approval dari DJP setelah faktur diterima.</summary>
    [MaxLength(60)]
    public string? DjpApprovalCode { get; set; }
    public DateTime? DjpSubmittedAt { get; set; }

    [MaxLength(1000)]
    public string? DjpMessage { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum TaxInvoiceStatus
{
    Draft,        // Dibuat, belum dilaporkan
    Issued,       // Sudah diterbitkan dan diberi NSFP
    Submitted,    // Sudah dikirim ke DJP
    Approved,     // Disetujui DJP
    Rejected,     // Ditolak DJP
    Canceled      // Dibatalkan
}

/// <summary>
/// Bukti potong PPh (21 / 23 / 4 ayat 2) yang diterbitkan atau diterima.
/// </summary>
public class WithholdingTax
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(30)]
    public string SlipNumber { get; set; } = string.Empty;

    /// <summary>Jenis pajak: PPh21, PPh23, PPh4-2, PPh22.</summary>
    [MaxLength(20)]
    public string TaxType { get; set; } = "PPh23";

    public DateTime SlipDate { get; set; }
    public int TaxPeriodMonth { get; set; }
    public int TaxPeriodYear { get; set; }

    /// <summary>True bila perusahaan yang memotong, false bila dipotong pihak lain.</summary>
    public bool IsWithholder { get; set; } = true;

    [MaxLength(200)]
    public string PartyName { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? PartyNpwp { get; set; }

    public int? CustomerId { get; set; }
    public int? SupplierId { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }

    public decimal Dpp { get; set; }
    public decimal Rate { get; set; }
    public decimal TaxAmount { get; set; }

    public bool IsReported { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// SPT Masa PPN untuk satu masa pajak — ringkasan pajak keluaran, pajak
/// masukan, dan posisi kurang / lebih bayar.
/// </summary>
public class TaxReturn
{
    [Key]
    public int Id { get; set; }

    public int PeriodMonth { get; set; }
    public int PeriodYear { get; set; }

    [MaxLength(20)]
    public string ReturnType { get; set; } = "PPN"; // PPN, PPh21, PPh23, PPh25

    /// <summary>0 = normal, 1..n = pembetulan ke-n.</summary>
    public int Revision { get; set; }

    public decimal OutputDpp { get; set; }
    public decimal OutputTax { get; set; }
    public decimal InputDpp { get; set; }
    public decimal InputTax { get; set; }

    /// <summary>Kompensasi kelebihan pajak dari masa sebelumnya.</summary>
    public decimal CarryForward { get; set; }

    /// <summary>Positif = kurang bayar, negatif = lebih bayar.</summary>
    public decimal PayableAmount { get; set; }

    public TaxReturnStatus Status { get; set; } = TaxReturnStatus.Draft;

    /// <summary>Nomor Tanda Terima Elektronik dari DJP.</summary>
    [MaxLength(60)]
    public string? NtteNumber { get; set; }

    /// <summary>Nomor Transaksi Penerimaan Negara atas setoran pajaknya.</summary>
    [MaxLength(60)]
    public string? NtpnNumber { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public DateTime? PaidAt { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum TaxReturnStatus
{
    Draft,
    Submitted,
    Accepted,
    Paid,
    Rejected
}

/// <summary>
/// Catatan setiap percakapan dengan layanan DJP — dipakai untuk menelusuri
/// kembali apa yang dikirim dan apa jawabannya.
/// </summary>
public class DjpSubmission
{
    [Key]
    public long Id { get; set; }

    [MaxLength(40)]
    public string Operation { get; set; } = string.Empty; // TestConnection, SubmitFaktur, SubmitSpt

    [MaxLength(40)]
    public string? DocumentNumber { get; set; }

    [MaxLength(20)]
    public string Mode { get; set; } = "Sandbox";

    [MaxLength(300)]
    public string? Endpoint { get; set; }

    public bool Success { get; set; }
    public int? HttpStatus { get; set; }

    public string? RequestPayload { get; set; }
    public string? ResponsePayload { get; set; }

    [MaxLength(1000)]
    public string? Message { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
