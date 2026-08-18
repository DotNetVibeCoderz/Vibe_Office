using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using VibeDesk.Application.Documents;
using VibeDesk.Office;
using Xunit;

namespace VibeDesk.Tests.Office;

/// <summary>
/// Whether the exported files are valid OOXML, not merely readable by our own importer.
/// </summary>
/// <remarks>
/// Round-tripping through <see cref="OfficeConverterTests"/> proves the two halves agree with each
/// other, which they would even if both were wrong in the same way. This runs the SDK's schema
/// validator instead — the same rules Word, Excel and PowerPoint apply when deciding whether to open
/// a file or offer to repair it.
/// </remarks>
public class OfficeValidityTests
{
    private readonly IOfficeConverter _converter = new OfficeConverter();
    private readonly OpenXmlValidator _validator = new();

    private static MemoryStream Write(Action<Stream> write)
    {
        var stream = new MemoryStream();
        write(stream);
        stream.Position = 0;
        return stream;
    }

    private void AssertValid(IEnumerable<ValidationErrorInfo> errors)
    {
        var listed = errors
            .Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}")
            .ToList();

        Assert.Empty(listed);
    }

    [Fact]
    public void AnExportedDocumentIsValidWordML()
    {
        var model = new DocumentModel
        {
            Html = "<h1>Judul</h1><p>Isi <b>tebal</b>.</p><ul><li>Satu</li></ul>" +
                   "<ol><li>Pertama</li></ol><table><tr><td>A</td><td>B</td></tr></table>",
        };

        using var file = Write(s => _converter.WriteWord(s, model, "Laporan"));
        using var package = WordprocessingDocument.Open(file, isEditable: false);

        AssertValid(_validator.Validate(package));
    }

    [Fact]
    public void AnExportedWorkbookIsValidSpreadsheetML()
    {
        var model = new SpreadsheetModel
        {
            Sheets =
            [
                new SheetTab
                {
                    Name = "Data",
                    Cells = new()
                    {
                        ["A1"] = new Cell { V = "Nama" },
                        ["B1"] = new Cell { V = "Nilai" },
                        ["A2"] = new Cell { V = "Jakarta, Indonesia" },
                        ["B2"] = new Cell { V = "4000" },
                        ["B3"] = new Cell { F = "=SUM(B2:B2)", V = "4000" },
                        ["C1"] = new Cell { V = "TRUE" },
                    },
                },
                new SheetTab { Name = "Kosong" },
            ],
        };

        using var file = Write(s => _converter.WriteExcel(s, model));
        using var package = SpreadsheetDocument.Open(file, isEditable: false);

        AssertValid(_validator.Validate(package));
    }

    [Fact]
    public void AnExportedDeckIsValidPresentationML()
    {
        var slide = new Slide { Layout = "titleContent" };

        slide.Elements.Add(new SlideElement
        {
            Type = "text", X = 8, Y = 12, W = 84, H = 18, Text = "<h1>Judul</h1>",
        });

        slide.Elements.Add(new SlideElement
        {
            Type = "text", X = 8, Y = 36, W = 84, H = 52, Text = "<p>Poin satu</p><p>Poin dua</p>",
        });

        var model = new PresentationModel { Slides = [slide] };

        using var file = Write(s => _converter.WritePowerPoint(s, model, "Rencana"));
        using var package = PresentationDocument.Open(file, isEditable: false);

        AssertValid(_validator.Validate(package));
    }
}
