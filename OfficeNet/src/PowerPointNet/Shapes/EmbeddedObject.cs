// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;

namespace PowerPointNet.Shapes;

/// <summary>
/// A file embedded in a slide as an OLE object — a workbook, a document, a PDF — shown as a
/// picture and opened by double-clicking it in PowerPoint.
/// </summary>
/// <remarks>
/// <para>
/// Two storage forms, chosen by what is embedded, as PowerPoint itself does. An Office Open XML file
/// (.xlsx, .docx, .pptx and their macro-enabled forms) is stored <b>as it is</b>, a package part
/// under <c>ppt/embeddings</c> with its application's ProgID (<c>Excel.Sheet.12</c>); double-clicking
/// it edits it in place. Anything else is wrapped in an <b>OLE Package</b> — a compound file holding
/// the file's name and bytes, ProgID <c>Package</c> — which Windows opens with whatever program
/// handles the extension, the way a PDF dragged onto a slide is stored.
/// </para>
/// <para>
/// The slide shows a picture, not the content: PowerPoint never renders an embedded object itself
/// until it is activated, so the preview is all a viewer, a printout or the PDF export can show.
/// Pass a real one — a rendered first page, from <c>OfficeNet.Rendering</c> — or a plain generated
/// tile is used.
/// </para>
/// </remarks>
public sealed class EmbeddedObject : Shape
{
    private const string OleUri = "http://schemas.openxmlformats.org/presentationml/2006/ole";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>The Packager shell object's class id, which an OLE Package's root storage carries.</summary>
    internal static readonly Guid PackagerClassId = Guid.Parse("0003000c-0000-0000-c000-000000000046");

    internal EmbeddedObject(Presentation presentation, XElement element, OpcPart slidePart) : base(presentation, element)
    {
        SlidePart = slidePart;
    }

    private OpcPart SlidePart { get; }

    private XElement? OleObject => FindOleObject(Element);

    /// <summary>
    /// The program that owns the object: <c>Excel.Sheet.12</c>, <c>Word.Document.12</c>,
    /// <c>Package</c> for a wrapped file, or whatever the producer wrote.
    /// </summary>
    public string? ProgId => OleObject?.Attr("progId");

    /// <summary>Whether PowerPoint shows the object as an icon rather than as its content.</summary>
    public bool ShowAsIcon => OleObject?.Attr("showAsIcon") is "1" or "true";

    /// <summary>
    /// The embedded file's name: the one an OLE Package carries, or the part's for an Office file.
    /// </summary>
    public string? FileName
    {
        get
        {
            if (DataPart() is not { } part)
            {
                return null;
            }

            if (part.Name.Extension == "bin" && Native(part.GetBytes()) is { } native)
            {
                return native.Name;
            }

            return System.IO.Path.GetFileName(part.Name.Value);
        }
    }

    /// <summary>
    /// The embedded file's bytes — the .xlsx itself, or the file inside an OLE Package — or
    /// <c>null</c> when the relationship is broken.
    /// </summary>
    /// <remarks>
    /// For an OLE object of another kind, the bytes of its <c>CONTENTS</c> or <c>Package</c> stream
    /// when it has one (how Acrobat and Office store a file in one), else the compound file itself.
    /// </remarks>
    public byte[]? GetData()
    {
        if (DataPart() is not { } part)
        {
            return null;
        }

        var bytes = part.GetBytes();

        if (part.Name.Extension != "bin" || !CompoundFile.IsCompoundFile(bytes))
        {
            return bytes;
        }

        if (Native(bytes) is { } native)
        {
            return native.Data;
        }

        try
        {
            var streams = CompoundFile.Read(bytes).Streams;

            return streams.TryGetValue("CONTENTS", out var contents) ? contents
                : streams.TryGetValue("Package", out var package) ? package
                : bytes;
        }
        catch (OfficeNetException)
        {
            return bytes;
        }
    }

    /// <summary>The preview picture's bytes, or <c>null</c> when it has none.</summary>
    public byte[]? GetPreviewImage()
    {
        var blip = OleObject?.Element(Ns.P + "pic")?.Element(Ns.P + "blipFill")?.Element(Ns.A + "blip")?.Attr(Ns.R + "embed");
        return blip is null ? null : Presentation.ResolveImage(Element, blip);
    }

    private OpcPart? DataPart() =>
        OleObject?.Attr(Ns.R + "id") is { } id ? SlidePart.RelatedPart(id) : null;

    /// <summary>Whether a graphic frame holds an OLE object.</summary>
    internal static bool IsEmbeddedObject(XElement graphicFrame) =>
        graphicFrame.Element(Ns.A + "graphic")?.Element(Ns.A + "graphicData")?.Attr("uri") == OleUri;

    /// <summary>
    /// The <c>p:oleObj</c>: directly in the graphic data, or — as PowerPoint writes it — in the
    /// fallback of an <c>mc:AlternateContent</c> whose choice is the VML form.
    /// </summary>
    private static XElement? FindOleObject(XElement graphicFrame)
    {
        var data = graphicFrame.Element(Ns.A + "graphic")?.Element(Ns.A + "graphicData");

        return data?.Element(Ns.P + "oleObj")
               ?? data?.Element(Mc + "AlternateContent")?.Element(Mc + "Fallback")?.Element(Ns.P + "oleObj")
               ?? data?.Element(Mc + "AlternateContent")?.Element(Mc + "Choice")?.Element(Ns.P + "oleObj");
    }

    // ---- Writing -------------------------------------------------------------------------------

    private sealed record OfficeType(string ProgId, string Name, string PartTemplate, string ContentType);

    private static OfficeType? Office(string extension) => extension switch
    {
        "xlsx" => new("Excel.Sheet.12", "Worksheet", "Microsoft_Excel_Worksheet{0}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
        "xlsm" => new("Excel.SheetMacroEnabled.12", "Worksheet", "Microsoft_Excel_Macro-Enabled_Worksheet{0}.xlsm",
            "application/vnd.ms-excel.sheet.macroEnabled.12"),
        "docx" => new("Word.Document.12", "Document", "Microsoft_Word_Document{0}.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        "docm" => new("Word.DocumentMacroEnabled.12", "Document", "Microsoft_Word_Macro-Enabled_Document{0}.docm",
            "application/vnd.ms-word.document.macroEnabled.12"),
        "pptx" => new("PowerPoint.Show.12", "Presentation", "Microsoft_PowerPoint_Presentation{0}.pptx",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
        "pptm" => new("PowerPoint.ShowMacroEnabled.12", "Presentation", "Microsoft_PowerPoint_Macro-Enabled_Presentation{0}.pptm",
            "application/vnd.ms-powerpoint.presentation.macroEnabled.12"),
        _ => null,
    };

    internal static EmbeddedObject Create(Presentation presentation, Slide slide, XElement shapeTree, uint id,
        byte[] data, string fileName, byte[]? preview, bool showAsIcon,
        Length left, Length top, Length width, Length height)
    {
        var extension = System.IO.Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        var office = Office(extension);

        string progId, name, relationshipId;

        if (office is not null)
        {
            // An Office file goes in as itself, so it must be one: a renamed file would embed and
            // then fail to open in the one place nobody can see the error — someone else's PowerPoint.
            if (data.Length < 4 || data[0] != 'P' || data[1] != 'K')
            {
                throw new ArgumentException(
                    $"'{fileName}' is named as an Office Open XML file but is not a zip package.", nameof(data));
            }

            var part = presentation.Package.AddPart(
                presentation.Package.NextPartName("/ppt/embeddings/" + office.PartTemplate), office.ContentType, data);
            relationshipId = slide.Part.AddRelationship(part, RelationshipTypes.Package_).Id;
            (progId, name) = (office.ProgId, office.Name);
        }
        else
        {
            var part = presentation.Package.AddPart(
                presentation.Package.NextPartName("/ppt/embeddings/oleObject{0}.bin"),
                ContentTypes.ForExtension("bin")!, PackageObject(System.IO.Path.GetFileName(fileName), data));
            relationshipId = slide.Part.AddRelationship(part, RelationshipTypes.OleObject).Id;
            (progId, name) = ("Package", "Packager Shell Object");
        }

        var (imageId, _) = presentation.AddImage(slide.Part, preview ?? PreviewTile(extension, width, height));

        XElement Position(XName xfrm) => new(xfrm,
            new XElement(Ns.A + "off", new XAttribute("x", left.Emu), new XAttribute("y", top.Emu)),
            new XElement(Ns.A + "ext", new XAttribute("cx", width.Emu), new XAttribute("cy", height.Emu)));

        // The form PowerPoint reads without VML: the object, its embed marker, and the picture it
        // shows. PowerPoint itself wraps this in mc:AlternateContent beside a VML choice.
        var element = new XElement(Ns.P + "graphicFrame",
            new XElement(Ns.P + "nvGraphicFramePr",
                new XElement(Ns.P + "cNvPr", new XAttribute("id", id), new XAttribute("name", $"Object {id}")),
                new XElement(Ns.P + "cNvGraphicFramePr",
                    new XElement(Ns.A + "graphicFrameLocks", new XAttribute("noChangeAspect", "1"))),
                new XElement(Ns.P + "nvPr")),
            Position(Ns.P + "xfrm"),
            new XElement(Ns.A + "graphic",
                new XElement(Ns.A + "graphicData", new XAttribute("uri", OleUri),
                    new XElement(Ns.P + "oleObj",
                        new XAttribute("name", name),
                        showAsIcon ? new XAttribute("showAsIcon", "1") : null,
                        new XAttribute(Ns.R + "id", relationshipId),
                        new XAttribute("imgW", width.Emu),
                        new XAttribute("imgH", height.Emu),
                        new XAttribute("progId", progId),
                        new XElement(Ns.P + "embed"),
                        new XElement(Ns.P + "pic",
                            new XElement(Ns.P + "nvPicPr",
                                new XElement(Ns.P + "cNvPr", new XAttribute("id", 0), new XAttribute("name", "")),
                                new XElement(Ns.P + "cNvPicPr"),
                                new XElement(Ns.P + "nvPr")),
                            new XElement(Ns.P + "blipFill",
                                new XElement(Ns.A + "blip", new XAttribute(Ns.R + "embed", imageId)),
                                new XElement(Ns.A + "stretch", new XElement(Ns.A + "fillRect"))),
                            new XElement(Ns.P + "spPr",
                                Position(Ns.A + "xfrm"),
                                new XElement(Ns.A + "prstGeom", new XAttribute("prst", "rect"), new XElement(Ns.A + "avLst"))))))));

        shapeTree.Add(element);
        presentation.Touch();
        return new EmbeddedObject(presentation, element, slide.Part);
    }

    // ---- The OLE Package ---------------------------------------------------------------------

    /// <summary>
    /// Wraps a file as the Packager shell object Windows uses for "insert object from file":
    /// <c>\u0001CompObj</c> naming the class, and <c>\u0001Ole10Native</c> holding the file.
    /// </summary>
    /// <remarks>
    /// The layout is the one PowerPoint writes, taken apart from a PDF it embedded: a size, a
    /// type of 2, the name, source path and temporary path as ANSI strings, the data, then the three
    /// names again in UTF-16. The paths are the bare file name here — a path from the machine that
    /// built the deck means nothing on the one that opens it.
    /// </remarks>
    internal static byte[] PackageObject(string fileName, byte[] data)
    {
        var native = new MemoryStream();
        var ansi = Encoding.Latin1.GetBytes(fileName.Select(c => c <= 0xFF ? c : '_').ToArray());

        void UInt32(uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            native.Write(buffer);
        }

        void UInt16(ushort value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            native.Write(buffer);
        }

        void Ansi()
        {
            native.Write(ansi);
            native.WriteByte(0);
        }

        UInt32(0);   // the total size, filled in below
        UInt16(2);
        Ansi();      // the label
        Ansi();      // the source path
        UInt16(0);
        UInt16(3);   // embedded, not linked
        UInt32((uint)ansi.Length + 1);
        Ansi();      // the temporary path
        UInt32((uint)data.Length);
        native.Write(data);

        foreach (var _ in Enumerable.Range(0, 3))
        {
            // Temporary path, label, source path, in UTF-16, character counts first.
            UInt32((uint)fileName.Length);
            native.Write(Encoding.Unicode.GetBytes(fileName));
        }

        var bytes = native.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)(bytes.Length - 4));

        var file = new CompoundFile { RootClassId = PackagerClassId };
        file.Streams["\u0001CompObj"] = CompObj();
        file.Streams["\u0001Ole10Native"] = bytes;
        return file.ToArray();
    }

    /// <summary>
    /// The <c>CompObjStream</c> (MS-OLEDS 2.3.8) of a Packager object: its class, user type
    /// "OLE Package", and ProgID "Package".
    /// </summary>
    private static byte[] CompObj()
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);

        writer.Write(0xFFFE0001u);   // reserved
        writer.Write(0x00000A03u);   // version
        writer.Write(0xFFFFFFFFu);   // reserved
        writer.Write(PackagerClassId.ToByteArray());

        void Ansi(string value)
        {
            writer.Write((uint)value.Length + 1);
            writer.Write(Encoding.ASCII.GetBytes(value));
            writer.Write((byte)0);
        }

        Ansi("OLE Package");
        writer.Write(0u);            // no clipboard format
        Ansi("Package");
        writer.Write(0x71B239F4u);   // the Unicode marker, then three empty Unicode strings
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Reads the file out of a Packager object's <c>\u0001Ole10Native</c> stream.</summary>
    private static (string Name, byte[] Data)? Native(byte[] compoundFile)
    {
        try
        {
            if (!CompoundFile.Read(compoundFile).Streams.TryGetValue("\u0001Ole10Native", out var native) || native.Length < 6)
            {
                return null;
            }

            var reader = new BinaryReader(new MemoryStream(native));
            reader.ReadUInt32();

            if (reader.ReadUInt16() != 2)
            {
                return null;
            }

            var name = AnsiString(reader);
            AnsiString(reader);
            reader.ReadUInt16();

            if (reader.ReadUInt16() != 3)
            {
                return null;   // a link, not an embedded file
            }

            reader.ReadBytes((int)reader.ReadUInt32());
            var data = reader.ReadBytes((int)reader.ReadUInt32());

            // The UTF-16 label follows the temporary path, when the producer wrote them.
            if (reader.BaseStream.Length - reader.BaseStream.Position >= 8)
            {
                reader.ReadBytes((int)reader.ReadUInt32() * 2);
                var length = (int)reader.ReadUInt32();

                if (length > 0 && length * 2 <= reader.BaseStream.Length - reader.BaseStream.Position)
                {
                    name = Encoding.Unicode.GetString(reader.ReadBytes(length * 2));
                }
            }

            return (name, data);
        }
        catch (Exception ex) when (ex is OfficeNetException or EndOfStreamException or ArgumentException)
        {
            return null;
        }

        static string AnsiString(BinaryReader reader)
        {
            var bytes = new List<byte>();

            for (var b = reader.ReadByte(); b != 0; b = reader.ReadByte())
            {
                bytes.Add(b);
            }

            return Encoding.Latin1.GetString([.. bytes]);
        }
    }

    /// <summary>
    /// The preview used when none is given: a light tile with a page and a band in the colour of
    /// the application that owns the file. Generated, so the library carries no image assets.
    /// </summary>
    private static byte[] PreviewTile(string extension, Length width, Length height)
    {
        var w = Math.Clamp((int)Math.Round(width.Pixels), 32, 800);
        var h = Math.Clamp((int)Math.Round(height.Pixels), 32, 800);

        (byte R, byte G, byte B) band = extension switch
        {
            "xlsx" or "xlsm" or "csv" => (0x21, 0x73, 0x46),
            "docx" or "docm" or "txt" => (0x2B, 0x57, 0x9A),
            "pptx" or "pptm" => (0xC4, 0x3E, 0x1C),
            "pdf" => (0xB3, 0x0B, 0x00),
            _ => (0x5F, 0x63, 0x68),
        };

        var rgb = new byte[w * h * 3];
        var pageWidth = Math.Min(w, h) * 0.5;
        var (pageLeft, pageTop) = ((w - pageWidth) / 2, h * 0.15);
        var pageBottom = h * 0.85;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var onPage = x >= pageLeft && x < pageLeft + pageWidth && y >= pageTop && y < pageBottom;
                var edge = onPage && (x < pageLeft + 1 || x >= pageLeft + pageWidth - 1 || y < pageTop + 1 || y >= pageBottom - 1);
                var onBand = onPage && y >= pageTop + (pageBottom - pageTop) * 0.62 && y < pageTop + (pageBottom - pageTop) * 0.8;

                var (r, g, b) = edge ? ((byte)0x9A, (byte)0xA0, (byte)0xA6)
                    : onBand ? band
                    : onPage ? ((byte)0xFF, (byte)0xFF, (byte)0xFF)
                    : ((byte)0xF1, (byte)0xF3, (byte)0xF4);

                var offset = (y * w + x) * 3;
                (rgb[offset], rgb[offset + 1], rgb[offset + 2]) = (r, g, b);
            }
        }

        return PdfNet.Text.ImageExtractor.EncodePng(rgb, w, h);
    }

    /// <inheritdoc />
    public override string ToString() => $"Embedded {ProgId} \"{FileName}\"";
}
