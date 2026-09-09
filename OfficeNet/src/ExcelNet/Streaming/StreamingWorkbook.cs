// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using OfficeNet.Core;
using OfficeNet.Core.Packaging;

namespace ExcelNet.Streaming;

/// <summary>
/// Writes a workbook a row at a time, without holding it in memory.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Workbook"/> parses a file into a model and writes the model back, which is what makes
/// random access to a cell an O(1) dictionary lookup and what makes a five-million-row export
/// impossible. This writes straight into the package's ZIP entry instead: a row is serialised and
/// forgotten, so memory stays flat however long the export runs.
/// </para>
/// <para>
/// The trade is that it is write-only and forward-only. There is no reading a cell back, no going
/// back to a previous row, and no feature that needs to know the whole sheet — no formula
/// evaluation, no charts, no pivots, no conditional formatting, no autofit. When any of those
/// matter, use <see cref="Workbook"/>; the two are different tools because the jobs are different.
/// </para>
/// <para>
/// <b>The sheet names are given up front.</b> A package's <c>[Content_Types].xml</c> has to be the
/// first entry in the ZIP, and it names every part in the file — so the sheets have to be known
/// before the first byte of the first one is written. There is no way round it that does not involve
/// writing the whole file twice.
/// </para>
/// <para>
/// <b>Strings are written inline, not shared.</b> A shared-string table is a dictionary of every
/// distinct string in the file, and it has to be complete before it can be written — which means
/// holding it all in memory, which is the thing this class exists to avoid. Inline strings make the
/// file larger and, on data with heavy repetition, noticeably so; that is the price of not knowing
/// the last row before writing the first. Excel reads both without complaint.
/// </para>
/// <example>
/// <code>
/// using var workbook = StreamingWorkbook.Create("besar.xlsx", "Data");
/// using var sheet = workbook.Sheet("Data");
///
/// sheet.WriteHeader("Tanggal", "Wilayah", "Jumlah");
///
/// foreach (var record in source)
/// {
///     sheet.WriteRow(record.Date, record.Region, record.Amount);
/// }
/// </code>
/// </example>
/// </remarks>
public sealed class StreamingWorkbook : IDisposable
{
    private readonly ZipArchive _archive;
    private readonly Stream? _ownedStream;
    private readonly List<string> _sheetNames;
    private readonly HashSet<string> _written = new(StringComparer.OrdinalIgnoreCase);

    private StreamingSheet? _current;
    private bool _closed;

    private StreamingWorkbook(ZipArchive archive, Stream? ownedStream, List<string> sheetNames)
    {
        _archive = archive;
        _ownedStream = ownedStream;
        _sheetNames = sheetNames;

        // First, because the OPC ZIP mapping says so and because a consumer that streams rather than
        // seeks has nothing to go on until it has read this.
        WriteContentTypes();
        WriteRootRelationships();
    }

    /// <summary>Creates a workbook that writes to a file.</summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="sheetNames">
    /// The sheets, in order. They are fixed for the life of the workbook — see the class remarks for
    /// why. Passing none gives a single sheet called <c>Sheet1</c>.
    /// </param>
    public static StreamingWorkbook Create(string path, params string[] sheetNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var names = Validate(sheetNames);

        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 1 << 16, FileOptions.SequentialScan);

        try
        {
            return new StreamingWorkbook(
                new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false), stream, names);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Creates a workbook that writes to a stream, which the caller keeps ownership of.</summary>
    public static StreamingWorkbook Create(Stream stream, params string[] sheetNames)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return new StreamingWorkbook(
            new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true), ownedStream: null,
            Validate(sheetNames));
    }

    private static List<string> Validate(string[] sheetNames)
    {
        ArgumentNullException.ThrowIfNull(sheetNames);

        if (sheetNames.Length == 0)
        {
            // Excel refuses a workbook with no sheets, so the empty case gets one rather than
            // producing a file nobody can open.
            return ["Sheet1"];
        }

        foreach (var name in sheetNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(sheetNames));
        }

        if (sheetNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != sheetNames.Length)
        {
            throw new OfficeNetException(
                "Two sheets in the same workbook cannot share a name. Excel compares them without " +
                "regard to case, so \"Data\" and \"data\" are the same name.");
        }

        return [.. sheetNames];
    }

    /// <summary>The workbook's sheets, in order.</summary>
    public IReadOnlyList<string> SheetNames => _sheetNames;

    /// <summary>
    /// Opens one of the workbook's sheets for writing.
    /// </summary>
    /// <remarks>
    /// A ZIP entry is written from start to finish before the next one begins, so only one sheet can
    /// be open at a time. Opening a second closes the first, and a sheet cannot be reopened once its
    /// entry has been written — which is what forward-only means at the workbook level.
    /// </remarks>
    /// <exception cref="OfficeNetException">
    /// The name is not one of the workbook's sheets, or that sheet has already been written.
    /// </exception>
    public StreamingSheet Sheet(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ObjectDisposedException.ThrowIf(_closed, this);

        var index = _sheetNames.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            throw new OfficeNetException(
                $"This workbook has no sheet called \"{name}\". Its sheets are " +
                $"{string.Join(", ", _sheetNames)}, and they are fixed at creation because " +
                "[Content_Types].xml has to name every part before the first one is written.");
        }

        if (!_written.Add(_sheetNames[index]))
        {
            throw new OfficeNetException(
                $"Sheet \"{name}\" has already been written. A streamed sheet cannot be reopened: " +
                "its ZIP entry is closed and the bytes are gone.");
        }

        _current?.Close();

        var entry = _archive.CreateEntry(
            $"xl/worksheets/sheet{index + 1}.xml", CompressionLevel.Fastest);

        _current = new StreamingSheet(_sheetNames[index], entry.Open());
        return _current;
    }

    /// <summary>Opens one of the workbook's sheets for writing.</summary>
    public StreamingSheet this[string name] => Sheet(name);

    /// <summary>Finishes the package: the last sheet, the workbook part, and the stylesheet.</summary>
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        _current?.Close();
        _current = null;

        // A sheet nobody wrote to still has to exist: [Content_Types].xml already promised it, and a
        // part named there but absent from the package makes Excel offer to repair the file.
        for (var i = 0; i < _sheetNames.Count; i++)
        {
            if (_written.Contains(_sheetNames[i]))
            {
                continue;
            }

            using var empty = new StreamingSheet(_sheetNames[i],
                _archive.CreateEntry($"xl/worksheets/sheet{i + 1}.xml", CompressionLevel.Fastest).Open());
        }

        WriteWorkbook();
        WriteWorkbookRelationships();
        WriteStyles();
    }

    public void Dispose()
    {
        Close();
        _archive.Dispose();
        _ownedStream?.Dispose();
    }

    // ---- Package parts -----------------------------------------------------------------------

    private void WriteEntry(string path, Action<XmlWriter> body)
    {
        using var stream = _archive.CreateEntry(path, CompressionLevel.Optimal).Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Indent = false,
            Encoding = new UTF8Encoding(false),
            CloseOutput = false,
        });

        writer.WriteStartDocument(standalone: true);
        body(writer);
        writer.WriteEndDocument();
    }

    private void WriteContentTypes() => WriteEntry("[Content_Types].xml", writer =>
    {
        const string TypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";

        writer.WriteStartElement("Types", TypesNamespace);

        writer.WriteStartElement("Default", TypesNamespace);
        writer.WriteAttributeString("Extension", "rels");
        writer.WriteAttributeString("ContentType",
            "application/vnd.openxmlformats-package.relationships+xml");
        writer.WriteEndElement();

        writer.WriteStartElement("Default", TypesNamespace);
        writer.WriteAttributeString("Extension", "xml");
        writer.WriteAttributeString("ContentType", "application/xml");
        writer.WriteEndElement();

        Override("/xl/workbook.xml", ContentTypes.ExcelWorkbook);
        Override("/xl/styles.xml", ContentTypes.ExcelStyles);

        for (var i = 1; i <= _sheetNames.Count; i++)
        {
            Override($"/xl/worksheets/sheet{i}.xml", ContentTypes.ExcelWorksheet);
        }

        writer.WriteEndElement();

        void Override(string part, string contentType)
        {
            writer.WriteStartElement("Override", TypesNamespace);
            writer.WriteAttributeString("PartName", part);
            writer.WriteAttributeString("ContentType", contentType);
            writer.WriteEndElement();
        }
    });

    private void WriteRootRelationships() => WriteEntry("_rels/.rels", writer =>
    {
        writer.WriteStartElement("Relationships", RelationshipsNamespace);
        WriteRelationship(writer, "rId1", RelationshipTypes.OfficeDocument, "xl/workbook.xml");
        writer.WriteEndElement();
    });

    private void WriteWorkbook() => WriteEntry("xl/workbook.xml", writer =>
    {
        writer.WriteStartElement("workbook", SpreadsheetNamespace);
        writer.WriteAttributeString("xmlns", "r", null, RelationshipNamespace);

        writer.WriteStartElement("sheets", SpreadsheetNamespace);

        for (var i = 0; i < _sheetNames.Count; i++)
        {
            writer.WriteStartElement("sheet", SpreadsheetNamespace);
            writer.WriteAttributeString("name", _sheetNames[i]);
            writer.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("id", RelationshipNamespace,
                $"rId{i + 1}");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    });

    private void WriteWorkbookRelationships() => WriteEntry("xl/_rels/workbook.xml.rels", writer =>
    {
        writer.WriteStartElement("Relationships", RelationshipsNamespace);

        for (var i = 1; i <= _sheetNames.Count; i++)
        {
            WriteRelationship(writer, $"rId{i}", RelationshipTypes.Worksheet,
                $"worksheets/sheet{i}.xml");
        }

        // The stylesheet takes the id after the last sheet, so the sheet ids stay contiguous from 1
        // and match the order in workbook.xml.
        WriteRelationship(writer, $"rId{_sheetNames.Count + 1}",
            RelationshipTypes.SpreadsheetStyles, "styles.xml");

        writer.WriteEndElement();
    });

    /// <summary>
    /// Writes the smallest stylesheet Excel accepts, plus the formats the writer can apply.
    /// </summary>
    /// <remarks>
    /// Fill 0 must be <c>none</c> and fill 1 <c>gray125</c>: Excel hard-codes both indices, and a
    /// stylesheet that puts anything else there renders every fill wrong. The same goes for having
    /// at least one font, border and cellXf even when nothing uses them.
    /// </remarks>
    private void WriteStyles() => WriteEntry("xl/styles.xml", writer =>
    {
        writer.WriteStartElement("styleSheet", SpreadsheetNamespace);

        writer.WriteStartElement("fonts", SpreadsheetNamespace);
        writer.WriteAttributeString("count", "2");

        Font(bold: false);
        Font(bold: true);

        writer.WriteEndElement();

        writer.WriteStartElement("fills", SpreadsheetNamespace);
        writer.WriteAttributeString("count", "2");
        Fill("none");
        Fill("gray125");
        writer.WriteEndElement();

        writer.WriteStartElement("borders", SpreadsheetNamespace);
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("border", SpreadsheetNamespace);
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("cellStyleXfs", SpreadsheetNamespace);
        writer.WriteAttributeString("count", "1");
        Xf(fontId: 0, numberFormatId: 0, applyFont: false, applyNumberFormat: false);
        writer.WriteEndElement();

        writer.WriteStartElement("cellXfs", SpreadsheetNamespace);
        writer.WriteAttributeString("count", "4");

        // The indices StreamingSheet writes. They are fixed rather than looked up, because a lookup
        // table is state that grows, and this class exists not to grow state.
        Xf(0, 0, false, false);       // 0: general
        Xf(1, 0, true, false);        // 1: bold, for a header row
        Xf(0, 14, false, true);       // 2: date, built-in mm-dd-yy (localised by Excel)
        Xf(0, 22, false, true);       // 3: date and time

        writer.WriteEndElement();
        writer.WriteEndElement();

        void Font(bool bold)
        {
            writer.WriteStartElement("font", SpreadsheetNamespace);

            if (bold)
            {
                writer.WriteStartElement("b", SpreadsheetNamespace);
                writer.WriteEndElement();
            }

            Value("sz", "11");
            Value("name", "Calibri");
            Value("family", "2");

            writer.WriteEndElement();
        }

        void Fill(string pattern)
        {
            writer.WriteStartElement("fill", SpreadsheetNamespace);
            writer.WriteStartElement("patternFill", SpreadsheetNamespace);
            writer.WriteAttributeString("patternType", pattern);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        void Xf(int fontId, int numberFormatId, bool applyFont, bool applyNumberFormat)
        {
            writer.WriteStartElement("xf", SpreadsheetNamespace);
            writer.WriteAttributeString("numFmtId", numberFormatId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("fontId", fontId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("fillId", "0");
            writer.WriteAttributeString("borderId", "0");

            // Excel ignores a format outright unless the matching apply flag is set. This is the top
            // reason a hand-written stylesheet appears to do nothing.
            if (applyFont)
            {
                writer.WriteAttributeString("applyFont", "1");
            }

            if (applyNumberFormat)
            {
                writer.WriteAttributeString("applyNumberFormat", "1");
            }

            writer.WriteEndElement();
        }

        void Value(string name, string value)
        {
            writer.WriteStartElement(name, SpreadsheetNamespace);
            writer.WriteAttributeString("val", value);
            writer.WriteEndElement();
        }
    });

    private static void WriteRelationship(XmlWriter writer, string id, string type, string target)
    {
        writer.WriteStartElement("Relationship", RelationshipsNamespace);
        writer.WriteAttributeString("Id", id);
        writer.WriteAttributeString("Type", type);
        writer.WriteAttributeString("Target", target);
        writer.WriteEndElement();
    }

    internal const string SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string RelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string RelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
}
