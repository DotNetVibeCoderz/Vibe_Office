// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text;
using System.Xml;
using OfficeNet.Core;

namespace ExcelNet.Streaming;

/// <summary>
/// One sheet of a <see cref="StreamingWorkbook"/>, written a row at a time.
/// </summary>
/// <remarks>
/// Forward-only: a row that has been written has left memory and cannot be read back or changed.
/// Rows are numbered automatically, in the order they are written.
/// </remarks>
public sealed class StreamingSheet : IDisposable
{
    /// <summary>Applies no formatting.</summary>
    public const int GeneralStyle = 0;

    /// <summary>Bold, for a header row.</summary>
    public const int HeaderStyle = 1;

    /// <summary>A date.</summary>
    public const int DateStyle = 2;

    /// <summary>A date with a time.</summary>
    public const int DateTimeStyle = 3;

    private readonly Stream _stream;
    private readonly XmlWriter _writer;

    private int _row;
    private bool _closed;

    internal StreamingSheet(string name, Stream stream)
    {
        Name = name;
        _stream = stream;

        _writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Indent = false,
            Encoding = new UTF8Encoding(false),
            CloseOutput = false,
        });

        _writer.WriteStartDocument(standalone: true);
        _writer.WriteStartElement("worksheet", StreamingWorkbook.SpreadsheetNamespace);

        // No dimension element: it names the used range, and the used range is not known until the
        // last row has been written. Excel recomputes it on open, and the schema makes it optional
        // for exactly this reason.
        _writer.WriteStartElement("sheetData", StreamingWorkbook.SpreadsheetNamespace);
    }

    /// <summary>The sheet's name.</summary>
    public string Name { get; }

    /// <summary>How many rows have been written.</summary>
    public int RowCount => _row;

    /// <summary>Writes a row of values, each cell typed from the object it holds.</summary>
    /// <remarks>
    /// <c>null</c> writes an empty cell rather than an empty string, which is the difference between
    /// a gap in the data and a value that happens to be blank — and the difference between
    /// <c>COUNT</c> and <c>COUNTA</c> agreeing with the source and not.
    /// </remarks>
    public void WriteRow(params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ObjectDisposedException.ThrowIf(_closed, this);

        _row++;
        _writer.WriteStartElement("row", StreamingWorkbook.SpreadsheetNamespace);
        _writer.WriteAttributeString("r", _row.ToString(CultureInfo.InvariantCulture));

        for (var column = 0; column < values.Length; column++)
        {
            WriteCell(column, values[column], StyleFor(values[column]));
        }

        _writer.WriteEndElement();
    }

    /// <summary>Writes a bold row, for column titles.</summary>
    public void WriteHeader(params string[] titles)
    {
        ArgumentNullException.ThrowIfNull(titles);
        ObjectDisposedException.ThrowIf(_closed, this);

        _row++;
        _writer.WriteStartElement("row", StreamingWorkbook.SpreadsheetNamespace);
        _writer.WriteAttributeString("r", _row.ToString(CultureInfo.InvariantCulture));

        for (var column = 0; column < titles.Length; column++)
        {
            WriteCell(column, titles[column], HeaderStyle);
        }

        _writer.WriteEndElement();
    }

    /// <summary>Writes a row with a style index of your own for each cell.</summary>
    /// <param name="values">The values.</param>
    /// <param name="styles">
    /// One of <see cref="GeneralStyle"/>, <see cref="HeaderStyle"/>, <see cref="DateStyle"/> or
    /// <see cref="DateTimeStyle"/> per value. A shorter array leaves the remaining cells unstyled.
    /// </param>
    public void WriteRow(IReadOnlyList<object?> values, IReadOnlyList<int> styles)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(styles);
        ObjectDisposedException.ThrowIf(_closed, this);

        _row++;
        _writer.WriteStartElement("row", StreamingWorkbook.SpreadsheetNamespace);
        _writer.WriteAttributeString("r", _row.ToString(CultureInfo.InvariantCulture));

        for (var column = 0; column < values.Count; column++)
        {
            var style = column < styles.Count ? styles[column] : StyleFor(values[column]);
            WriteCell(column, values[column], style);
        }

        _writer.WriteEndElement();
    }

    /// <summary>Skips rows, leaving them empty.</summary>
    public void SkipRows(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _row += count;
    }

    /// <summary>Finishes the sheet. Called for you when the workbook closes.</summary>
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        _writer.WriteEndElement();   // sheetData
        _writer.WriteEndElement();   // worksheet
        _writer.WriteEndDocument();
        _writer.Flush();
        _writer.Dispose();
        _stream.Dispose();
    }

    public void Dispose() => Close();

    // ---- Cells ---------------------------------------------------------------------------------

    private void WriteCell(int column, object? value, int style)
    {
        if (value is null)
        {
            return;
        }

        _writer.WriteStartElement("c", StreamingWorkbook.SpreadsheetNamespace);
        _writer.WriteAttributeString("r", Reference(column, _row));

        if (style != GeneralStyle)
        {
            _writer.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
        }

        switch (value)
        {
            case string text:
                WriteInlineString(text);
                break;

            case bool flag:
                _writer.WriteAttributeString("t", "b");
                Value(flag ? "1" : "0");
                break;

            // "R" round-trips a double exactly. A fixed number of decimals looks tidier and loses
            // microseconds off a date-time serial, so 09:30:00 comes back as 09:29:59.999997.
            case DateTime date:
                Value(CellValue.ToSerial(date).ToString("R", CultureInfo.InvariantCulture));
                break;

            case DateOnly day:
                Value(CellValue.ToSerial(day.ToDateTime(TimeOnly.MinValue))
                    .ToString("R", CultureInfo.InvariantCulture));
                break;

            case DateTimeOffset moment:
                Value(CellValue.ToSerial(moment.DateTime).ToString("R", CultureInfo.InvariantCulture));
                break;

            case float or double:
                Value(Convert.ToDouble(value, CultureInfo.InvariantCulture)
                    .ToString("R", CultureInfo.InvariantCulture));
                break;

            case IFormattable number when value is byte or sbyte or short or ushort or int
                or uint or long or ulong or decimal:
                Value(number.ToString(null, CultureInfo.InvariantCulture));
                break;

            default:
                WriteInlineString(value.ToString() ?? string.Empty);
                break;
        }

        _writer.WriteEndElement();

        void Value(string text)
        {
            _writer.WriteStartElement("v", StreamingWorkbook.SpreadsheetNamespace);
            _writer.WriteString(text);
            _writer.WriteEndElement();
        }
    }

    /// <summary>
    /// Writes a string into the cell itself rather than into a shared table.
    /// </summary>
    /// <remarks>
    /// A shared-string table has to be complete before it can be written, which means holding every
    /// distinct string in the file in memory — the thing this writer exists to avoid. Inline strings
    /// cost file size instead, which is the right way round when the row count is the problem.
    /// </remarks>
    private void WriteInlineString(string text)
    {
        _writer.WriteAttributeString("t", "inlineStr");
        _writer.WriteStartElement("is", StreamingWorkbook.SpreadsheetNamespace);
        _writer.WriteStartElement("t", StreamingWorkbook.SpreadsheetNamespace);

        // Without xml:space="preserve" a leading or trailing space is discarded on read, silently
        // turning " 001" into "001".
        if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])))
        {
            _writer.WriteAttributeString("space", "http://www.w3.org/XML/1998/namespace", "preserve");
        }

        _writer.WriteString(Sanitise(text));
        _writer.WriteEndElement();
        _writer.WriteEndElement();
    }

    /// <summary>
    /// Removes the characters XML 1.0 cannot carry.
    /// </summary>
    /// <remarks>
    /// A control character in a database column is common and legal; in an XML document it is not,
    /// and writing one produces a file every reader rejects. Dropping it loses a character nothing
    /// could have displayed; keeping it loses the file.
    /// </remarks>
    private static string Sanitise(string text)
    {
        var needsWork = false;

        foreach (var c in text)
        {
            if (c < 0x20 && c is not ('\t' or '\n' or '\r'))
            {
                needsWork = true;
                break;
            }
        }

        if (!needsWork)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            if (c >= 0x20 || c is '\t' or '\n' or '\r')
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static int StyleFor(object? value) => value switch
    {
        DateTime date => date.TimeOfDay == TimeSpan.Zero ? DateStyle : DateTimeStyle,
        DateOnly => DateStyle,
        DateTimeOffset moment => moment.TimeOfDay == TimeSpan.Zero ? DateStyle : DateTimeStyle,
        _ => GeneralStyle,
    };

    /// <summary>Builds an A1 reference without allocating a <see cref="CellReference"/>.</summary>
    /// <remarks>
    /// Called once per cell, so a five-million-row export calls it tens of millions of times. The
    /// stack buffer is the difference between that being free and it being the export's dominant
    /// allocation.
    /// </remarks>
    private static string Reference(int column, int row)
    {
        if (column is < 0 or >= 16384)
        {
            throw new OfficeNetException(
                $"Column {column} is outside the sheet: Excel has 16,384 columns, numbered 0 to 16,383.");
        }

        Span<char> buffer = stackalloc char[3 + 7];
        var index = 0;
        Span<char> letters = stackalloc char[3];
        var letterCount = 0;

        for (var value = column; ; value = value / 26 - 1)
        {
            letters[letterCount++] = (char)('A' + (value % 26));

            if (value < 26)
            {
                break;
            }
        }

        for (var i = letterCount - 1; i >= 0; i--)
        {
            buffer[index++] = letters[i];
        }

        row.TryFormat(buffer[index..], out var written, provider: CultureInfo.InvariantCulture);

        return new string(buffer[..(index + written)]);
    }
}
