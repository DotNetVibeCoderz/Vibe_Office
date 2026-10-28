// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;

namespace ExcelNet;

/// <summary>
/// Reads and writes a worksheet's cell comments.
/// </summary>
/// <remarks>
/// <para>
/// A comment is two parts, not one. <c>xl/commentsN.xml</c> holds the text, and a legacy VML
/// drawing holds the yellow box that shows it. The file is valid without the VML, but Excel then
/// displays nothing — no red triangle, no hover — so writing only the comments part looks like
/// success and is not. The worksheet refers to the VML through <c>legacyDrawing</c>; the comments
/// part has no element at all and is found only through its relationship.
/// </para>
/// <para>
/// VML shape ids must be unique across the whole workbook, not per sheet. Each sheet claims blocks
/// of 1024 through <c>o:idmap</c>, and a shape's id is <c>block × 1024 + n</c>. The caller hands
/// out blocks so two sheets can never collide.
/// </para>
/// </remarks>
internal static class SheetComments
{
    private static readonly XNamespace S = Ns.S;

    private const string VmlNs = "urn:schemas-microsoft-com:vml";
    private const string OfficeNs = "urn:schemas-microsoft-com:office:office";
    private const string ExcelNs = "urn:schemas-microsoft-com:office:excel";

    /// <summary>Shapes per idmap block. 1024 ids per block, and id <c>block × 1024</c> is not used.</summary>
    private const int ShapesPerBlock = 1023;

    private const string DefaultAuthor = "OfficeNet";

    // ---- Reading -------------------------------------------------------------------------------

    /// <summary>Loads the sheet's comments into its cells.</summary>
    internal static void Read(Worksheet sheet)
    {
        var part = sheet.Part.RelatedPartByType(RelationshipTypes.Comments);

        if (part?.Xml.Root is not { } root)
        {
            return;
        }

        foreach (var comment in root.Element(S + "commentList")?.Elements(S + "comment") ?? [])
        {
            if (!CellReference.TryParse(comment.Attr("ref"), out var reference))
            {
                continue;
            }

            var text = comment.Element(S + "text");

            if (text is null)
            {
                continue;
            }

            // Plain text is a bare <t>; rich text is a run per format. Either way the comment is
            // the concatenation of every <t>, in order — Excel's own "Author:" prefix included,
            // because it is part of the text the user sees.
            var builder = new StringBuilder();

            foreach (var t in text.Descendants(S + "t"))
            {
                // A phonetic reading (rPh) carries its own <t>, which is not part of the text.
                if (t.Parent?.Name == S + "rPh")
                {
                    continue;
                }

                builder.Append(t.Value);
            }

            sheet.LoadComment(reference, builder.ToString());
        }
    }

    // ---- Writing -------------------------------------------------------------------------------

    /// <summary>
    /// Writes the comments part and its VML drawing, or removes both when the sheet has none.
    /// </summary>
    /// <param name="sheet">The sheet being saved.</param>
    /// <param name="nextBlock">The next free VML idmap block; advanced past the blocks used.</param>
    internal static void Write(Worksheet sheet, ref int nextBlock)
    {
        var comments = sheet.RawCells
            .Where(pair => !string.IsNullOrEmpty(pair.Value.Comment))
            .OrderBy(pair => pair.Key)
            .Select(pair => (pair.Key, Text: pair.Value.Comment!))
            .ToList();

        var package = sheet.Workbook.Package;
        var commentsPart = sheet.Part.RelatedPartByType(RelationshipTypes.Comments);
        var vmlPart = sheet.Part.RelatedPartByType(RelationshipTypes.VmlDrawing);

        if (comments.Count == 0)
        {
            if (commentsPart is not null)
            {
                package.RemovePart(commentsPart.Name);
            }

            // A VML drawing that holds nothing but notes is ours to remove with them; one holding
            // form controls or anything else belongs to the sheet and stays.
            if (vmlPart is not null && HoldsOnlyNotes(vmlPart))
            {
                package.RemovePart(vmlPart.Name);
            }

            return;
        }

        if (commentsPart is null)
        {
            commentsPart = package.AddPart(package.NextPartName("/xl/comments{0}.xml"),
                ContentTypes.ExcelComments);
            sheet.Part.AddRelationship(commentsPart, RelationshipTypes.Comments);
        }

        if (vmlPart is null)
        {
            vmlPart = package.AddPart(package.NextPartName("/xl/drawings/vmlDrawing{0}.vml"),
                ContentTypes.VmlDrawing);
            sheet.Part.AddRelationship(vmlPart, RelationshipTypes.VmlDrawing);
        }

        var author = sheet.Workbook.Properties.Creator;

        if (string.IsNullOrWhiteSpace(author))
        {
            author = DefaultAuthor;
        }

        commentsPart.SetBytes(WriteComments(comments, author));

        var blocks = (comments.Count + ShapesPerBlock - 1) / ShapesPerBlock;
        vmlPart.SetBytes(WriteVml(comments, nextBlock, blocks));
        nextBlock += blocks;

        package.MarkDirty();
    }

    private static byte[] WriteComments(List<(CellReference Reference, string Text)> comments,
        string author)
    {
        using var buffer = new MemoryStream();

        using (var writer = XmlWriter.Create(buffer, Settings()))
        {
            writer.WriteStartDocument(standalone: true);
            writer.WriteStartElement("comments", S.NamespaceName);

            writer.WriteStartElement("authors", S.NamespaceName);
            writer.WriteElementString("author", S.NamespaceName, author);
            writer.WriteEndElement();

            writer.WriteStartElement("commentList", S.NamespaceName);

            foreach (var (reference, text) in comments)
            {
                writer.WriteStartElement("comment", S.NamespaceName);
                writer.WriteAttributeString("ref", reference.A1);
                writer.WriteAttributeString("authorId", "0");

                writer.WriteStartElement("text", S.NamespaceName);
                writer.WriteStartElement("t", S.NamespaceName);
                // Leading/trailing spaces and line breaks are content; without preserve they go.
                writer.WriteAttributeString("xml", "space", null, "preserve");
                writer.WriteString(SharedStrings.Sanitize(text));
                writer.WriteEndElement();
                writer.WriteEndElement();

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return buffer.ToArray();
    }

    /// <summary>Writes one hidden note shape per comment.</summary>
    /// <remarks>
    /// <c>x:Row</c> and <c>x:Column</c> are what tie a shape to its cell, and they are zero-based
    /// while <c>ref</c> in the comments part is A1. <c>x:Anchor</c> is where the box appears when
    /// shown: column, offset, row, offset for its top-left and bottom-right corners.
    /// </remarks>
    private static byte[] WriteVml(List<(CellReference Reference, string Text)> comments,
        int firstBlock, int blocks)
    {
        var builder = new StringBuilder(512 + comments.Count * 640);

        builder.Append("<xml xmlns:v=\"").Append(VmlNs)
            .Append("\" xmlns:o=\"").Append(OfficeNs)
            .Append("\" xmlns:x=\"").Append(ExcelNs).Append("\">\n");

        builder.Append(" <o:shapelayout v:ext=\"edit\"><o:idmap v:ext=\"edit\" data=\"");

        for (var i = 0; i < blocks; i++)
        {
            builder.Append(i == 0 ? "" : ",").Append(firstBlock + i);
        }

        builder.Append("\"/></o:shapelayout>\n");

        builder.Append(" <v:shapetype id=\"_x0000_t202\" coordsize=\"21600,21600\" o:spt=\"202\"" +
                       " path=\"m,l,21600r21600,l21600,xe\">" +
                       "<v:stroke joinstyle=\"miter\"/>" +
                       "<v:path gradientshapeok=\"t\" o:connecttype=\"rect\"/></v:shapetype>\n");

        for (var i = 0; i < comments.Count; i++)
        {
            var reference = comments[i].Reference;
            var block = firstBlock + i / ShapesPerBlock;
            var shapeId = block * 1024 + 1 + i % ShapesPerBlock;

            var row = reference.Row;
            var column = reference.Column;
            var anchor = string.Create(CultureInfo.InvariantCulture,
                $"{column + 1}, 15, {Math.Max(0, row - 1)}, 10, {column + 3}, 15, {row + 3}, 4");

            builder.Append(" <v:shape id=\"_x0000_s").Append(shapeId)
                .Append("\" type=\"#_x0000_t202\" style=\"position:absolute;margin-left:59.25pt;")
                .Append("margin-top:1.5pt;width:108pt;height:59.25pt;z-index:").Append(i + 1)
                .Append(";visibility:hidden\" fillcolor=\"#ffffe1\" o:insetmode=\"auto\">")
                .Append("<v:fill color2=\"#ffffe1\"/>")
                .Append("<v:shadow on=\"t\" color=\"black\" obscured=\"t\"/>")
                .Append("<v:path o:connecttype=\"none\"/>")
                .Append("<v:textbox style=\"mso-direction-alt:auto\"><div style=\"text-align:left\"></div></v:textbox>")
                .Append("<x:ClientData ObjectType=\"Note\"><x:MoveWithCells/><x:SizeWithCells/>")
                .Append("<x:Anchor>").Append(anchor).Append("</x:Anchor>")
                .Append("<x:AutoFill>False</x:AutoFill>")
                .Append("<x:Row>").Append(row.ToString(CultureInfo.InvariantCulture)).Append("</x:Row>")
                .Append("<x:Column>").Append(column.ToString(CultureInfo.InvariantCulture)).Append("</x:Column>")
                .Append("</x:ClientData></v:shape>\n");
        }

        builder.Append("</xml>\n");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>Whether every shape in a VML drawing is a comment note.</summary>
    /// <remarks>
    /// VML written by Excel is not always well-formed XML. One that cannot be parsed is treated as
    /// holding something else, so it is kept rather than deleted on a guess.
    /// </remarks>
    private static bool HoldsOnlyNotes(OpcPart part)
    {
        try
        {
            var document = XDocument.Parse(part.GetText());
            XNamespace x = ExcelNs;

            return document.Descendants(x + "ClientData")
                .All(e => (string?)e.Attribute("ObjectType") == "Note");
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static XmlWriterSettings Settings() => new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        Indent = false,
        CloseOutput = false,
    };
}
