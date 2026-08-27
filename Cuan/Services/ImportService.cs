using ClosedXML.Excel;
using Cuan.Data;

namespace Cuan.Services;

/// <summary>Satu kolom pada template impor.</summary>
public sealed class ImportColumn<T>
{
    public required string Header { get; init; }
    public bool Required { get; init; }
    public string? Example { get; init; }
    public string? Hint { get; init; }

    /// <summary>
    /// Mengisi properti entitas dari teks sel. Lempar <see cref="ImportException"/>
    /// bila nilainya tidak bisa dipakai — pesannya tampil di baris yang bersangkutan.
    /// </summary>
    public required Action<T, string> Apply { get; init; }
}

/// <summary>Kesalahan pada satu sel atau baris yang perlu diperbaiki pengguna.</summary>
public sealed class ImportException : Exception
{
    public ImportException(string message) : base(message) { }
}

/// <summary>Satu baris hasil pembacaan berkas, lengkap dengan catatan kesalahannya.</summary>
public sealed class ImportRow<T>
{
    public int RowNumber { get; init; }
    public T Entity { get; init; } = default!;
    public List<string> Errors { get; } = new();
    public bool IsValid => Errors.Count == 0;

    /// <summary>Baris yang menimpa data yang sudah ada, bukan menambah baru.</summary>
    public bool IsUpdate { get; set; }
}

public sealed class ImportResult<T>
{
    public List<ImportRow<T>> Rows { get; } = new();
    public List<string> FileErrors { get; } = new();

    public int ValidCount => Rows.Count(r => r.IsValid);
    public int ErrorCount => Rows.Count(r => !r.IsValid);
    public int UpdateCount => Rows.Count(r => r.IsValid && r.IsUpdate);
    public int InsertCount => ValidCount - UpdateCount;
    public bool CanCommit => FileErrors.Count == 0 && ValidCount > 0;
}

public sealed class ImportSummary
{
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
}

/// <summary>
/// Rencana impor satu jenis data induk: kolom apa saja yang dibaca, bagaimana
/// baris divalidasi, dan bagaimana hasilnya disimpan.
/// </summary>
public sealed class ImportPlan<T> where T : new()
{
    public required string EntityName { get; init; }
    public required string SheetName { get; init; }
    public required List<ImportColumn<T>> Columns { get; init; }

    /// <summary>Kunci alami baris, dipakai mendeteksi data yang sudah ada dan baris kembar.</summary>
    public required Func<T, string> KeyOf { get; init; }

    /// <summary>Pemeriksaan tambahan setelah seluruh kolom terisi.</summary>
    public Func<T, string?>? Validate { get; init; }

    /// <summary>Menyimpan baris yang lolos. Dijalankan sekali untuk seluruh berkas.</summary>
    public required Func<AppDbContext, List<ImportRow<T>>, Task<ImportSummary>> Commit { get; init; }

    /// <summary>Kunci yang sudah ada di basis data, untuk menandai baris pembaruan.</summary>
    public HashSet<string> ExistingKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Catatan yang dicetak di lembar "Petunjuk" pada template.</summary>
    public List<string> Notes { get; init; } = new();
}

/// <summary>
/// Mesin impor Excel untuk seluruh data induk.
///
/// Template dan pembacaannya berasal dari definisi yang sama, jadi kolom pada
/// berkas contoh selalu cocok dengan kolom yang dibaca. Berkas dibaca lebih dulu
/// dan ditampilkan sebagai pratinjau — tidak ada yang tersimpan sebelum pengguna
/// menekan tombol impor.
/// </summary>
public class ImportService
{
    private const int HeaderRow = 1;
    private const int FirstDataRow = 2;

    /// <summary>Template .xlsx: satu lembar data berisi contoh, satu lembar petunjuk.</summary>
    public byte[] BuildTemplate<T>(ImportPlan<T> plan) where T : new()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(plan.SheetName);

        for (var i = 0; i < plan.Columns.Count; i++)
        {
            var column = plan.Columns[i];
            var cell = sheet.Cell(HeaderRow, i + 1);
            cell.Value = column.Header + (column.Required ? " *" : "");
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = column.Required
                ? XLColor.FromHtml("#0e6b4e")
                : XLColor.FromHtml("#5b6b63");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            if (!string.IsNullOrEmpty(column.Hint))
                cell.CreateComment().AddText(column.Hint);

            // Satu baris contoh supaya bentuk isiannya langsung terlihat.
            if (!string.IsNullOrEmpty(column.Example))
            {
                var sample = sheet.Cell(FirstDataRow, i + 1);
                sample.Value = column.Example;
                sample.Style.Font.Italic = true;
                sample.Style.Font.FontColor = XLColor.FromHtml("#8b978f");
            }

            // Seluruh kolom dibaca sebagai teks: kode akun seperti 1-1100 dan
            // NPWP berawalan nol tidak boleh diubah Excel menjadi angka.
            sheet.Column(i + 1).Style.NumberFormat.Format = "@";
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, plan.Columns.Count);
        foreach (var column in sheet.Columns(1, plan.Columns.Count))
            if (column.Width > 40) column.Width = 40;

        var guide = workbook.Worksheets.Add("Petunjuk");
        var row = 1;

        guide.Cell(row, 1).Value = $"Template impor {plan.EntityName}";
        guide.Cell(row, 1).Style.Font.Bold = true;
        guide.Cell(row, 1).Style.Font.FontSize = 14;
        row += 2;

        foreach (var line in new[]
        {
            $"1. Isi data mulai baris {FirstDataRow} pada lembar \"{plan.SheetName}\".",
            "2. Hapus baris contoh yang tercetak miring sebelum mengunggah.",
            "3. Kolom bertanda * wajib diisi.",
            "4. Jangan mengubah nama kolom pada baris pertama.",
            "5. Baris dengan kunci yang sudah ada akan memperbarui data lama, bukan menambah baru.",
            "6. Berkas diperiksa lebih dulu; tidak ada yang tersimpan sebelum Anda menekan tombol impor."
        })
        {
            guide.Cell(row++, 1).Value = line;
        }

        row++;
        guide.Cell(row, 1).Value = "Kolom";
        guide.Cell(row, 2).Value = "Wajib";
        guide.Cell(row, 3).Value = "Keterangan";
        guide.Row(row).Style.Font.Bold = true;
        row++;

        foreach (var column in plan.Columns)
        {
            guide.Cell(row, 1).Value = column.Header;
            guide.Cell(row, 2).Value = column.Required ? "ya" : "tidak";
            guide.Cell(row, 3).Value = column.Hint ?? "";
            row++;
        }

        if (plan.Notes.Count > 0)
        {
            row++;
            guide.Cell(row, 1).Value = "Catatan";
            guide.Cell(row, 1).Style.Font.Bold = true;
            row++;
            foreach (var note in plan.Notes) guide.Cell(row++, 1).Value = note;
        }

        guide.Columns().AdjustToContents();
        foreach (var column in guide.Columns())
            if (column.Width > 70) column.Width = 70;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Membaca berkas dan memeriksa setiap barisnya. Tidak menyentuh basis data —
    /// hasilnya dipakai untuk pratinjau.
    /// </summary>
    public ImportResult<T> Parse<T>(Stream stream, ImportPlan<T> plan) where T : new()
    {
        var result = new ImportResult<T>();

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception ex)
        {
            result.FileErrors.Add($"Berkas tidak bisa dibaca sebagai .xlsx: {ex.Message}");
            return result;
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name == plan.SheetName)
                        ?? workbook.Worksheets.FirstOrDefault(w => w.Name != "Petunjuk")
                        ?? workbook.Worksheets.FirstOrDefault();

            if (sheet is null)
            {
                result.FileErrors.Add("Berkas tidak memuat lembar kerja apa pun.");
                return result;
            }

            // Kolom dicocokkan menurut judulnya, jadi urutannya boleh berbeda.
            var headerByIndex = new Dictionary<int, ImportColumn<T>>();
            var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;

            for (var col = 1; col <= lastColumn; col++)
            {
                var header = sheet.Cell(HeaderRow, col).GetString().Trim().TrimEnd('*').Trim();
                if (header.Length == 0) continue;

                var match = plan.Columns.FirstOrDefault(c =>
                    string.Equals(c.Header, header, StringComparison.OrdinalIgnoreCase));
                if (match is not null) headerByIndex[col] = match;
            }

            var missing = plan.Columns
                .Where(c => c.Required && !headerByIndex.Values.Contains(c))
                .Select(c => c.Header)
                .ToList();

            if (missing.Count > 0)
            {
                result.FileErrors.Add(
                    $"Kolom wajib tidak ditemukan: {string.Join(", ", missing)}. " +
                    "Unduh template terbaru dan isi ulang.");
                return result;
            }

            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            var seenKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var rowNumber = FirstDataRow; rowNumber <= lastRow; rowNumber++)
            {
                var values = new Dictionary<ImportColumn<T>, string>();
                foreach (var (col, column) in headerByIndex)
                    values[column] = sheet.Cell(rowNumber, col).GetString().Trim();

                // Baris kosong dilewati diam-diam.
                if (values.Values.All(string.IsNullOrWhiteSpace)) continue;

                var row = new ImportRow<T> { RowNumber = rowNumber, Entity = new T() };

                foreach (var column in plan.Columns)
                {
                    var raw = values.GetValueOrDefault(column, string.Empty);

                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        if (column.Required) row.Errors.Add($"{column.Header} wajib diisi");
                        continue;
                    }

                    try
                    {
                        column.Apply(row.Entity, raw);
                    }
                    catch (ImportException ex)
                    {
                        row.Errors.Add($"{column.Header}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        row.Errors.Add($"{column.Header}: nilai \"{raw}\" tidak dikenali ({ex.Message})");
                    }
                }

                if (row.Errors.Count == 0 && plan.Validate is not null)
                {
                    var problem = plan.Validate(row.Entity);
                    if (problem is not null) row.Errors.Add(problem);
                }

                if (row.Errors.Count == 0)
                {
                    var key = plan.KeyOf(row.Entity);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        row.Errors.Add("Kunci baris kosong");
                    }
                    else if (seenKeys.TryGetValue(key, out var firstRow))
                    {
                        row.Errors.Add($"Kunci \"{key}\" kembar dengan baris {firstRow}");
                    }
                    else
                    {
                        seenKeys[key] = rowNumber;
                        row.IsUpdate = plan.ExistingKeys.Contains(key);
                    }
                }

                result.Rows.Add(row);
            }

            if (result.Rows.Count == 0)
                result.FileErrors.Add("Tidak ada baris data yang bisa dibaca. Pastikan pengisian dimulai dari baris 2.");
        }

        return result;
    }

    // ---------- Pembantu penguraian nilai sel ----------

    public static decimal ParseDecimal(string raw, string field)
    {
        // Terima format Indonesia (1.500.000,50) maupun format titik desimal.
        var cleaned = raw.Replace(" ", "").Replace("Rp", "", StringComparison.OrdinalIgnoreCase);

        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');

        if (lastComma >= 0 && lastComma > lastDot)
            cleaned = cleaned.Replace(".", "").Replace(',', '.');
        else if (lastDot >= 0 && lastComma >= 0)
            cleaned = cleaned.Replace(",", "");
        else if (lastComma >= 0)
            cleaned = cleaned.Replace(',', '.');

        if (decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            return value;

        throw new ImportException($"\"{raw}\" bukan angka");
    }

    public static int ParseInt(string raw, string field) => (int)ParseDecimal(raw, field);

    public static bool ParseBool(string raw)
    {
        var v = raw.Trim().ToLowerInvariant();
        return v switch
        {
            "ya" or "y" or "true" or "1" or "aktif" or "yes" => true,
            "tidak" or "t" or "false" or "0" or "nonaktif" or "no" => false,
            _ => throw new ImportException($"\"{raw}\" bukan ya/tidak")
        };
    }

    public static DateTime ParseDate(string raw)
    {
        string[] formats = { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy" };
        if (DateTime.TryParseExact(raw, formats, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var exact))
            return exact;
        if (DateTime.TryParse(raw, out var loose)) return loose;
        throw new ImportException($"\"{raw}\" bukan tanggal (pakai dd/MM/yyyy)");
    }

    /// <summary>Mencocokkan teks dengan daftar acuan, mis. nama kategori ke id-nya.</summary>
    public static int Lookup(IDictionary<string, int> map, string raw, string what)
    {
        if (map.TryGetValue(raw.Trim(), out var id)) return id;
        var known = string.Join(", ", map.Keys.Take(8));
        throw new ImportException($"{what} \"{raw}\" tidak ditemukan. Pilihan: {known}…");
    }
}
