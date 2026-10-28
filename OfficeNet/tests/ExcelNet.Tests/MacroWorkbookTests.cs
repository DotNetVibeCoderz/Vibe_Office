// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ExcelNet;
using Xunit;

namespace ExcelNet.Tests;

/// <summary>
/// A macro-enabled workbook (.xlsm) opened, edited and saved keeps its macros and stays an .xlsm.
/// </summary>
/// <remarks>
/// The library does not model VBA, and should not lose it either. Two things have to survive: the
/// vbaProject.bin part with its relationship, and the main part's content type — an .xlsm whose
/// workbook says <c>sheet.main+xml</c> is refused by Excel as "the file format or extension is not
/// valid".
/// </remarks>
public class MacroWorkbookTests
{
    private const string MacroEnabled = "application/vnd.ms-excel.sheet.macroEnabled.main+xml";
    private const string VbaRelationship = "http://schemas.microsoft.com/office/2006/relationships/vbaProject";

    private static byte[] MakeXlsm()
    {
        using var workbook = Workbook.Create("Data");
        workbook["Data"]["A1"].Set("asli");

        using var stream = new MemoryStream();
        workbook.Save(stream);

        // Turn the .xlsx into what Excel writes for an .xlsm: another content type for the main
        // part, and a binary VBA project related to it.
        using var output = new MemoryStream();
        using (var source = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read))
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                var text = reader.ReadToEnd();

                if (entry.FullName == "[Content_Types].xml")
                {
                    text = text.Replace("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", MacroEnabled)
                        .Replace("</Types>", """<Default Extension="bin" ContentType="application/vnd.ms-office.vbaProject"/></Types>""");
                }
                else if (entry.FullName == "xl/_rels/workbook.xml.rels")
                {
                    text = text.Replace("</Relationships>",
                        $"""<Relationship Id="rIdVba" Type="{VbaRelationship}" Target="vbaProject.bin"/></Relationships>""");
                }

                using var writer = new StreamWriter(target.CreateEntry(entry.FullName).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }

            using var vba = target.CreateEntry("xl/vbaProject.bin").Open();
            vba.Write([0xCC, 0x61, 0x88, 0x00, 0x01, 0x02, 0x03]);
        }

        return output.ToArray();
    }

    [Fact]
    public void AnEditedXlsmKeepsItsMacrosAndItsContentType()
    {
        using var workbook = Workbook.Open(MakeXlsm());
        workbook["Data"]["A1"].Set("diubah");
        workbook.AddSheet("Baru")["A1"].Set(1);

        using var stream = new MemoryStream();
        workbook.Save(stream);
        var saved = stream.ToArray();
        XlsxValidator.AssertValid(saved);

        using var archive = new ZipArchive(new MemoryStream(saved));
        var types = XDocument.Load(archive.GetEntry("[Content_Types].xml")!.Open()).Root!;
        var main = types.Elements().Single(e => e.Attribute("PartName")?.Value == "/xl/workbook.xml");
        Assert.Equal(MacroEnabled, main.Attribute("ContentType")!.Value);

        var vba = archive.GetEntry("xl/vbaProject.bin")!;
        using var buffer = new MemoryStream();
        vba.Open().CopyTo(buffer);
        Assert.Equal([0xCC, 0x61, 0x88, 0x00, 0x01, 0x02, 0x03], buffer.ToArray());

        var relationships = XDocument.Load(archive.GetEntry("xl/_rels/workbook.xml.rels")!.Open()).Root!;
        Assert.Contains(relationships.Elements(), r => r.Attribute("Type")!.Value == VbaRelationship);

        using var reopened = Workbook.Open(saved);
        Assert.Equal("diubah", reopened["Data"]["A1"].Text);
    }
}
