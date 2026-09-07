// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using BenchmarkDotNet.Attributes;
using ExcelNet.Styles;
using ExcelNet;
using OfficeNet.Core.Charts;
using OfficeNet.Core.Drawing;
using OfficeNet.Core;
using PdfNet.Document;
using PowerPointNet.Charts;
using PowerPointNet;
using WordNet;

namespace OfficeNet.Benchmarks;

/// <summary>
/// Shared setup: benchmarks write to memory, never to disk.
/// </summary>
/// <remarks>
/// A benchmark that touches the file system measures the file system. Every case here serialises
/// into a <see cref="MemoryStream"/>, so what is timed is the library's own work — XML generation,
/// the OPC container, compression — and the numbers do not change between an SSD and a network
/// share.
/// </remarks>
[MemoryDiagnoser]
public abstract class DocumentBenchmark
{
    protected static readonly string[] Words =
    [
        "pendapatan", "wilayah", "pertumbuhan", "laporan", "tahunan", "Jakarta", "Bandung",
        "Surabaya", "Medan", "kuartal", "operasional", "otomasi", "pelaporan", "target",
    ];

    protected static string Sentence(int seed, int words = 12)
    {
        var random = new Random(seed);
        return string.Join(' ', Enumerable.Range(0, words).Select(_ => Words[random.Next(Words.Length)]));
    }

    protected static byte[] ToBytes(Action<Stream> save)
    {
        using var stream = new MemoryStream();
        save(stream);
        return stream.ToArray();
    }
}

// ---- WordNet ------------------------------------------------------------------------------------

/// <summary>Creating, reading and converting <c>.docx</c> documents.</summary>
public class WordBenchmarks : DocumentBenchmark
{
    private byte[] _document = [];

    /// <summary>Paragraph counts spanning a memo, a report and a small book chapter.</summary>
    [Params(100, 1_000, 10_000)]
    public int Paragraphs { get; set; }

    [GlobalSetup]
    public void Setup() => _document = ToBytes(stream =>
    {
        using var document = Build();
        document.Save(stream);
    });

    private WordDocument Build()
    {
        var document = WordDocument.Create();
        document.AddHeading("Laporan Tahunan", 0);

        for (var i = 0; i < Paragraphs; i++)
        {
            if (i % 20 == 0)
            {
                document.AddHeading($"Bagian {i / 20 + 1}", 1);
            }

            document.AddParagraph(Sentence(i));
        }

        return document;
    }

    [Benchmark(Description = "Create + save")]
    public int Create()
    {
        using var document = Build();
        return ToBytes(document.Save).Length;
    }

    [Benchmark(Description = "Open")]
    public int Open()
    {
        using var document = WordDocument.Open(_document);
        return document.Paragraphs.Count;
    }

    [Benchmark(Description = "Extract text")]
    public int ExtractText()
    {
        using var document = WordDocument.Open(_document);
        return document.ExtractText().Length;
    }

    [Benchmark(Description = "Export to PDF")]
    public int ToPdf()
    {
        using var document = WordDocument.Open(_document);
        return ToBytes(stream => document.SaveAsPdf(stream)).Length;
    }
}

/// <summary>Table-heavy documents, which stress a different path from flowed text.</summary>
public class WordTableBenchmarks : DocumentBenchmark
{
    [Params(50, 500)]
    public int Rows { get; set; }

    [Benchmark(Description = "Table via AddTable(string[][])")]
    public int BulkTable()
    {
        using var document = WordDocument.Create();

        var data = Enumerable.Range(0, Rows)
            .Select(r => new[] { $"Baris {r}", $"{r * 37}", $"{r * 91}", $"+{r % 50}%" });

        document.AddTable(data);
        return ToBytes(document.Save).Length;
    }

    [Benchmark(Description = "Table cell by cell")]
    public int CellByCell()
    {
        using var document = WordDocument.Create();
        var table = document.AddTable(Rows, 4);

        for (var r = 0; r < Rows; r++)
        {
            table[r, 0].Text = $"Baris {r}";
            table[r, 1].Text = $"{r * 37}";
            table[r, 2].Text = $"{r * 91}";
            table[r, 3].Text = $"+{r % 50}%";
        }

        return ToBytes(document.Save).Length;
    }
}

// ---- ExcelNet -----------------------------------------------------------------------------------

/// <summary>Writing, reading and recalculating workbooks.</summary>
public class ExcelBenchmarks : DocumentBenchmark
{
    private byte[] _workbook = [];

    [Params(1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup() => _workbook = ToBytes(stream =>
    {
        using var workbook = Build(withFormulas: false);
        workbook.Save(stream);
    });

    private Workbook Build(bool withFormulas)
    {
        var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        sheet.WriteHeader("A1", ["Tanggal", "Produk", "Qty", "Harga", "Total"]);

        var start = new DateTime(2026, 1, 1);

        for (var i = 0; i < Rows; i++)
        {
            var row = i + 1;
            sheet[row, 0].Set(start.AddDays(i % 365));
            sheet[row, 1].Set(Words[i % Words.Length]);
            sheet[row, 2].Set(i % 40 + 1);
            sheet[row, 3].Set((i % 200 + 60) * 1000.0);

            if (withFormulas)
            {
                sheet[row, 4].SetFormula($"C{row + 1}*D{row + 1}");
            }
            else
            {
                sheet[row, 4].Set((i % 40 + 1) * ((i % 200 + 60) * 1000.0));
            }
        }

        return workbook;
    }

    [Benchmark(Description = "Write values")]
    public int WriteValues()
    {
        using var workbook = Build(withFormulas: false);
        return ToBytes(workbook.Save).Length;
    }

    [Benchmark(Description = "Write + recalculate formulas")]
    public int WriteFormulas()
    {
        using var workbook = Build(withFormulas: true);
        workbook.Recalculate();
        return ToBytes(workbook.Save).Length;
    }

    [Benchmark(Description = "Open")]
    public int Open()
    {
        using var workbook = Workbook.Open(_workbook);
        return workbook["Data"].CellCount;
    }

    [Benchmark(Description = "Open + sum a column")]
    public double SumColumn()
    {
        using var workbook = Workbook.Open(_workbook);
        var sheet = workbook["Data"];
        var total = 0.0;

        for (var row = 1; row <= Rows; row++)
        {
            total += sheet[row, 4].Number;
        }

        return total;
    }
}

/// <summary>Styling, which goes through the stylesheet's deduplication.</summary>
public class ExcelStyleBenchmarks : DocumentBenchmark
{
    [Params(10_000)]
    public int Cells { get; set; }

    [Benchmark(Description = "One shared style")]
    public int SharedStyle()
    {
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        var style = CellStyle.Default.Bold().WithNumberFormat(NumberFormats.Rupiah);

        for (var i = 0; i < Cells; i++)
        {
            sheet[i, 0].Set(i * 1000.0).WithStyle(style);
        }

        return ToBytes(workbook.Save).Length;
    }

    [Benchmark(Description = "A distinct style per 100 cells")]
    public int VariedStyles()
    {
        using var workbook = Workbook.Create("Data");
        var sheet = workbook["Data"];

        for (var i = 0; i < Cells; i++)
        {
            // The stylesheet deduplicates identical styles, so this measures how well it copes
            // when the caller does not cooperate.
            var style = CellStyle.Default
                .Bold(i % 2 == 0)
                .WithBackground(OfficeColor.FromRgb((byte)(i / 100 % 256), 0x80, 0x40));

            sheet[i, 0].Set(i).WithStyle(style);
        }

        return ToBytes(workbook.Save).Length;
    }
}

// ---- PowerPointNet ------------------------------------------------------------------------------

/// <summary>Building decks, including native charts.</summary>
public class PowerPointBenchmarks : DocumentBenchmark
{
    private byte[] _deck = [];

    [Params(20, 200)]
    public int Slides { get; set; }

    [GlobalSetup]
    public void Setup() => _deck = ToBytes(stream =>
    {
        using var deck = Build();
        deck.Save(stream);
    });

    private Presentation Build()
    {
        var deck = Presentation.Create();
        deck.AddTitleSlide("Laporan", "Gravicode Studios");

        for (var i = 0; i < Slides; i++)
        {
            deck.AddBulletSlide($"Bagian {i + 1}",
                Enumerable.Range(0, 5).Select(b => Sentence(i * 5 + b, 8)));
        }

        return deck;
    }

    [Benchmark(Description = "Create + save")]
    public int Create()
    {
        using var deck = Build();
        return ToBytes(deck.Save).Length;
    }

    [Benchmark(Description = "Open")]
    public int Open()
    {
        using var deck = Presentation.Open(_deck);
        return deck.SlideCount;
    }

    [Benchmark(Description = "Export to PDF")]
    public int ToPdf()
    {
        using var deck = Presentation.Open(_deck);
        return ToBytes(stream => deck.SaveAsPdf(stream)).Length;
    }
}

/// <summary>Charts: writing the part, and reading it back.</summary>
public class ChartBenchmarks : DocumentBenchmark
{
    private byte[] _deck = [];

    [Params(10, 100)]
    public int Points { get; set; }

    private ChartData Data() => new()
    {
        Type = ChartType.Column,
        Categories = [.. Enumerable.Range(0, Points).Select(i => $"K{i}")],
        Series =
        [
            new ChartSeries("2025", [.. Enumerable.Range(0, Points).Select(i => (double)(i * 13 % 900))]),
            new ChartSeries("2026", [.. Enumerable.Range(0, Points).Select(i => (double)(i * 17 % 1200))]),
        ],
        ValueFormat = "#,##0",
        ShowDataLabels = true,
    };

    [GlobalSetup]
    public void Setup() => _deck = ToBytes(stream =>
    {
        using var deck = Presentation.Create();
        deck.AddSlide(5).AddChart(Data());
        deck.Save(stream);
    });

    [Benchmark(Description = "Write chart part")]
    public int Write()
    {
        using var deck = Presentation.Create();
        deck.AddSlide(5).AddChart(Data());
        return ToBytes(deck.Save).Length;
    }

    [Benchmark(Description = "Read chart back")]
    public int Read()
    {
        using var deck = Presentation.Open(_deck);
        return deck.Slides[0].Charts.First().GetData().Series.Count;
    }
}

// ---- PdfNet -------------------------------------------------------------------------------------

/// <summary>Drawing, merging, splitting and extracting.</summary>
public class PdfBenchmarks : DocumentBenchmark
{
    private byte[] _pdf = [];

    [Params(10, 100)]
    public int Pages { get; set; }

    [GlobalSetup]
    public void Setup() => _pdf = ToBytes(stream =>
    {
        using var document = Build();
        document.Save(stream);
    });

    private PdfDocument Build()
    {
        var document = PdfDocument.Create();

        for (var p = 0; p < Pages; p++)
        {
            var page = document.Pages.Add(PageSize.A4);
            using var canvas = page.OpenCanvas();

            canvas.TopDown = true;
            canvas.SetFont(PdfNet.Content.StandardFont.Helvetica, 11);

            for (var line = 0; line < 40; line++)
            {
                canvas.DrawText(Sentence(p * 40 + line), 56, 60 + (line * 18));
            }
        }

        return document;
    }

    [Benchmark(Description = "Draw + save")]
    public int Create()
    {
        using var document = Build();
        return ToBytes(document.Save).Length;
    }

    [Benchmark(Description = "Open")]
    public int Open()
    {
        using var document = PdfDocument.Open(_pdf);
        return document.Pages.Count;
    }

    [Benchmark(Description = "Extract text")]
    public int ExtractText()
    {
        using var document = PdfDocument.Open(_pdf);
        return document.ExtractText().Length;
    }

    [Benchmark(Description = "Merge two copies")]
    public int Merge()
    {
        using var a = PdfDocument.Open(_pdf);
        using var b = PdfDocument.Open(_pdf);

        a.Merge(b);
        return ToBytes(a.Save).Length;
    }

    [Benchmark(Description = "Split into single pages")]
    public int Split()
    {
        using var document = PdfDocument.Open(_pdf);
        var parts = document.Split();

        try
        {
            return parts.Count;
        }
        finally
        {
            foreach (var part in parts)
            {
                part.Dispose();
            }
        }
    }

    [Benchmark(Description = "Encrypt (AES-256)")]
    public int Encrypt()
    {
        using var document = PdfDocument.Open(_pdf);
        document.Encrypt("rahasia");
        return ToBytes(document.Save).Length;
    }
}

// ---- Cross-cutting ------------------------------------------------------------------------------

/// <summary>
/// The OPC container itself, which every OOXML format pays for.
/// </summary>
/// <remarks>
/// Worth isolating: if opening a small <c>.docx</c> is slow, the question is whether the cost is in
/// WordprocessingML or in the ZIP and the relationship graph underneath. This answers it.
/// </remarks>
public class PackageBenchmarks : DocumentBenchmark
{
    private byte[] _small = [];

    [GlobalSetup]
    public void Setup() => _small = ToBytes(stream =>
    {
        using var document = WordDocument.Create();
        document.AddParagraph("Halo dunia.");
        document.Save(stream);
    });

    [Benchmark(Description = "Open a minimal .docx")]
    public int OpenSmall()
    {
        using var document = WordDocument.Open(_small);
        return document.Paragraphs.Count;
    }

    [Benchmark(Description = "Detect format from bytes")]
    public OfficeFormat Detect() => Office.DetectFormat(_small);

    [Benchmark(Description = "Round trip, no edits")]
    public int RoundTrip()
    {
        using var document = WordDocument.Open(_small);
        return ToBytes(document.Save).Length;
    }
}
