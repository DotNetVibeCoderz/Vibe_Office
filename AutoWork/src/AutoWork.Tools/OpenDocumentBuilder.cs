using System.IO.Compression;
using System.Text;
using System.Xml;

namespace AutoWork.Tools;

/// <summary>
/// Writes OpenDocument text and spreadsheet files.
///
/// Built by hand rather than through a library because the format needed here is small — a
/// styled body and a table — and the alternative was another dependency for two file types.
///
/// The part that decides whether the file opens at all is the <c>mimetype</c> entry: it must be
/// the <em>first</em> entry in the archive and it must be <em>stored, not deflated</em>. That is
/// how a reader identifies an ODF package before parsing anything. Get it wrong and LibreOffice
/// reports a corrupt file while every ZIP tool in the world says the archive is fine — which is
/// exactly the failure mode the PowerPoint relationship bug had, so it is tested directly.
/// </summary>
internal static class OpenDocumentBuilder
{
    /// <summary>
    /// 1.2, not 1.3. Word writes 1.2 when it saves ODF itself, and refuses a 1.3 package as
    /// corrupt — see the note in the class summary about which check settles this.
    /// </summary>
    private const string OdfVersion = "1.2";

    private const string TextMime = "application/vnd.oasis.opendocument.text";
    private const string SheetMime = "application/vnd.oasis.opendocument.spreadsheet";

    private const string OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private const string StyleNs = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private const string FoNs = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private const string ManifestNs = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    /// <summary>OpenFormula. Undeclared, a formula attribute is a string nothing can resolve.</summary>
    private const string OfNs = "urn:oasis:names:tc:opendocument:xmlns:of:1.2";

    /// <summary>An .odt from the same block model the Word and PDF writers use.</summary>
    public static void WriteText(string path, IEnumerable<DocumentTools.MarkdownBlock> blocks, string? title)
    {
        var content = new StringBuilder();
        using (var writer = XmlWriter.Create(new Utf8StringWriter(content), WriterSettings()))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("office", "document-content", OfficeNs);
            WriteCommonNamespaces(writer);
            writer.WriteAttributeString("office", "version", OfficeNs, OdfVersion);

            WriteAutomaticTextStyles(writer);

            writer.WriteStartElement("office", "body", OfficeNs);
            writer.WriteStartElement("office", "text", OfficeNs);

            if (!string.IsNullOrWhiteSpace(title)) WriteHeading(writer, title, 1);

            foreach (var block in blocks)
            {
                switch (block.Kind)
                {
                    case DocumentTools.BlockKind.Heading1: WriteHeading(writer, block.Text, 1); break;
                    case DocumentTools.BlockKind.Heading2: WriteHeading(writer, block.Text, 2); break;
                    case DocumentTools.BlockKind.Heading3: WriteHeading(writer, block.Text, 3); break;

                    // Bullets are written as paragraphs carrying the marker, matching what the
                    // Word writer does. A real list needs list styles that add nothing here.
                    case DocumentTools.BlockKind.Bullet: WriteParagraph(writer, "• " + block.Text, "Bullet"); break;

                    default: WriteParagraph(writer, block.Text, "Standard"); break;
                }
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        WritePackage(path, TextMime, content.ToString());
    }

    /// <summary>An .ods with one table per sheet. Cells starting with = become formulas.</summary>
    public static void WriteSheet(string path, IReadOnlyList<(string Name, IReadOnlyList<IReadOnlyList<string>> Rows)> sheets)
    {
        var content = new StringBuilder();
        using (var writer = XmlWriter.Create(new Utf8StringWriter(content), WriterSettings()))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("office", "document-content", OfficeNs);
            WriteCommonNamespaces(writer);
            writer.WriteAttributeString("office", "version", OfficeNs, OdfVersion);

            writer.WriteStartElement("office", "body", OfficeNs);
            writer.WriteStartElement("office", "spreadsheet", OfficeNs);

            foreach (var (name, rows) in sheets)
            {
                writer.WriteStartElement("table", "table", TableNs);
                writer.WriteAttributeString("table", "name", TableNs, name);

                foreach (var row in rows)
                {
                    writer.WriteStartElement("table", "table-row", TableNs);

                    foreach (var cell in row)
                    {
                        writer.WriteStartElement("table", "table-cell", TableNs);

                        if (cell.StartsWith('='))
                        {
                            writer.WriteAttributeString("table", "formula", TableNs, ToOpenFormula(cell));
                            writer.WriteAttributeString("office", "value-type", OfficeNs, "float");
                            writer.WriteAttributeString("office", "value", OfficeNs, "0");
                        }
                        else if (double.TryParse(cell, System.Globalization.NumberStyles.Any,
                                     System.Globalization.CultureInfo.InvariantCulture, out var number))
                        {
                            writer.WriteAttributeString("office", "value-type", OfficeNs, "float");
                            writer.WriteAttributeString("office", "value", OfficeNs,
                                number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            writer.WriteAttributeString("office", "value-type", OfficeNs, "string");
                        }

                        writer.WriteStartElement("text", "p", TextNs);
                        writer.WriteString(cell);
                        writer.WriteEndElement();

                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        WritePackage(path, SheetMime, content.ToString());
    }

    // ── Package ───────────────────────────────────────────────────────────────────────────

    private static void WritePackage(string path, string mimeType, string contentXml)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        // First, and stored rather than deflated. Both matter — see the class comment.
        var mime = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var mimeWriter = new StreamWriter(mime.Open(), new UTF8Encoding(false)))
            mimeWriter.Write(mimeType);

        WriteEntry(archive, "META-INF/manifest.xml", BuildManifest(mimeType));
        WriteEntry(archive, "content.xml", contentXml);
        WriteEntry(archive, "styles.xml", BuildStyles());
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string BuildManifest(string mimeType)
    {
        var manifest = new StringBuilder();
        using var writer = XmlWriter.Create(new Utf8StringWriter(manifest), WriterSettings());

        writer.WriteStartDocument();
        writer.WriteStartElement("manifest", "manifest", ManifestNs);
        writer.WriteAttributeString("manifest", "version", ManifestNs, OdfVersion);

        foreach (var (full, media) in new[]
                 {
                     ("/", mimeType),
                     ("content.xml", "text/xml"),
                     ("styles.xml", "text/xml"),
                 })
        {
            writer.WriteStartElement("manifest", "file-entry", ManifestNs);
            writer.WriteAttributeString("manifest", "full-path", ManifestNs, full);
            writer.WriteAttributeString("manifest", "media-type", ManifestNs, media);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();

        return manifest.ToString();
    }

    private static string BuildStyles()
    {
        var styles = new StringBuilder();
        using var writer = XmlWriter.Create(new Utf8StringWriter(styles), WriterSettings());

        writer.WriteStartDocument();
        writer.WriteStartElement("office", "document-styles", OfficeNs);
        WriteCommonNamespaces(writer);
        writer.WriteAttributeString("office", "version", OfficeNs, "1.3");
        writer.WriteElementString("office", "styles", OfficeNs, "");
        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();

        return styles.ToString();
    }

    // ── Styles and elements ───────────────────────────────────────────────────────────────

    private static void WriteCommonNamespaces(XmlWriter writer)
    {
        writer.WriteAttributeString("xmlns", "office", null, OfficeNs);
        writer.WriteAttributeString("xmlns", "text", null, TextNs);
        writer.WriteAttributeString("xmlns", "table", null, TableNs);
        writer.WriteAttributeString("xmlns", "style", null, StyleNs);
        writer.WriteAttributeString("xmlns", "fo", null, FoNs);
        writer.WriteAttributeString("xmlns", "of", null, OfNs);
    }

    /// <summary>
    /// Rewrites an A1-style formula into OpenFormula, which is what an ODF spreadsheet expects:
    /// <c>=SUM(B2:C2)</c> becomes <c>of:=SUM([.B2:.C2])</c>.
    ///
    /// Both halves matter. Without the <c>of:</c> prefix and its namespace declaration the
    /// attribute is an unresolvable string; with the prefix but plain A1 references, Excel opens
    /// the file, shows the cell, and silently drops the formula — which is how this was found.
    ///
    /// Quoted text is copied through untouched, so <c>="B2 is fine"</c> stays a string. Anything
    /// more elaborate than plain references and ranges — sheet-qualified names, structured
    /// references — is passed through as written rather than half-translated.
    /// </summary>
    internal static string ToOpenFormula(string a1Formula)
    {
        var body = a1Formula.StartsWith('=') ? a1Formula[1..] : a1Formula;
        var output = new StringBuilder("of:=");
        var index = 0;

        while (index < body.Length)
        {
            var c = body[index];

            if (c == '"')
            {
                var close = body.IndexOf('"', index + 1);
                if (close < 0) close = body.Length - 1;

                output.Append(body, index, close - index + 1);
                index = close + 1;
                continue;
            }

            var reference = CellReference.Match(body[index..]);

            // Anchored at the current position, and never a function name: SUM( has no digits,
            // but a match must also not be the start of something like A1B.
            if (reference.Success && reference.Index == 0)
            {
                var text = reference.Value;
                var colon = text.IndexOf(':');

                output.Append(colon >= 0
                    ? $"[.{text[..colon]}:.{text[(colon + 1)..]}]"
                    : $"[.{text}]");

                index += text.Length;
                continue;
            }

            output.Append(c);
            index++;
        }

        return output.ToString();
    }

    /// <summary>A1, $A$1, or a range of two of them. Not followed by a letter, digit or "(".</summary>
    private static readonly System.Text.RegularExpressions.Regex CellReference = new(
        @"^\$?[A-Za-z]{1,3}\$?[0-9]{1,7}(?::\$?[A-Za-z]{1,3}\$?[0-9]{1,7})?(?![A-Za-z0-9_(])",
        System.Text.RegularExpressions.RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static void WriteAutomaticTextStyles(XmlWriter writer)
    {
        writer.WriteStartElement("office", "automatic-styles", OfficeNs);

        foreach (var (name, size, bold) in new[]
                 {
                     ("H1", "18pt", true), ("H2", "15pt", true), ("H3", "13pt", true),
                     ("Standard", "11pt", false), ("Bullet", "11pt", false),
                 })
        {
            writer.WriteStartElement("style", "style", StyleNs);
            writer.WriteAttributeString("style", "name", StyleNs, name);
            writer.WriteAttributeString("style", "family", StyleNs, "paragraph");

            writer.WriteStartElement("style", "text-properties", StyleNs);
            writer.WriteAttributeString("fo", "font-size", FoNs, size);
            if (bold) writer.WriteAttributeString("fo", "font-weight", FoNs, "bold");
            writer.WriteEndElement();

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteHeading(XmlWriter writer, string text, int level)
    {
        writer.WriteStartElement("text", "h", TextNs);
        writer.WriteAttributeString("text", "outline-level", TextNs, level.ToString());
        writer.WriteAttributeString("text", "style-name", TextNs, "H" + level);
        writer.WriteString(text);
        writer.WriteEndElement();
    }

    private static void WriteParagraph(XmlWriter writer, string text, string style)
    {
        writer.WriteStartElement("text", "p", TextNs);
        writer.WriteAttributeString("text", "style-name", TextNs, style);
        writer.WriteString(text);
        writer.WriteEndElement();
    }

    private static XmlWriterSettings WriterSettings() => new()
    {
        Indent = false,
        OmitXmlDeclaration = false,
        Encoding = new UTF8Encoding(false),
    };

    /// <summary>
    /// A <see cref="StringWriter"/> that admits to being UTF-8.
    ///
    /// <see cref="XmlWriter"/> takes the encoding for its declaration from the writer it is given,
    /// not from <see cref="XmlWriterSettings.Encoding"/>, and a plain <c>StringWriter</c> reports
    /// UTF-16 because that is what a .NET string is. The declaration then reads
    /// <c>encoding="utf-16"</c> while the bytes written to the package are UTF-8, and every ODF
    /// reader rejects the file.
    ///
    /// It survives a well-formedness check, because parsing a <em>string</em> ignores the
    /// declaration entirely — so this was invisible until the file was opened in Word.
    /// </summary>
    private sealed class Utf8StringWriter : StringWriter
    {
        public Utf8StringWriter(StringBuilder builder) : base(builder) { }

        public override Encoding Encoding => Encoding.UTF8;
    }
}
