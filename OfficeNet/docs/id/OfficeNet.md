# API terpadu

Satu titik masuk ketika Anda tidak tahu — atau tidak peduli — sebuah berkas berformat apa.

*English: [docs/OfficeNet.md](../OfficeNet.md)* · [Kembali ke indeks](README.md)

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

```bash
dotnet add package Gravicode.OfficeNet
```

Paket `Gravicode.OfficeNet` merujuk keempat library format dan menambahkan kelas statis `Office`.
Pasang paket ini kalau Anda menangani apa pun yang diunggah pengguna; pasang paket masing-masing
kalau formatnya sudah pasti dan Anda ingin graf dependensi yang lebih ramping.

---

## Mendeteksi format

```csharp
using OfficeNet;

var format = Office.DetectFormat("berkas.docx");   // path, Stream, atau byte[]

Console.WriteLine(format);   // Word, Excel, PowerPoint, Pdf, atau Unknown
```

Deteksi membaca isinya, bukan ekstensinya. Berkas `.docx` yang sebenarnya `.xlsx` — yang terjadi
setiap kali ada orang mengganti nama berkas agar lolos penyaring unggahan — dilaporkan sebagai Excel.

```csharp
if (Office.IsSupportedExtension(path))
{
    // …
}

Console.WriteLine(string.Join(", ", Office.SupportedExtensions));
```

## Membuka tanpa tahu tipenya

```csharp
using var document = Office.Open("berkas.pptx");   // IOfficeDocument

Console.WriteLine(document.ExtractText());
Console.WriteLine(document.Properties.Title);

switch (document)
{
    case WordNet.WordDocument word:
        Console.WriteLine($"{word.WordCount} kata");
        break;

    case ExcelNet.Workbook workbook:
        Console.WriteLine($"{workbook.Count} lembar");
        break;

    case PowerPointNet.Presentation deck:
        Console.WriteLine($"{deck.SlideCount} slide");
        break;
}
```

`IOfficeDocument` membawa apa yang benar-benar dimiliki bersama ketiga format OOXML — ekstraksi teks,
properti inti, penyimpanan — dan tidak lebih. Apa pun yang khas per format membutuhkan tipe
konkretnya, dan untuk itulah pencocokan pola di atas ada.

## Mengekstrak teks dari apa saja

```csharp
string text = Office.ExtractText("berkas.pdf");
```

Berlaku untuk `.docx`, `.xlsx`, `.pptx`, dan `.pdf`. Inilah satu baris di balik sebagian besar
pekerjaan pengindeksan pencarian.

Akhiran baris selalu `\n`, di setiap platform. Kedengarannya sepele padahal tidak: memakai
`StringBuilder.AppendLine` akan menghasilkan `\r\n` di Windows dan `\n` di Linux, sehingga dokumen
yang sama menghasilkan teks — dan hash — yang berbeda tergantung di mana pekerjaannya berjalan.

## Mengonversi ke PDF

```csharp
Office.ConvertToPdf("laporan.docx");                    // → laporan.pdf
Office.ConvertToPdf("data.xlsx", "keluaran/data.pdf");
```

Mengembalikan path yang ditulisnya. Word, Excel, dan PowerPoint masing-masing dikonversi lewat
pengekspornya sendiri; masukan `.pdf` disalin.

## Konverter batch

```csharp
using OfficeNet;

var failures = new List<(string Path, string Reason)>();

foreach (var path in Directory.EnumerateFiles("masuk", "*.*", SearchOption.AllDirectories))
{
    if (!Office.IsSupportedExtension(path))
    {
        continue;
    }

    try
    {
        var pdf = Office.ConvertToPdf(path, Path.Combine("keluar",
            Path.GetFileNameWithoutExtension(path) + ".pdf"));

        Console.WriteLine($"OK    {Path.GetFileName(pdf)}");
    }
    catch (OfficeNetException ex)
    {
        // Satu berkas cacat dari seribu tidak boleh menghentikan 999 sisanya.
        failures.Add((path, ex.Message));
        Console.WriteLine($"GAGAL {Path.GetFileName(path)}: {ex.Message}");
    }
}
```

Versi yang benar-benar berjalan ada di [`samples/BatchConverter.Console`](../../samples).

## Thumbnail untuk unggahan

Dipadukan dengan [`OfficeNet.Rendering`](Rendering.md):

```csharp
using OfficeNet;
using OfficeNet.Rendering;

if (!Office.IsSupportedExtension(uploaded))
{
    return Results.BadRequest("Format tidak didukung.");
}

var png = DocumentRenderer.RenderThumbnail(uploaded, widthPixels: 320);
return Results.File(png, "image/png");
```

## Lihat juga

- [Konsep inti](Core.md) — apa yang dimiliki bersama format-formatnya
- [Rendering](Rendering.md) — gambar dari format mana pun
- Empat panduan format: [WordNet](WordNet.md) · [ExcelNet](ExcelNet.md) ·
  [PowerPointNet](PowerPointNet.md) · [PdfNet](PdfNet.md)
