// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.RegularExpressions;

namespace OfficeNet.Core.Markdown;

/// <summary>
/// GitHub-flavoured tables, recognised in a paragraph's lines when the paragraph is closed.
/// </summary>
/// <remarks>
/// A table is a header row, a delimiter row of dashes with the same number of cells, and body rows
/// up to the end of the paragraph — which already ends at a blank line or a new block, exactly the
/// GFM rule for where a table ends. Lines before the header stay a paragraph.
/// </remarks>
internal static partial class TableParser
{
    public static void TryConvert(MarkdownNode paragraph)
    {
        var lines = paragraph.Content.ToString().TrimEnd('\n').Split('\n');

        for (var k = 1; k < lines.Length; k++)
        {
            if (!lines[k].Contains('|', StringComparison.Ordinal) &&
                !lines[k - 1].Contains('|', StringComparison.Ordinal))
            {
                continue;
            }

            if (Alignments(lines[k]) is not { } alignments ||
                SplitRow(lines[k - 1]).Count != alignments.Count)
            {
                continue;
            }

            var table = new MarkdownNode(MarkdownNodeType.Table) { StartLine = paragraph.StartLine };
            table.AppendChild(Row(lines[k - 1], alignments, header: true));

            for (var i = k + 1; i < lines.Length; i++)
            {
                table.AppendChild(Row(lines[i], alignments, header: false));
            }

            paragraph.InsertAfter(table);

            if (k == 1)
            {
                paragraph.Unlink();
            }
            else
            {
                paragraph.Content.Clear().Append(string.Join('\n', lines[..(k - 1)])).Append('\n');
            }

            return;
        }
    }

    private static MarkdownNode Row(string line, List<string?> alignments, bool header)
    {
        var row = new MarkdownNode(MarkdownNodeType.TableRow);
        var cells = SplitRow(line);

        // Every row has the header's cell count: missing cells are empty, extra ones are dropped.
        for (var i = 0; i < alignments.Count; i++)
        {
            var cell = new MarkdownNode(MarkdownNodeType.TableCell) { Align = alignments[i], IsHeader = header };
            cell.Content.Append(i < cells.Count ? cells[i] : string.Empty);
            row.AppendChild(cell);
        }

        return row;
    }

    private static List<string?>? Alignments(string line)
    {
        var cells = SplitRow(line);

        if (cells.Count == 0 || !cells.All(c => DelimiterCell().IsMatch(c)))
        {
            return null;
        }

        return
        [
            .. cells.Select(c => (c.StartsWith(':'), c.EndsWith(':')) switch
            {
                (true, true) => "center",
                (true, false) => "left",
                (false, true) => "right",
                _ => (string?)null,
            }),
        ];
    }

    private static List<string> SplitRow(string line)
    {
        var text = line.Trim();

        if (text.StartsWith('|'))
        {
            text = text[1..];
        }

        if (text.EndsWith('|') && !text.EndsWith("\\|", StringComparison.Ordinal))
        {
            text = text[..^1];
        }

        var cells = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == '|')
            {
                cells.Add(Cell(text[start..i]));
                start = i + 1;
            }
        }

        cells.Add(Cell(text[start..]));
        return cells;

        // An escaped pipe is part of the cell, including inside a code span.
        static string Cell(string raw) => raw.Trim().Replace("\\|", "|", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^:?-+:?$")]
    private static partial Regex DelimiterCell();
}
