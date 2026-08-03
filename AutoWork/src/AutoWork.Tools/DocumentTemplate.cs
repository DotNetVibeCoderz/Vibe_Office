using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AutoWork.Tools;

public sealed record TemplateFill
{
    /// <summary>The filled text, for text templates. Null for a .docx, which is edited in place.</summary>
    public string? Text { get; init; }

    public int Replaced { get; init; }

    /// <summary>Placeholders still in the document because no value was supplied for them.</summary>
    public IReadOnlyList<string> Unfilled { get; init; } = [];

    /// <summary>Values supplied that matched no placeholder — usually a typo in one or the other.</summary>
    public IReadOnlyList<string> Unused { get; init; } = [];
}

/// <summary>
/// Fills <c>{{placeholder}}</c> markers in a template.
///
/// For plain text this is a regular expression. For Word it is not, and the reason is the thing
/// worth knowing: Word splits a paragraph into <em>runs</em> whenever anything about the text
/// changes — a spell-check mark, an edit, a language tag — so <c>{{name}}</c> typed as one word
/// is routinely stored as <c>{{na</c> + <c>me}}</c> across two runs. A naive search of each run's
/// text finds nothing and reports a template with no placeholders in it, which is exactly what a
/// user would call broken.
///
/// So each paragraph is flattened to a string, substituted, and written back into its first run
/// with the rest emptied. Formatting that varied <em>within</em> a replaced span is lost; the
/// formatting of the paragraph's first run wins. That is a deliberate trade — a correct fill in
/// the paragraph's own style beats a faithful one that silently misses half the placeholders.
/// </summary>
public static class DocumentTemplate
{
    /// <summary>Matches {{ name }} with optional spacing. Names are letters, digits, _ . - only.</summary>
    private static readonly Regex Placeholder =
        new(@"\{\{\s*([A-Za-z0-9_.\-]+)\s*\}\}", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

    public static TemplateFill FillText(string template, IReadOnlyDictionary<string, string> values)
    {
        var replaced = 0;
        var unfilled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var output = Substitute(template, values, ref replaced, unfilled, used);

        return new TemplateFill
        {
            Text = output,
            Replaced = replaced,
            Unfilled = [.. unfilled.OrderBy(u => u, StringComparer.OrdinalIgnoreCase)],
            Unused = Unused(values, used),
        };
    }

    public static TemplateFill FillWord(string path, IReadOnlyDictionary<string, string> values)
    {
        var replaced = 0;
        var unfilled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var document = WordprocessingDocument.Open(path, isEditable: true))
        {
            var main = document.MainDocumentPart;
            if (main?.Document?.Body is not null)
                FillPart(main.Document.Body, values, ref replaced, unfilled, used);

            // Headers and footers carry the placeholders people most expect to work — a company
            // name, a document reference, a page footer with the client's name in it.
            foreach (var header in main?.HeaderParts ?? [])
                if (header.Header is not null)
                    FillPart(header.Header, values, ref replaced, unfilled, used);

            foreach (var footer in main?.FooterParts ?? [])
                if (footer.Footer is not null)
                    FillPart(footer.Footer, values, ref replaced, unfilled, used);

            main?.Document?.Save();
        }

        return new TemplateFill
        {
            Replaced = replaced,
            Unfilled = [.. unfilled.OrderBy(u => u, StringComparer.OrdinalIgnoreCase)],
            Unused = Unused(values, used),
        };
    }

    /// <summary>Body, header and footer share no closer base type than this.</summary>
    private static void FillPart(
        DocumentFormat.OpenXml.OpenXmlElement part,
        IReadOnlyDictionary<string, string> values,
        ref int replaced,
        HashSet<string> unfilled,
        HashSet<string> used)
    {
        foreach (var paragraph in part.Descendants<Paragraph>())
        {
            var runs = paragraph.Descendants<Run>().Where(r => r.GetFirstChild<Text>() is not null).ToList();
            if (runs.Count == 0) continue;

            var original = string.Concat(runs.Select(r => r.GetFirstChild<Text>()!.Text));
            if (!original.Contains("{{", StringComparison.Ordinal)) continue;

            var filled = Substitute(original, values, ref replaced, unfilled, used);
            if (string.Equals(filled, original, StringComparison.Ordinal)) continue;

            // Everything lands in the first run; the rest are emptied rather than removed, so
            // any bookmarks or comment anchors hanging off them survive.
            runs[0].GetFirstChild<Text>()!.Text = filled;
            runs[0].GetFirstChild<Text>()!.Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve;

            for (var i = 1; i < runs.Count; i++)
                runs[i].GetFirstChild<Text>()!.Text = "";
        }
    }

    private static string Substitute(
        string input,
        IReadOnlyDictionary<string, string> values,
        ref int replaced,
        HashSet<string> unfilled,
        HashSet<string> used)
    {
        var count = 0;
        var localUnfilled = unfilled;
        var localUsed = used;

        var output = Placeholder.Replace(input, match =>
        {
            var name = match.Groups[1].Value;

            if (values.TryGetValue(name, out var value))
            {
                localUsed.Add(name);
                count++;
                return value;
            }

            // Left exactly as it was, so a second pass with the missing value still works.
            localUnfilled.Add(name);
            return match.Value;
        });

        replaced += count;
        return output;
    }

    private static IReadOnlyList<string> Unused(IReadOnlyDictionary<string, string> values, HashSet<string> used) =>
        [.. values.Keys.Where(k => !used.Contains(k)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase)];
}
