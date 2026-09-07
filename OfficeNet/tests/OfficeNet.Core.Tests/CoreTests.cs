// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Metadata;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using OfficeNet.TestKit;
using Xunit;

namespace OfficeNet.Core.Tests;

public class LengthTests
{
    [Fact]
    public void ConversionsAreExactForTheUnitsOoxmlWrites()
    {
        // EMU is divisible by every OOXML unit, so these are exact rather than approximate. A
        // conversion that goes through double inches loses the round trip.
        Assert.Equal(914_400, Units.Inches(1).Emu);
        Assert.Equal(360_000, Units.Cm(1).Emu);
        Assert.Equal(12_700, Units.Pt(1).Emu);
        Assert.Equal(635, Units.Twips(1).Emu);
        Assert.Equal(9_525, Units.Px(1).Emu);
    }

    [Fact]
    public void CentimetreRoundTripIsLossless()
    {
        // 2.54 cm through twips and back is 2.5400000000000005 when the intermediate is inches.
        var margin = Units.Cm(2.54);
        Assert.Equal(2.54, Length.FromTwips(margin.Twips).Centimeters, 10);
    }

    [Fact]
    public void FontSizeIsHalfPointsAndBorderSizeIsEighths()
    {
        // The same w:sz attribute means half-points on a run and eighths of a point on a border.
        Assert.Equal(24, Units.Pt(12).HalfPoints);
        Assert.Equal(1200, Units.Pt(12).Centipoints);
    }

    [Fact]
    public void ArithmeticAndComparisonBehave()
    {
        Assert.Equal(Units.Cm(3), Units.Cm(1) + Units.Cm(2));
        Assert.Equal(Units.Cm(1), Units.Cm(3) - Units.Cm(2));
        Assert.Equal(Units.Cm(4), Units.Cm(2) * 2);
        Assert.True(Units.Cm(1) < Units.Cm(2));
        Assert.Equal(2, Units.Cm(4) / Units.Cm(2), 10);
    }
}

public class OpcPartNameTests
{
    [Theory]
    [InlineData("/word/document.xml", "/word", "document.xml", "xml")]
    [InlineData("word/document.xml", "/word", "document.xml", "xml")]
    [InlineData("/image1.PNG", "", "image1.PNG", "png")]
    public void ParsesAndNormalises(string input, string directory, string fileName, string extension)
    {
        var name = new OpcPartName(input);
        Assert.Equal(directory, name.Directory);
        Assert.Equal(fileName, name.FileName);
        Assert.Equal(extension, name.Extension);
    }

    [Fact]
    public void ComparesCaseInsensitivelyButPreservesCase()
    {
        // OPC compares part names case-insensitively. Treating them as case-sensitive lets two
        // spellings of one part into the same package, which Word rejects.
        var a = new OpcPartName("/word/Document.xml");
        var b = new OpcPartName("/word/document.xml");

        Assert.Equal(a, b);
        Assert.Equal("/word/Document.xml", a.Value);
    }

    [Fact]
    public void RelationshipPartNameFollowsTheRelsConvention()
    {
        Assert.Equal("/word/_rels/document.xml.rels",
            new OpcPartName("/word/document.xml").RelationshipPartName.Value);

        Assert.Equal("/_rels/foo.xml.rels",
            new OpcPartName("/foo.xml").RelationshipPartName.Value);
    }

    [Theory]
    [InlineData("/word/document.xml", "media/image1.png", "/word/media/image1.png")]
    [InlineData("/word/document.xml", "../customXml/item1.xml", "/customXml/item1.xml")]
    [InlineData("/word/document.xml", "/docProps/core.xml", "/docProps/core.xml")]
    [InlineData("/word/document.xml", "media/image%201.png", "/word/media/image 1.png")]
    public void ResolvesRelativeTargets(string source, string target, string expected) =>
        Assert.Equal(expected, new OpcPartName(source).Resolve(target).Value);

    [Fact]
    public void ExpressesTargetsRelativeToTheSource()
    {
        var source = new OpcPartName("/word/document.xml");

        Assert.Equal("media/image1.png", source.RelativeTo("/word/media/image1.png"));
        Assert.Equal("../docProps/core.xml", source.RelativeTo("/docProps/core.xml"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/word/")]
    [InlineData("/word//document.xml")]
    public void RejectsMalformedNames(string input) =>
        Assert.Throws<ArgumentException>(() => new OpcPartName(input));
}

public class OpcPackageTests
{
    [Fact]
    public void RoundTripsPartsRelationshipsAndContentTypes()
    {
        using var file = new TempFile(".zip");

        using (var package = OpcPackage.Create())
        {
            var main = package.AddXmlPart("/word/document.xml", ContentTypes.WordDocument,
                XmlUtil.NewDocument(new XElement(Ns.W + "document")));

            package.AddRootRelationship(main, RelationshipTypes.OfficeDocument);

            var image = package.AddPart("/word/media/image1.png", "image/png", [1, 2, 3, 4]);
            main.AddRelationship(image, RelationshipTypes.Image);
            main.AddExternalRelationship(RelationshipTypes.Hyperlink, "https://gravicode.com");

            package.Save(file.Path);
        }

        using var reopened = OpcPackage.Open(file.Path);

        Assert.NotNull(reopened.MainDocumentPart);
        Assert.Equal(ContentTypes.WordDocument, reopened.MainDocumentPart!.ContentType);

        var media = reopened.MainDocumentPart.RelatedPartByType(RelationshipTypes.Image);
        Assert.NotNull(media);
        Assert.Equal<byte[]>([1, 2, 3, 4], media!.GetBytes());

        var link = reopened.MainDocumentPart.RelationshipByType(RelationshipTypes.Hyperlink);
        Assert.NotNull(link);
        Assert.Equal(TargetMode.External, link!.TargetMode);
        Assert.Equal("https://gravicode.com", link.Target);
    }

    [Fact]
    public void SavedPackagePassesIndependentValidation()
    {
        using var package = OpcPackage.Create();

        var main = package.AddXmlPart("/word/document.xml", ContentTypes.WordDocument,
            XmlUtil.NewDocument(new XElement(Ns.W + "document")));

        package.AddRootRelationship(main, RelationshipTypes.OfficeDocument);
        main.AddRelationship(package.AddPart("/word/media/image1.png", "image/png", [1]),
            RelationshipTypes.Image);

        var report = OpcValidator.Validate(package.ToArray());
        Assert.True(report.IsValid, report.Describe());
    }

    [Fact]
    public void RemovingAPartAlsoRemovesRelationshipsPointingAtIt()
    {
        // A relationship whose target is gone is exactly what Office reports as unreadable content.
        using var package = OpcPackage.Create();

        var main = package.AddXmlPart("/word/document.xml", ContentTypes.WordDocument,
            XmlUtil.NewDocument(new XElement(Ns.W + "document")));

        package.AddRootRelationship(main, RelationshipTypes.OfficeDocument);

        var image = package.AddPart("/word/media/image1.png", "image/png", [1]);
        main.AddRelationship(image, RelationshipTypes.Image);

        Assert.Single(main.RelationshipsByType(RelationshipTypes.Image));

        package.RemovePart(image.Name);

        Assert.Empty(main.RelationshipsByType(RelationshipTypes.Image));

        var report = OpcValidator.Validate(package.ToArray());
        Assert.True(report.IsValid, report.Describe());
    }

    [Fact]
    public void UnknownPartsSurviveARoundTripUntouched()
    {
        // The whole point of keeping raw bytes: a part this library does not model must come back
        // byte for byte, or opening and saving a real document silently drops features.
        var payload = "kunci-rahasia-vendor"u8.ToArray();

        using var file = new TempFile(".zip");

        using (var package = OpcPackage.Create())
        {
            var main = package.AddXmlPart("/word/document.xml", ContentTypes.WordDocument,
                XmlUtil.NewDocument(new XElement(Ns.W + "document")));

            package.AddRootRelationship(main, RelationshipTypes.OfficeDocument);
            package.AddPart("/customXml/item1.bin", "application/octet-stream", payload);
            package.Save(file.Path);
        }

        using var first = OpcPackage.Open(file.Path);

        using var second = new TempFile(".zip");
        first.Save(second.Path);

        using var reopened = OpcPackage.Open(second.Path);
        Assert.Equal(payload, reopened.GetPart("/customXml/item1.bin").GetBytes());
    }

    [Fact]
    public void RejectsSomethingThatIsNotAZip()
    {
        var ex = Assert.Throws<OfficeNetException>(() =>
            OpcPackage.Open(new MemoryStream("this is not a zip file"u8.ToArray())));

        Assert.Contains("zip", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MediaKeepsItsExtensionAsADefaultContentType()
    {
        using var package = OpcPackage.Create();

        var main = package.AddXmlPart("/word/document.xml", ContentTypes.WordDocument,
            XmlUtil.NewDocument(new XElement(Ns.W + "document")));

        package.AddRootRelationship(main, RelationshipTypes.OfficeDocument);
        package.AddPart("/word/media/image1.png", "image/png", [1]);
        package.AddPart("/word/media/image2.png", "image/png", [2]);

        var manifest = System.Text.Encoding.UTF8.GetString(
            ReadEntry(package.ToArray(), "[Content_Types].xml"));

        // Two PNGs share one Default entry rather than getting an Override each.
        Assert.Contains("Extension=\"png\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("/word/media/image1.png", manifest, StringComparison.Ordinal);
    }

    private static byte[] ReadEntry(byte[] package, string name)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var archive = new System.IO.Compression.ZipArchive(stream);
        using var entry = archive.GetEntry(name)!.Open();
        using var buffer = new MemoryStream();
        entry.CopyTo(buffer);
        return buffer.ToArray();
    }
}

public class OfficeColorTests
{
    [Theory]
    [InlineData("FF0000", 255, 0, 0)]
    [InlineData("#00FF00", 0, 255, 0)]
    [InlineData("00f", 0, 0, 255)]
    [InlineData("FF112233", 0x11, 0x22, 0x33)]
    public void ParsesTheFormsFilesContain(string input, byte r, byte g, byte b)
    {
        Assert.True(OfficeColor.TryParse(input, out var color));
        Assert.Equal(r, color.R);
        Assert.Equal(g, color.G);
        Assert.Equal(b, color.B);
    }

    [Fact]
    public void AutomaticIsNotBlack()
    {
        // OOXML "auto" means unspecified, not black. Collapsing them loses the distinction that
        // lets a consumer pick a colour based on the background.
        Assert.True(OfficeColor.Parse("auto").IsAutomatic);
        Assert.False(OfficeColor.Black.IsAutomatic);
    }

    [Fact]
    public void ContrastingForegroundPicksTheReadableOne()
    {
        Assert.Equal(OfficeColor.White, OfficeColor.FromRgb(0x1F, 0x38, 0x64).ContrastingForeground);
        Assert.Equal(OfficeColor.Black, OfficeColor.FromRgb(0xFF, 0xF2, 0xCC).ContrastingForeground);
    }

    [Fact]
    public void HexRoundTripsWithoutTheHash() =>
        Assert.Equal("1F3864", OfficeColor.FromRgb(0x1F, 0x38, 0x64).ToHex());
}

public class ImageInfoTests
{
    [Fact]
    public void ReadsPngSizeWithoutDecodingPixels()
    {
        var png = TestImages.SolidPng(64, 32, 255, 0, 0);
        var info = ImageInfo.Read(png);

        Assert.Equal(ImageFormat.Png, info.Format);
        Assert.Equal(64, info.PixelWidth);
        Assert.Equal(32, info.PixelHeight);
        Assert.Equal(2, info.AspectRatio, 6);
    }

    [Fact]
    public void NaturalSizeUsesTheImagesOwnResolution()
    {
        // A 96 pixel wide image at 96 DPI is one inch. The bug this guards against is assuming
        // 96 DPI for an image that says otherwise, which overflows the page margins.
        var info = ImageInfo.Read(TestImages.SolidPng(96, 96, 0, 0, 0));
        Assert.Equal(1, info.NaturalWidth.Inches, 6);
    }

    [Fact]
    public void RejectsSomethingThatIsNotAnImage() =>
        Assert.Throws<OfficeNetException>(() => ImageInfo.Read("hello"u8));

    [Fact]
    public void ReadsSvgFromItsViewBoxWhenItHasNoWidth()
    {
        // A responsive SVG has only a viewBox; falling back to a fixed default squashes it.
        var svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100"></svg>"""u8;
        var info = ImageInfo.Read(svg);

        Assert.Equal(ImageFormat.Svg, info.Format);
        Assert.Equal(200, info.PixelWidth);
        Assert.Equal(100, info.PixelHeight);
    }
}

public class XmlUtilTests
{
    [Fact]
    public void SetOrderedKeepsTheSchemaSequence()
    {
        // Word rejects a w:rPr whose children are out of order, so inserting must respect it
        // regardless of the order the caller sets properties in.
        XName[] order = [Ns.W + "b", Ns.W + "i", Ns.W + "sz"];
        var parent = new XElement(Ns.W + "rPr");

        XmlUtil.SetOrdered(parent, Ns.W + "sz", new XElement(Ns.W + "sz"), order);
        XmlUtil.SetOrdered(parent, Ns.W + "b", new XElement(Ns.W + "b"), order);
        XmlUtil.SetOrdered(parent, Ns.W + "i", new XElement(Ns.W + "i"), order);

        Assert.Equal(["b", "i", "sz"], parent.Elements().Select(e => e.Name.LocalName));
        Assert.Null(OpcValidator.CheckChildOrder(parent, "b", "i", "sz"));
    }

    [Fact]
    public void ToggleValueDistinguishesInheritFromExplicitlyOff()
    {
        // null = inherit, false = explicitly off. Collapsing them makes it impossible to write an
        // unbolded word inside a bold style.
        var parent = new XElement(Ns.W + "rPr");
        Assert.Null(parent.ToggleValue(Ns.W + "b"));

        parent.Add(new XElement(Ns.W + "b"));
        Assert.True(parent.ToggleValue(Ns.W + "b"));

        parent.Element(Ns.W + "b")!.SetAttributeValue(Ns.W + "val", "0");
        Assert.False(parent.ToggleValue(Ns.W + "b"));
    }

    [Fact]
    public void TextElementPreservesSignificantWhitespace()
    {
        // Without xml:space, "Hello " + "world" becomes "Helloworld" after a save.
        var element = XmlUtil.TextElement(Ns.W + "t", "Hello ");
        Assert.Equal("preserve", element.Attribute(XNamespace.Xml + "space")?.Value);

        Assert.Null(XmlUtil.TextElement(Ns.W + "t", "Hello").Attribute(XNamespace.Xml + "space"));
    }

    [Fact]
    public void NormalizesStrictNamespacesToTransitional()
    {
        // A strict document is structurally identical but every w:p lookup misses without this.
        var document = new XDocument(new XElement(Ns.WStrict + "document",
            new XElement(Ns.WStrict + "body",
                new XElement(Ns.WStrict + "p"))));

        Assert.True(XmlUtil.NormalizeStrictNamespaces(document));
        Assert.Single(document.Descendants(Ns.W + "p"));
    }
}

public class MetadataTests
{
    [Fact]
    public void CorePropertiesRoundTrip()
    {
        using var file = new TempFile(".zip");
        var created = new DateTime(2026, 1, 15, 8, 30, 0, DateTimeKind.Utc);

        using (var package = OpcPackage.Create())
        {
            var main = package.AddXmlPart("/word/document.xml", ContentTypes.WordDocument,
                XmlUtil.NewDocument(new XElement(Ns.W + "document")));

            package.AddRootRelationship(main, RelationshipTypes.OfficeDocument);

            var properties = CoreProperties.Open(package);
            properties.Title = "Laporan";
            properties.Creator = "Kang Fadhil";
            properties.Keywords = "officenet;gravicode";
            properties.Created = created;

            package.Save(file.Path);
        }

        using var reopened = OpcPackage.Open(file.Path);
        var read = CoreProperties.Open(reopened);

        Assert.Equal("Laporan", read.Title);
        Assert.Equal("Kang Fadhil", read.Creator);
        Assert.Equal("officenet;gravicode", read.Keywords);
        Assert.Equal(created, read.Created);
    }

    [Fact]
    public void CustomPropertiesKeepTheirTypes()
    {
        using var package = OpcPackage.Create();

        var main = package.AddXmlPart("/word/document.xml", ContentTypes.WordDocument,
            XmlUtil.NewDocument(new XElement(Ns.W + "document")));

        package.AddRootRelationship(main, RelationshipTypes.OfficeDocument);

        var custom = CustomProperties.Open(package);
        custom["Departemen"] = "Riset";
        custom["Versi"] = 2;
        custom["Rasio"] = 1.5;
        custom["Aktif"] = true;

        using var stream = new MemoryStream(package.ToArray());
        using var reopened = OpcPackage.Open(stream);
        var read = CustomProperties.Open(reopened);

        // A number stored as a string cannot be used in a Word field calculation, so the type
        // matters as much as the value.
        Assert.Equal("Riset", read["Departemen"]);
        Assert.Equal(2, read["Versi"]);
        Assert.Equal(1.5, read["Rasio"]);
        Assert.Equal(true, read["Aktif"]);
    }

    [Fact]
    public void CustomPropertyIdsStartAtTwo()
    {
        // pid 0 and 1 are reserved by the underlying OLE property set; Word silently drops a
        // property that uses them.
        using var package = OpcPackage.Create();

        var main = package.AddXmlPart("/word/document.xml", ContentTypes.WordDocument,
            XmlUtil.NewDocument(new XElement(Ns.W + "document")));

        package.AddRootRelationship(main, RelationshipTypes.OfficeDocument);

        var custom = CustomProperties.Open(package);
        custom["A"] = 1;
        custom["B"] = 2;

        var part = package.GetPart("/docProps/custom.xml");

        var pids = part.Xml.Root!.Elements(Ns.Cp + "property")
            .Select(e => int.Parse(e.Attribute("pid")!.Value))
            .ToList();

        Assert.Equal([2, 3], pids);
    }
}
