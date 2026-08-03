using System.ComponentModel;
using System.Globalization;
using System.Text;
using AutoWork.Core.Agents;
using AutoWork.Core.Security;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.AI;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace AutoWork.Tools;

/// <summary>
/// Extraction, profiling and cleaning for the two formats real desktop work is buried in:
/// CSV and PDF.
///
/// The profiling tool matters more than it looks. Handing a model 10,000 raw rows wastes the
/// context window and produces worse analysis than handing it column types, ranges and null
/// counts — so <c>data_profile_csv</c> is the one the agent is told to reach for first.
/// </summary>
public sealed class DataTools : ToolSetBase, IToolProvider
{
    public DataTools(ToolContext context) : base(context) { }

    protected override AgentOrgan Organ => AgentOrgan.Hands;

    public string Name => "Data";

    public IEnumerable<ToolDescriptor> GetTools(ToolContext context)
    {
        var tools = new DataTools(context);

        yield return Describe(AIFunctionFactory.Create(tools.ReadCsvAsync, "data_read_csv",
            "Read rows from a CSV file. For anything larger than a few hundred rows, call data_profile_csv first."),
            ToolRisk.Safe);

        yield return Describe(AIFunctionFactory.Create(tools.ProfileCsvAsync, "data_profile_csv",
            "Summarise a CSV: row count, column types, ranges, null counts and distinct values. Use this before analysing a large file."),
            ToolRisk.Safe);

        yield return Describe(AIFunctionFactory.Create(tools.CleanCsvAsync, "data_clean_csv",
            "Write a cleaned copy of a CSV: trim whitespace, drop blank rows, remove duplicates, drop columns."),
            ToolRisk.Write, ApprovalKind.WriteFiles);

        yield return Describe(AIFunctionFactory.Create(tools.ExtractPdfAsync, "data_extract_pdf",
            "Extract the text of a PDF, optionally a page range."), ToolRisk.Safe);

        yield return Describe(AIFunctionFactory.Create(tools.PdfInfoAsync, "data_pdf_info",
            "Page count and metadata for a PDF, without extracting all its text."), ToolRisk.Safe);
    }

    private static ToolDescriptor Describe(AIFunction function, ToolRisk risk,
        ApprovalKind approval = ApprovalKind.Other) => new()
    {
        Function = function,
        Organ = AgentOrgan.Hands,
        Risk = risk,
        Category = "Data",
        ApprovalKind = approval,
    };

    // ── CSV ───────────────────────────────────────────────────────────────────────────────

    private static CsvConfiguration ReaderConfig(string? delimiter) => new(CultureInfo.InvariantCulture)
    {
        Delimiter = string.IsNullOrEmpty(delimiter) ? "," : delimiter,
        // Real-world exports are ragged; refusing to read them helps nobody.
        MissingFieldFound = null,
        BadDataFound = null,
        HeaderValidated = null,
        TrimOptions = TrimOptions.Trim,
        DetectColumnCountChanges = false,
    };

    [Description("Read a CSV file.")]
    private Task<string> ReadCsvAsync(
        [Description("CSV file to read.")] string path,
        [Description("Maximum rows to return.")] int maxRows = 100,
        [Description("Field delimiter. Defaults to a comma.")] string? delimiter = null)
    {
        var target = Locate(path);

        return GuardedAsync("data.read_csv", $"Read {PathGuard.Describe(target)}", () =>
        {
            var canonical = Guard.EnsureReadable(target);
            if (!File.Exists(canonical)) return Failed($"{PathGuard.Describe(canonical)} does not exist.");

            var table = ReadTable(canonical, delimiter, maxRows);
            if (table.Headers.Count == 0) return Ok("The file is empty.");

            var builder = new StringBuilder();
            builder.AppendLine(string.Join(" | ", table.Headers));
            builder.AppendLine(string.Join("-+-", table.Headers.Select(h => new string('-', Math.Min(h.Length, 20)))));

            foreach (var row in table.Rows)
                builder.AppendLine(string.Join(" | ", row));

            var note = table.Truncated ? $"\n… showing the first {maxRows} rows." : "";
            return Ok(Cap(builder.ToString()) + note);
        }, [target]);
    }

    private sealed record Table(List<string> Headers, List<string[]> Rows, bool Truncated);

    private static Table ReadTable(string path, string? delimiter, int maxRows)
    {
        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, ReaderConfig(delimiter));

        if (!csv.Read() || !csv.ReadHeader())
            return new Table([], [], false);

        var headers = csv.HeaderRecord?.ToList() ?? [];
        var rows = new List<string[]>();
        var truncated = false;

        while (csv.Read())
        {
            if (rows.Count >= maxRows) { truncated = true; break; }

            var row = new string[headers.Count];
            for (var i = 0; i < headers.Count; i++)
                row[i] = csv.TryGetField<string>(i, out var value) ? value ?? "" : "";

            rows.Add(row);
        }

        return new Table(headers, rows, truncated);
    }

    [Description("Profile a CSV file.")]
    private Task<string> ProfileCsvAsync(
        [Description("CSV file to profile.")] string path,
        [Description("Field delimiter. Defaults to a comma.")] string? delimiter = null)
    {
        var target = Locate(path);

        return GuardedAsync("data.profile_csv", $"Profile {PathGuard.Describe(target)}", () =>
        {
            var canonical = Guard.EnsureReadable(target);
            if (!File.Exists(canonical)) return Failed($"{PathGuard.Describe(canonical)} does not exist.");

            using var reader = new StreamReader(canonical);
            using var csv = new CsvReader(reader, ReaderConfig(delimiter));

            if (!csv.Read() || !csv.ReadHeader()) return Ok("The file is empty.");

            var headers = csv.HeaderRecord?.ToList() ?? [];
            var columns = headers.Select(h => new ColumnProfile(h)).ToList();
            var rowCount = 0;

            while (csv.Read())
            {
                rowCount++;
                for (var i = 0; i < columns.Count; i++)
                    columns[i].Observe(csv.TryGetField<string>(i, out var value) ? value : null);
            }

            var builder = new StringBuilder()
                .AppendLine($"{PathGuard.Describe(canonical)}")
                .AppendLine($"{rowCount:N0} rows × {columns.Count} columns, {Human(new FileInfo(canonical).Length)}")
                .AppendLine();

            foreach (var column in columns)
                builder.AppendLine(column.Describe(rowCount));

            return Ok(Cap(builder.ToString()));
        }, [target]);
    }

    /// <summary>Streaming column statistics — never holds more than the distinct-value sample.</summary>
    private sealed class ColumnProfile(string name)
    {
        private const int DistinctSampleLimit = 50;

        private readonly HashSet<string> _distinct = new(StringComparer.OrdinalIgnoreCase);
        private bool _distinctOverflowed;
        private int _empty;
        private int _numeric;
        private int _dates;
        private int _total;
        private double _min = double.MaxValue;
        private double _max = double.MinValue;
        private double _sum;

        public void Observe(string? value)
        {
            _total++;

            if (string.IsNullOrWhiteSpace(value)) { _empty++; return; }

            value = value.Trim();

            if (_distinct.Count < DistinctSampleLimit) _distinct.Add(value);
            else if (!_distinct.Contains(value)) _distinctOverflowed = true;

            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            {
                _numeric++;
                _sum += number;
                _min = Math.Min(_min, number);
                _max = Math.Max(_max, number);
            }
            else if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                _dates++;
            }
        }

        public string Describe(int rowCount)
        {
            var populated = _total - _empty;
            var type = populated == 0 ? "empty"
                : _numeric >= populated * 0.9 ? "number"
                : _dates >= populated * 0.9 ? "date"
                : _distinct.Count <= 12 && !_distinctOverflowed ? "category"
                : "text";

            var builder = new StringBuilder($"  {name}  [{type}]");

            if (_empty > 0)
                builder.Append($"  {_empty:N0} blank ({(rowCount == 0 ? 0 : 100.0 * _empty / rowCount):0.#}%)");

            if (type == "number" && _numeric > 0)
                builder.Append($"  min {_min:0.##}  max {_max:0.##}  mean {_sum / _numeric:0.##}");

            if (type == "category")
                builder.Append($"  values: {string.Join(", ", _distinct.Take(12))}");
            else if (!_distinctOverflowed)
                builder.Append($"  {_distinct.Count} distinct");
            else
                builder.Append($"  {DistinctSampleLimit}+ distinct");

            return builder.ToString();
        }
    }

    [Description("Write a cleaned copy of a CSV.")]
    private Task<string> CleanCsvAsync(
        [Description("Source CSV.")] string path,
        [Description("Destination CSV.")] string outputPath,
        [Description("Remove rows that duplicate an earlier row.")] bool dropDuplicates = true,
        [Description("Remove rows where every field is blank.")] bool dropEmptyRows = true,
        [Description("Trim leading and trailing whitespace from every field.")] bool trimWhitespace = true,
        [Description("Comma-separated column names to remove.")] string? dropColumns = null,
        [Description("Field delimiter. Defaults to a comma.")] string? delimiter = null)
    {
        var source = Locate(path);
        var destination = Locate(outputPath);

        return GuardedAsync("data.clean_csv", $"Clean {PathGuard.Describe(source)}", () =>
        {
            var canonicalSource = Guard.EnsureReadable(source);
            var canonicalDestination = Guard.EnsureWritable(destination);

            if (!File.Exists(canonicalSource)) return Failed($"{PathGuard.Describe(canonicalSource)} does not exist.");

            var removeColumns = (dropColumns ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            using var reader = new StreamReader(canonicalSource);
            using var csv = new CsvReader(reader, ReaderConfig(delimiter));

            if (!csv.Read() || !csv.ReadHeader()) return Failed("The source file has no header row.");

            var headers = csv.HeaderRecord?.ToList() ?? [];
            var keptIndexes = headers.Select((h, i) => (h, i))
                .Where(x => !removeColumns.Contains(x.h))
                .Select(x => x.i)
                .ToList();

            if (keptIndexes.Count == 0) return Failed("Every column was dropped — nothing would remain.");

            Directory.CreateDirectory(Path.GetDirectoryName(canonicalDestination)!);

            using var writer = new StreamWriter(canonicalDestination);
            using var output = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = string.IsNullOrEmpty(delimiter) ? "," : delimiter,
            });

            foreach (var index in keptIndexes) output.WriteField(headers[index]);
            output.NextRecord();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int read = 0, written = 0, duplicates = 0, blanks = 0;

            while (csv.Read())
            {
                read++;

                var fields = keptIndexes
                    .Select(i => csv.TryGetField<string>(i, out var value) ? value ?? "" : "")
                    .Select(value => trimWhitespace ? value.Trim() : value)
                    .ToArray();

                if (dropEmptyRows && fields.All(string.IsNullOrWhiteSpace)) { blanks++; continue; }
                if (dropDuplicates && !seen.Add(string.Join('\u001F', fields))) { duplicates++; continue; }

                foreach (var field in fields) output.WriteField(field);
                output.NextRecord();
                written++;
            }

            var notes = new List<string>();
            if (duplicates > 0) notes.Add($"{duplicates:N0} duplicates removed");
            if (blanks > 0) notes.Add($"{blanks:N0} blank rows removed");
            if (removeColumns.Count > 0) notes.Add($"{headers.Count - keptIndexes.Count} columns dropped");

            return Ok($"Wrote {written:N0} of {read:N0} rows to {PathGuard.Describe(canonicalDestination)}." +
                      (notes.Count > 0 ? " " + string.Join("; ", notes) + "." : ""));
        },
        [source, destination], ApprovalKind.WriteFiles);
    }

    // ── PDF ───────────────────────────────────────────────────────────────────────────────

    [Description("Extract text from a PDF.")]
    private Task<string> ExtractPdfAsync(
        [Description("PDF file to read.")] string path,
        [Description("First page, 1-based. Defaults to the first page.")] int firstPage = 1,
        [Description("Last page, 1-based. Defaults to the last page.")] int lastPage = 0,
        [Description("Maximum characters to return.")] int maxCharacters = 12000)
    {
        var target = Locate(path);

        return GuardedAsync("data.extract_pdf", $"Extract text from {PathGuard.Describe(target)}", () =>
        {
            var canonical = Guard.EnsureReadable(target);
            if (!File.Exists(canonical)) return Failed($"{PathGuard.Describe(canonical)} does not exist.");

            using var pdf = PdfDocument.Open(canonical);

            var from = Math.Max(1, firstPage);
            var to = lastPage <= 0 ? pdf.NumberOfPages : Math.Min(lastPage, pdf.NumberOfPages);

            if (from > pdf.NumberOfPages)
                return Failed($"The document has {pdf.NumberOfPages} pages; page {from} does not exist.");

            var builder = new StringBuilder();

            for (var number = from; number <= to; number++)
            {
                var page = pdf.GetPage(number);
                builder.AppendLine($"── page {number} ──");
                builder.AppendLine(ContentOrderTextExtractor.GetText(page) ?? "");
                builder.AppendLine();

                if (builder.Length > maxCharacters) break;
            }

            var text = builder.ToString();

            // A PDF of scans extracts to nothing. Say so, rather than returning blank.
            if (text.Replace("── page", "").Trim().Length < 20)
                return Ok($"{PathGuard.Describe(canonical)} has {pdf.NumberOfPages} pages but almost no extractable text. " +
                          "It is probably a scan — capture it with the screen tools and read it visually instead.");

            return Ok(Cap(text, maxCharacters));
        }, [target]);
    }

    [Description("Get PDF metadata.")]
    private Task<string> PdfInfoAsync([Description("PDF file to inspect.")] string path)
    {
        var target = Locate(path);

        return GuardedAsync("data.pdf_info", $"Inspect {PathGuard.Describe(target)}", () =>
        {
            var canonical = Guard.EnsureReadable(target);
            if (!File.Exists(canonical)) return Failed($"{PathGuard.Describe(canonical)} does not exist.");

            using var pdf = PdfDocument.Open(canonical);
            var information = pdf.Information;

            var lines = new List<string>
            {
                $"{PathGuard.Describe(canonical)}",
                $"Pages: {pdf.NumberOfPages}",
                $"Size: {Human(new FileInfo(canonical).Length)}",
            };

            if (!string.IsNullOrWhiteSpace(information.Title)) lines.Add($"Title: {information.Title}");
            if (!string.IsNullOrWhiteSpace(information.Author)) lines.Add($"Author: {information.Author}");
            if (!string.IsNullOrWhiteSpace(information.Producer)) lines.Add($"Producer: {information.Producer}");

            return Ok(string.Join('\n', lines));
        }, [target]);
    }
}
