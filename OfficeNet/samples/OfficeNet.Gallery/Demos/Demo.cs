// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Reflection;
using System.Text;

namespace OfficeNet.Gallery.Demos;

/// <summary>What a demo produced.</summary>
/// <param name="Pages">Rendered page images, in order.</param>
/// <param name="Log">Anything the demo wrote as it ran.</param>
/// <param name="FileName">The document it saved, so the gallery can offer it.</param>
/// <param name="Bytes">The document itself.</param>
internal sealed record DemoResult(
    IReadOnlyList<byte[]> Pages,
    string Log,
    string FileName,
    byte[] Bytes);

/// <summary>One entry in the gallery.</summary>
/// <param name="Library">Which library it belongs to; the catalog groups by this.</param>
/// <param name="Title">A short name.</param>
/// <param name="Summary">One sentence on what it shows.</param>
/// <param name="SourceKey">The marker naming the region of source to display.</param>
/// <param name="Run">The demo itself.</param>
internal sealed record Demo(
    string Library,
    string Title,
    string Summary,
    string SourceKey,
    Func<DemoResult> Run)
{
    /// <summary>The exact source that <see cref="Run"/> executes.</summary>
    public string Source => DemoSource.For(SourceKey);
}

/// <summary>
/// Reads a demo's source out of the assembly at runtime.
/// </summary>
/// <remarks>
/// <para>
/// A gallery that shows code next to output is only useful if the two agree, and the usual way of
/// doing it — a snippet string sitting beside the delegate — drifts the first time someone renames
/// a method. Here the demo files are embedded resources and the displayed lines are read back out
/// of them, so what you see is what ran, by construction.
/// </para>
/// <para>
/// Regions are marked with <c>// &gt;&gt; key</c> and <c>// &lt;&lt;</c> comments. The markers are
/// stripped from the display along with the common indentation.
/// </para>
/// </remarks>
internal static class DemoSource
{
    private const string OpenMarker = "// >> ";
    private const string CloseMarker = "// <<";

    private static readonly Dictionary<string, string> Cache = new(StringComparer.Ordinal);
    private static readonly Lock Gate = new();

    public static string For(string key)
    {
        lock (Gate)
        {
            if (Cache.Count == 0)
            {
                LoadAll();
            }

            return Cache.GetValueOrDefault(key,
                $"(No source region marked \"{key}\". Add // >> {key} and // << around it.)");
        }
    }

    private static void LoadAll()
    {
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith("demo-source/", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name);

            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            Extract(reader.ReadToEnd());
        }
    }

    private static void Extract(string source)
    {
        string? key = null;
        var lines = new List<string>();

        foreach (var line in source.Split('\n'))
        {
            var text = line.TrimEnd('\r');
            var trimmed = text.TrimStart();

            if (trimmed.StartsWith(OpenMarker, StringComparison.Ordinal))
            {
                key = trimmed[OpenMarker.Length..].Trim();
                lines.Clear();
                continue;
            }

            if (trimmed.StartsWith(CloseMarker, StringComparison.Ordinal))
            {
                if (key is not null)
                {
                    Cache[key] = Dedent(lines);
                    key = null;
                }

                continue;
            }

            if (key is not null)
            {
                lines.Add(text);
            }
        }
    }

    /// <summary>Removes the indentation the region shares, so it reads as top-level code.</summary>
    private static string Dedent(List<string> lines)
    {
        while (lines.Count > 0 && lines[0].Trim().Length == 0)
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var indent = lines
            .Where(l => l.Trim().Length > 0)
            .Min(l => l.Length - l.TrimStart().Length);

        var builder = new StringBuilder();

        foreach (var line in lines)
        {
            builder.Append(line.Length >= indent ? line[indent..] : line.TrimStart());
            builder.Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }
}
