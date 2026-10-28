// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml;
using OfficeNet.Core.Xml;

namespace ExcelNet;

/// <summary>
/// The workbook's shared string table.
/// </summary>
/// <remarks>
/// <para>
/// A spreadsheet stores text once and refers to it by index from every cell that shows it. On a
/// sheet where a status column holds "Selesai" ten thousand times, that is one string in the file
/// rather than ten thousand — which is most of why an .xlsx of a large export is small.
/// </para>
/// <para>
/// The table is append-only within a session. Removing an entry would renumber every index after
/// it, and the indices live in cells across every sheet; unused strings are cheap and a stale one
/// is invisible.
/// </para>
/// </remarks>
public sealed class SharedStrings
{
    private readonly List<string> _strings = [];
    private readonly List<IReadOnlyList<RichTextRun>?> _runs = [];
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    /// <summary>How many distinct strings the table holds.</summary>
    public int Count => _strings.Count;

    /// <summary>
    /// The total number of cell references to the table, which Excel writes as <c>count</c>.
    /// </summary>
    public int TotalReferences { get; private set; }

    /// <summary>Returns the index of a string, adding it when it is new.</summary>
    public int Add(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        TotalReferences++;

        if (_index.TryGetValue(value, out var existing))
        {
            return existing;
        }

        var index = _strings.Count;
        _strings.Add(value);
        _runs.Add(null);
        _index[value] = index;
        return index;
    }

    /// <summary>Returns the index of a rich-text string, adding it when it is new.</summary>
    /// <remarks>
    /// Keyed by the runs as well as the text: "LUNAS" in bold and "LUNAS" plain are two entries,
    /// and a rich key starts with NUL, which no cell text read from or written to XML can contain.
    /// </remarks>
    public int AddRich(IReadOnlyList<RichTextRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        TotalReferences++;

        var key = "\u0000" + string.Join("\u0002", runs.Select(r => r.Key()));

        if (_index.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var index = _strings.Count;
        _strings.Add(string.Concat(runs.Select(r => r.Text)));
        _runs.Add(runs);
        _index[key] = index;
        return index;
    }

    /// <summary>The runs of a rich-text entry, or <c>null</c> for plain text.</summary>
    public IReadOnlyList<RichTextRun>? GetRuns(int index) =>
        index >= 0 && index < _runs.Count ? _runs[index] : null;

    /// <summary>
    /// The string at an index, or the empty string when the index is out of range.
    /// </summary>
    /// <remarks>
    /// A missing index means the file is inconsistent. Returning empty rather than throwing keeps
    /// one bad cell from making a whole workbook unreadable, which is the trade a reader should
    /// make.
    /// </remarks>
    public string Get(int index) =>
        index >= 0 && index < _strings.Count ? _strings[index] : string.Empty;

    /// <summary>Resets the reference count before a save recounts it.</summary>
    internal void ResetReferenceCount() => TotalReferences = 0;

    /// <summary>Reads an <c>xl/sharedStrings.xml</c> part.</summary>
    public static SharedStrings Read(byte[] data)
    {
        var table = new SharedStrings();

        if (data.Length == 0)
        {
            return table;
        }

        using var stream = new MemoryStream(data, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            IgnoreWhitespace = false,
            IgnoreComments = true,
            DtdProcessing = DtdProcessing.Prohibit,
        });

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "si")
            {
                continue;
            }

            var (text, runs) = ReadStringItem(reader);
            table.AddRaw(text, runs);
        }

        return table;
    }

    private void AddRaw(string value, IReadOnlyList<RichTextRun>? runs)
    {
        var index = _strings.Count;
        _strings.Add(value);
        _runs.Add(runs);
        // Only the first occurrence is indexed; a file may legally contain duplicate entries and
        // cells point at specific indices, so the later ones must keep their positions.
        _index.TryAdd(runs is null ? value : "\u0000" + string.Join("\u0002", runs.Select(r => r.Key())), index);
    }

    /// <summary>
    /// Reads one <c>si</c> element: plain text, or the runs of a rich-text string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cell whose text is partly bold is stored as several <c>r</c> runs, each with its own
    /// <c>t</c>. Reading only the first <c>t</c> — the obvious implementation — silently truncates
    /// every such string to its first formatting change. Reading only the text, as this table did
    /// until the runs were kept, made every open-and-save strip the formatting inside cells.
    /// </para>
    /// <para>
    /// Only an <c>r</c> is materialised as an element; plain entries stay streamed, so a table of
    /// a million plain strings costs what it did. A phonetic reading (<c>rPh</c>) is not text and
    /// is skipped.
    /// </para>
    /// </remarks>
    private static (string Text, IReadOnlyList<RichTextRun>? Runs) ReadStringItem(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return (string.Empty, null);
        }

        var builder = new System.Text.StringBuilder();
        List<RichTextRun>? runs = null;
        var depth = reader.Depth;

        reader.Read();

        while (!reader.EOF && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Depth == depth + 1)
            {
                switch (reader.LocalName)
                {
                    case "t":
                        // Leaves the reader on the node after </t>, so no Read() follows.
                        builder.Append(reader.ReadElementContentAsString());
                        continue;

                    case "r":
                        var run = RichTextRun.Read((System.Xml.Linq.XElement)System.Xml.Linq.XNode.ReadFrom(reader));
                        (runs ??= []).Add(run);
                        builder.Append(run.Text);
                        continue;

                    default:
                        reader.Skip();
                        continue;
                }
            }

            reader.Read();
        }

        return (builder.ToString(), runs);
    }

    /// <summary>Serialises the table as an <c>xl/sharedStrings.xml</c> part.</summary>
    public byte[] Write()
    {
        using var buffer = new MemoryStream(Math.Max(1024, _strings.Count * 32));

        var settings = new XmlWriterSettings
        {
            Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            Indent = false,
            OmitXmlDeclaration = false,
            CloseOutput = false,
        };

        using (var writer = XmlWriter.Create(buffer, settings))
        {
            writer.WriteStartDocument(standalone: true);
            writer.WriteStartElement("sst", Ns.S.NamespaceName);
            writer.WriteAttributeString("count", TotalReferences.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("uniqueCount", _strings.Count.ToString(CultureInfo.InvariantCulture));

            for (var i = 0; i < _strings.Count; i++)
            {
                var value = _strings[i];
                writer.WriteStartElement("si", Ns.S.NamespaceName);

                if (_runs[i] is { } runs)
                {
                    foreach (var run in runs)
                    {
                        run.Write(writer);
                    }

                    writer.WriteEndElement();
                    continue;
                }

                writer.WriteStartElement("t", Ns.S.NamespaceName);

                // Leading or trailing whitespace is lost on the round trip without this, exactly
                // as it is in WordprocessingML's w:t.
                if (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
                {
                    writer.WriteAttributeString("space", System.Xml.Linq.XNamespace.Xml.NamespaceName,
                        "preserve");
                }

                writer.WriteString(Sanitize(value));
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Strips the control characters XML 1.0 cannot represent.
    /// </summary>
    /// <remarks>
    /// A string arriving from a database or a CSV can contain a NUL or a form feed. XML 1.0 has no
    /// escape for those, so writing one produces a file that no reader — including this one — can
    /// parse. Dropping them is the only option that keeps the export usable.
    /// </remarks>
    internal static string Sanitize(string value)
    {
        var needsWork = false;

        foreach (var c in value)
        {
            if (IsForbidden(c))
            {
                needsWork = true;
                break;
            }
        }

        if (!needsWork)
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length);

        foreach (var c in value)
        {
            if (!IsForbidden(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();

        static bool IsForbidden(char c) =>
            c < 0x20 && c is not ('\t' or '\n' or '\r') || c is '￾' or '￿';
    }
}
