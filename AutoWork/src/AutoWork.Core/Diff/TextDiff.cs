namespace AutoWork.Core.Diff;

public enum DiffMarker
{
    Context = 0,
    Added = 1,
    Removed = 2,
    /// <summary>A run of unchanged lines that was collapsed. <c>Text</c> says how many.</summary>
    Skipped = 3,
}

public sealed record DiffLine(DiffMarker Marker, string Text);

public sealed record TextDiffResult
{
    public IReadOnlyList<DiffLine> Lines { get; init; } = [];
    public int Added { get; init; }
    public int Removed { get; init; }

    /// <summary>True when the file was too big to diff line by line, or the output was cut.</summary>
    public bool Truncated { get; init; }

    /// <summary>Set when there is something to say instead of, or as well as, the lines.</summary>
    public string? Note { get; init; }

    public bool IsEmpty => Added == 0 && Removed == 0;

    /// <summary>"+12 −3", for a heading.</summary>
    public string Summary => $"+{Added} −{Removed}";
}

/// <summary>
/// A line diff, for showing what a write would change before it happens.
///
/// This is for a person to look at, not for patching, so it optimises for being readable and
/// cheap rather than for producing a minimal edit script. Common head and tail are trimmed first
/// — which is most of the work for a typical edit — and only the disputed middle goes through
/// the quadratic part, with a ceiling on it.
/// </summary>
public static class TextDiff
{
    /// <summary>Beyond this, the middle is reported as a wholesale replacement instead.</summary>
    private const int MaxMiddleLines = 1_500;

    /// <summary>Unchanged lines kept either side of a change.</summary>
    private const int ContextLines = 3;

    /// <summary>A hard ceiling on what is handed to the UI, so a consent card cannot become a novel.</summary>
    private const int MaxOutputLines = 200;

    public static TextDiffResult Compare(string? before, string? after)
    {
        before ??= "";
        after ??= "";

        if (string.Equals(before, after, StringComparison.Ordinal))
            return new TextDiffResult { Note = "No change — the file already has these contents." };

        var oldLines = SplitLines(before);
        var newLines = SplitLines(after);

        // Common head and tail. A one-line edit in a thousand-line file reduces to one line here.
        var head = 0;
        while (head < oldLines.Length && head < newLines.Length
               && string.Equals(oldLines[head], newLines[head], StringComparison.Ordinal))
            head++;

        var tail = 0;
        while (tail < oldLines.Length - head && tail < newLines.Length - head
               && string.Equals(oldLines[^(tail + 1)], newLines[^(tail + 1)], StringComparison.Ordinal))
            tail++;

        var oldMiddle = oldLines[head..(oldLines.Length - tail)];
        var newMiddle = newLines[head..(newLines.Length - tail)];

        if (oldMiddle.Length > MaxMiddleLines || newMiddle.Length > MaxMiddleLines)
        {
            return new TextDiffResult
            {
                Added = newMiddle.Length,
                Removed = oldMiddle.Length,
                Truncated = true,
                Note = $"Too large to show line by line: {oldMiddle.Length:N0} lines would be replaced by {newMiddle.Length:N0}.",
            };
        }

        var script = BuildScript(oldMiddle, newMiddle);

        var added = script.Count(l => l.Marker == DiffMarker.Added);
        var removed = script.Count(l => l.Marker == DiffMarker.Removed);

        // Re-attach the trimmed head and tail as context so the change is not floating free.
        var withContext = new List<DiffLine>();

        foreach (var line in oldLines[Math.Max(0, head - ContextLines)..head])
            withContext.Add(new DiffLine(DiffMarker.Context, line));

        withContext.AddRange(script);

        var tailStart = oldLines.Length - tail;
        foreach (var line in oldLines[tailStart..Math.Min(oldLines.Length, tailStart + ContextLines)])
            withContext.Add(new DiffLine(DiffMarker.Context, line));

        var collapsed = Collapse(withContext);
        var truncated = false;

        if (collapsed.Count > MaxOutputLines)
        {
            collapsed = [.. collapsed.Take(MaxOutputLines),
                new DiffLine(DiffMarker.Skipped, $"… {collapsed.Count - MaxOutputLines:N0} more lines not shown")];
            truncated = true;
        }

        return new TextDiffResult { Lines = collapsed, Added = added, Removed = removed, Truncated = truncated };
    }

    /// <summary>
    /// Longest common subsequence over the disputed middle. Bounded by
    /// <see cref="MaxMiddleLines"/>, which is what keeps the table a few megabytes at worst.
    /// </summary>
    private static List<DiffLine> BuildScript(string[] oldLines, string[] newLines)
    {
        var rows = oldLines.Length + 1;
        var columns = newLines.Length + 1;
        var table = new int[rows, columns];

        for (var i = oldLines.Length - 1; i >= 0; i--)
        {
            for (var j = newLines.Length - 1; j >= 0; j--)
            {
                table[i, j] = string.Equals(oldLines[i], newLines[j], StringComparison.Ordinal)
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var script = new List<DiffLine>();
        var x = 0;
        var y = 0;

        while (x < oldLines.Length && y < newLines.Length)
        {
            if (string.Equals(oldLines[x], newLines[y], StringComparison.Ordinal))
            {
                script.Add(new DiffLine(DiffMarker.Context, oldLines[x]));
                x++;
                y++;
            }
            else if (table[x + 1, y] >= table[x, y + 1])
            {
                script.Add(new DiffLine(DiffMarker.Removed, oldLines[x]));
                x++;
            }
            else
            {
                script.Add(new DiffLine(DiffMarker.Added, newLines[y]));
                y++;
            }
        }

        while (x < oldLines.Length) script.Add(new DiffLine(DiffMarker.Removed, oldLines[x++]));
        while (y < newLines.Length) script.Add(new DiffLine(DiffMarker.Added, newLines[y++]));

        return script;
    }

    /// <summary>Replaces long runs of unchanged lines with a single "n unchanged lines" marker.</summary>
    private static List<DiffLine> Collapse(List<DiffLine> lines)
    {
        var output = new List<DiffLine>();
        var index = 0;

        while (index < lines.Count)
        {
            if (lines[index].Marker != DiffMarker.Context)
            {
                output.Add(lines[index++]);
                continue;
            }

            var run = index;
            while (run < lines.Count && lines[run].Marker == DiffMarker.Context) run++;

            var length = run - index;

            if (length <= ContextLines * 2)
            {
                for (var i = index; i < run; i++) output.Add(lines[i]);
            }
            else
            {
                // Keep the edges of the run — they are the context for the changes either side.
                var lead = index == 0 ? 0 : ContextLines;
                var trail = run == lines.Count ? 0 : ContextLines;

                for (var i = index; i < index + lead; i++) output.Add(lines[i]);

                output.Add(new DiffLine(DiffMarker.Skipped, $"{length - lead - trail:N0} unchanged lines"));

                for (var i = run - trail; i < run; i++) output.Add(lines[i]);
            }

            index = run;
        }

        return output;
    }

    private static string[] SplitLines(string text) =>
        text.Length == 0 ? [] : text.ReplaceLineEndings("\n").Split('\n');
}
