// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ExcelNet.Styles;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;

namespace ExcelNet.Io;

/// <summary>How a CSV file is read and written.</summary>
public sealed class CsvOptions
{
    /// <summary>The field separator.</summary>
    public char Delimiter { get; set; } = ',';

    /// <summary>The quote character.</summary>
    public char Quote { get; set; } = '"';

    /// <summary>True when the first row holds column names.</summary>
    public bool HasHeader { get; set; } = true;

    /// <summary>The text encoding; UTF-8 by default.</summary>
    public Encoding Encoding { get; set; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>The culture used to parse and format numbers and dates.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    /// <summary>
    /// Converts text that looks numeric or date-like into typed cells.
    /// </summary>
    /// <remarks>
    /// On by default because a CSV of numbers imported as text is useless for analysis. Turn it off
    /// for data where an identifier could be mistaken for a number — a leading-zero product code
    /// becomes 1234 and loses its zero, which is the classic spreadsheet data-loss bug.
    /// </remarks>
    public bool InferTypes { get; set; } = true;

    /// <summary>
    /// A CSV configured for the Indonesian locale: semicolon separated, comma decimal mark.
    /// </summary>
    public static CsvOptions Indonesian => new()
    {
        Delimiter = ';',
        Culture = new CultureInfo("id-ID"),
    };
}

/// <summary>Reading and writing CSV.</summary>
public static class CsvIo
{
    /// <summary>Loads a CSV file into a worksheet, starting at A1.</summary>
    public static Worksheet Import(Workbook workbook, string path, string? sheetName = null,
        CsvOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        options ??= new CsvOptions();
        var text = File.ReadAllText(path, options.Encoding);
        var name = sheetName ?? Path.GetFileNameWithoutExtension(path);

        return ImportText(workbook, text, name, options);
    }

    /// <summary>Loads CSV text into a worksheet.</summary>
    public static Worksheet ImportText(Workbook workbook, string csv, string sheetName,
        CsvOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(csv);

        options ??= new CsvOptions();

        // A sheet name is capped at 31 characters and a file name easily exceeds that.
        var trimmed = sheetName.Length > 31 ? sheetName[..31] : sheetName;
        var sheet = workbook.Find(trimmed) ?? workbook.AddSheetUnique(trimmed);

        var row = 0;

        foreach (var fields in Parse(csv, options))
        {
            for (var column = 0; column < fields.Count; column++)
            {
                var reference = new CellReference(row, column);
                var text = fields[column];

                if (row == 0 && options.HasHeader)
                {
                    sheet.SetValue(reference, CellValue.FromText(text));
                    continue;
                }

                sheet.SetValue(reference, options.InferTypes
                    ? Infer(text, options.Culture)
                    : CellValue.FromText(text));
            }

            row++;
        }

        if (options.HasHeader && row > 0)
        {
            var columns = sheet.UsedRange?.ColumnCount ?? 0;

            if (columns > 0)
            {
                sheet.Range(0, 0, 0, columns - 1).ApplyStyle(
                    CellStyle.Default.Bold()
                        .WithBackground(OfficeColor.FromRgb(0x1F, 0x38, 0x64))
                        .WithColor(OfficeColor.White));

                sheet.Frozen = new FreezePanes(1, 0);
                sheet.AutoFilter = new CellRangeReference(0, 0, 0, columns - 1);
            }
        }

        sheet.AutoFitColumns();
        return sheet;
    }

    /// <summary>
    /// Infers a cell type from text.
    /// </summary>
    /// <remarks>
    /// Leading zeros and a leading <c>+</c> are treated as text on purpose. "007" and "+62812..."
    /// are identifiers, not numbers, and converting them is a data loss the user cannot undo.
    /// </remarks>
    internal static CellValue Infer(string text, CultureInfo culture)
    {
        if (text.Length == 0)
        {
            return CellValue.Empty;
        }

        var trimmed = text.Trim();

        if (trimmed.Length == 0)
        {
            return CellValue.FromText(text);
        }

        if (trimmed.Length > 1 && trimmed[0] == '0' && trimmed[1] != '.' &&
            trimmed[1] != culture.NumberFormat.NumberDecimalSeparator[0])
        {
            return CellValue.FromText(text);
        }

        if (trimmed[0] == '+')
        {
            return CellValue.FromText(text);
        }

        if (bool.TryParse(trimmed, out var boolean))
        {
            return CellValue.FromBoolean(boolean);
        }

        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, culture,
                out var number))
        {
            return CellValue.FromNumber(number);
        }

        // Only unambiguous date formats are inferred. "01/02/2024" means different days in
        // different cultures, so it is only accepted when the culture is explicit.
        if (DateTime.TryParseExact(trimmed,
                ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy/MM/dd"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate))
        {
            return CellValue.FromDateTime(isoDate);
        }

        if (!ReferenceEquals(culture, CultureInfo.InvariantCulture) &&
            DateTime.TryParse(trimmed, culture, DateTimeStyles.None, out var localDate))
        {
            return CellValue.FromDateTime(localDate);
        }

        return CellValue.FromText(text);
    }

    /// <summary>
    /// Parses CSV, honouring quoted fields containing separators and newlines.
    /// </summary>
    /// <remarks>
    /// Splitting on the delimiter is the wrong implementation and the one everybody writes first: a
    /// quoted address field containing a comma turns into two columns and shifts every later column
    /// on that row. The state machine here is what makes an export from a real system readable.
    /// </remarks>
    public static IEnumerable<List<string>> Parse(string csv, CsvOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(csv);
        options ??= new CsvOptions();

        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;

        while (i < csv.Length)
        {
            var c = csv[i];

            if (inQuotes)
            {
                if (c == options.Quote)
                {
                    // A doubled quote inside a quoted field is one literal quote.
                    if (i + 1 < csv.Length && csv[i + 1] == options.Quote)
                    {
                        field.Append(options.Quote);
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                field.Append(c);
                i++;
                continue;
            }

            if (c == options.Quote && field.Length == 0)
            {
                inQuotes = true;
                i++;
                continue;
            }

            if (c == options.Delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                i++;
                continue;
            }

            if (c is '\r' or '\n')
            {
                fields.Add(field.ToString());
                field.Clear();

                yield return fields;
                fields = [];

                // CRLF is one line break.
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                {
                    i++;
                }

                i++;
                continue;
            }

            field.Append(c);
            i++;
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields;
        }
    }

    /// <summary>Writes a worksheet as CSV.</summary>
    public static void Export(Worksheet sheet, string path, CsvOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        options ??= new CsvOptions();
        File.WriteAllText(path, ExportText(sheet, options), options.Encoding);
    }

    /// <summary>Renders a worksheet as CSV text.</summary>
    public static string ExportText(Worksheet sheet, CsvOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        options ??= new CsvOptions();

        var builder = new StringBuilder();

        if (sheet.UsedRange is not { } used)
        {
            return string.Empty;
        }

        for (var row = used.Start.Row; row <= used.End.Row; row++)
        {
            for (var column = used.Start.Column; column <= used.End.Column; column++)
            {
                if (column > used.Start.Column)
                {
                    builder.Append(options.Delimiter);
                }

                var reference = new CellReference(row, column);
                var value = sheet[reference].Value;
                var style = sheet.GetStyle(reference);

                var text = value.ValueType switch
                {
                    CellValueType.Number when style.NumberFormat is not null =>
                        ExcelToPdf.Format(value, style.NumberFormat),
                    CellValueType.Number => value.AsNumber().ToString(options.Culture),
                    CellValueType.DateTime => value.AsDateTime().ToString("yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture),
                    _ => value.AsText(),
                };

                builder.Append(Quote(text, options));
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static string Quote(string value, CsvOptions options)
    {
        var needsQuotes = value.Contains(options.Delimiter) || value.Contains(options.Quote) ||
                          value.Contains('\n') || value.Contains('\r') ||
                          value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]));

        if (!needsQuotes)
        {
            return value;
        }

        return options.Quote + value.Replace(options.Quote.ToString(),
            new string(options.Quote, 2)) + options.Quote;
    }
}

/// <summary>Reading and writing JSON.</summary>
public static class JsonIo
{
    /// <summary>
    /// Writes a worksheet as an array of objects, one per row, keyed by the header row.
    /// </summary>
    public static string ExportText(Worksheet sheet, bool firstRowIsHeader = true, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var rows = new List<Dictionary<string, object?>>();

        if (sheet.UsedRange is not { } used)
        {
            return "[]";
        }

        var headers = new List<string>();

        for (var column = used.Start.Column; column <= used.End.Column; column++)
        {
            headers.Add(firstRowIsHeader
                ? sheet[new CellReference(used.Start.Row, column)].Text
                : CellReference.ColumnIndexToName(column));
        }

        // A duplicate or empty header would collide in the object; making them unique keeps every
        // column in the output.
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Length == 0)
            {
                headers[i] = CellReference.ColumnIndexToName(used.Start.Column + i);
            }

            var suffix = 2;
            var original = headers[i];

            while (headers.Take(i).Contains(headers[i], StringComparer.Ordinal))
            {
                headers[i] = $"{original}_{suffix++}";
            }
        }

        var firstDataRow = firstRowIsHeader ? used.Start.Row + 1 : used.Start.Row;

        for (var row = firstDataRow; row <= used.End.Row; row++)
        {
            var record = new Dictionary<string, object?>();

            for (var column = used.Start.Column; column <= used.End.Column; column++)
            {
                record[headers[column - used.Start.Column]] =
                    sheet[new CellReference(row, column)].Value.AsObject();
            }

            rows.Add(record);
        }

        return JsonSerializer.Serialize(rows, new JsonSerializerOptions
        {
            WriteIndented = indented,
            // Without this, non-ASCII characters are escaped as \uXXXX — legal JSON, unreadable
            // for Indonesian and CJK text.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    /// <summary>Writes a worksheet to a JSON file.</summary>
    public static void Export(Worksheet sheet, string path, bool firstRowIsHeader = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, ExportText(sheet, firstRowIsHeader), Encoding.UTF8);
    }

    /// <summary>Loads a JSON array of objects into a worksheet.</summary>
    /// <exception cref="OfficeNetException">The JSON is not an array of objects.</exception>
    public static Worksheet Import(Workbook workbook, string json, string sheetName)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new OfficeNetException(
                "The JSON must be an array of objects, one per row.");
        }

        // The column set is the union of every object's keys, in first-seen order, so a record
        // that omits an optional field does not shift the later columns.
        var headers = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var property in item.EnumerateObject())
            {
                if (seen.Add(property.Name))
                {
                    headers.Add(property.Name);
                }
            }
        }

        var trimmed = sheetName.Length > 31 ? sheetName[..31] : sheetName;
        var sheet = workbook.Find(trimmed) ?? workbook.AddSheetUnique(trimmed);

        sheet.WriteHeader(new CellReference(0, 0), headers);

        var row = 1;

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            for (var column = 0; column < headers.Count; column++)
            {
                if (!item.TryGetProperty(headers[column], out var property))
                {
                    continue;
                }

                sheet.SetValue(new CellReference(row, column), Convert(property));
            }

            row++;
        }

        sheet.AutoFitColumns();
        return sheet;
    }

    private static CellValue Convert(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => CellValue.FromNumber(element.GetDouble()),
        JsonValueKind.True => CellValue.FromBoolean(true),
        JsonValueKind.False => CellValue.FromBoolean(false),
        JsonValueKind.Null or JsonValueKind.Undefined => CellValue.Empty,
        JsonValueKind.String => element.TryGetDateTime(out var date)
            ? CellValue.FromDateTime(date)
            : CellValue.FromText(element.GetString() ?? string.Empty),
        // A nested object or array has no cell representation; its JSON text is the honest answer.
        _ => CellValue.FromText(element.GetRawText()),
    };
}

/// <summary>Reading from and writing to a database.</summary>
/// <remarks>
/// Binds to <see cref="DbConnection"/> rather than to any one provider, so the caller supplies
/// SQLite, SQL Server, PostgreSQL or anything else with an ADO.NET driver and ExcelNet takes no
/// dependency on it.
/// </remarks>
public static class SqlIo
{
    /// <summary>Runs a query and writes the result set to a worksheet.</summary>
    public static Worksheet Import(Workbook workbook, DbConnection connection, string sql,
        string sheetName, int maxRows = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = command.ExecuteReader();

        var trimmed = sheetName.Length > 31 ? sheetName[..31] : sheetName;
        var sheet = workbook.Find(trimmed) ?? workbook.AddSheetUnique(trimmed);

        var headers = new List<string>(reader.FieldCount);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            headers.Add(reader.GetName(i));
        }

        sheet.WriteHeader(new CellReference(0, 0), headers);

        var row = 1;

        while (reader.Read() && row - 1 < maxRows)
        {
            for (var column = 0; column < reader.FieldCount; column++)
            {
                var value = reader.IsDBNull(column) ? null : reader.GetValue(column);

                sheet.SetValue(new CellReference(row, column), value switch
                {
                    null => CellValue.Empty,
                    // A byte array is a BLOB; its length is more useful in a sheet than a
                    // meaningless string conversion.
                    byte[] bytes => CellValue.FromText($"<{bytes.Length} bytes>"),
                    Guid guid => CellValue.FromText(guid.ToString()),
                    char c => CellValue.FromText(c.ToString()),
                    _ => TryConvert(value),
                });
            }

            row++;
        }

        sheet.AutoFitColumns();
        return sheet;
    }

    private static CellValue TryConvert(object value)
    {
        try
        {
            return CellValue.From(value);
        }
        catch (ArgumentException)
        {
            return CellValue.FromText(value.ToString() ?? string.Empty);
        }
    }

    /// <summary>
    /// Writes a worksheet into a database table, creating it when it does not exist.
    /// </summary>
    /// <param name="sheet">The sheet to export; its first row must be the header.</param>
    /// <param name="connection">An open or openable connection.</param>
    /// <param name="tableName">The destination table.</param>
    /// <param name="createTable">Creates the table from the header row and the first data row.</param>
    /// <returns>How many rows were written.</returns>
    public static int Export(Worksheet sheet, DbConnection connection, string tableName,
        bool createTable = true)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        if (sheet.UsedRange is not { } used || used.RowCount < 2)
        {
            return 0;
        }

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        var headers = new List<string>();

        for (var column = used.Start.Column; column <= used.End.Column; column++)
        {
            var name = sheet[new CellReference(used.Start.Row, column)].Text;
            headers.Add(SanitizeIdentifier(name.Length == 0
                ? CellReference.ColumnIndexToName(column)
                : name));
        }

        if (createTable)
        {
            var types = headers.Select((_, i) =>
            {
                var reference = new CellReference(used.Start.Row + 1, used.Start.Column + i);
                return sheet[reference].Value.ValueType switch
                {
                    CellValueType.Number => "REAL",
                    CellValueType.Boolean => "INTEGER",
                    _ => "TEXT",
                };
            });

            using var create = connection.CreateCommand();
            create.CommandText =
                $"CREATE TABLE IF NOT EXISTS {Quote(tableName)} (" +
                string.Join(", ", headers.Zip(types, (h, t) => $"{Quote(h)} {t}")) + ")";
            create.ExecuteNonQuery();
        }

        // Every row goes through one parameterised command inside a transaction: parameters keep
        // the values out of the SQL text, and the transaction turns thousands of round trips into
        // one commit.
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;

        var parameterNames = headers.Select((_, i) => "@p" + i).ToList();

        insert.CommandText =
            $"INSERT INTO {Quote(tableName)} ({string.Join(", ", headers.Select(Quote))}) " +
            $"VALUES ({string.Join(", ", parameterNames)})";

        foreach (var name in parameterNames)
        {
            var parameter = insert.CreateParameter();
            parameter.ParameterName = name;
            insert.Parameters.Add(parameter);
        }

        var written = 0;

        for (var row = used.Start.Row + 1; row <= used.End.Row; row++)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var value = sheet[new CellReference(row, used.Start.Column + i)].Value;
                insert.Parameters[i].Value = value.AsObject() ?? DBNull.Value;
            }

            insert.ExecuteNonQuery();
            written++;
        }

        transaction.Commit();
        return written;
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string SanitizeIdentifier(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var c in name)
        {
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        var result = builder.ToString().Trim('_');

        // A column name starting with a digit is invalid in most dialects.
        return result.Length == 0 ? "column"
            : char.IsDigit(result[0]) ? "c" + result
            : result;
    }
}
