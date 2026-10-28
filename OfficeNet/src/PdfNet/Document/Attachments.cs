// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Security.Cryptography;
using PdfNet.Objects;

namespace PdfNet.Document;

/// <summary>A file embedded in a PDF.</summary>
/// <param name="Name">The file name, as shown in a viewer's attachments panel.</param>
/// <param name="Data">The file's bytes.</param>
/// <param name="MimeType">Its media type, e.g. <c>application/xml</c>, if recorded.</param>
/// <param name="Description">A description, if any.</param>
/// <param name="Relationship">How it relates to the document (PDF/A-3): <c>Source</c>, <c>Data</c>, <c>Alternative</c>, <c>Supplement</c> or <c>Unspecified</c>.</param>
/// <param name="Modified">When it was last modified, if recorded.</param>
public sealed record PdfAttachment(string Name, byte[] Data, string? MimeType, string? Description,
    string? Relationship, DateTimeOffset? Modified);

public sealed partial class PdfDocument
{
    /// <summary>
    /// The files embedded in the document: those in the catalog's <c>/EmbeddedFiles</c> name tree,
    /// and those attached to pages by file-attachment annotations.
    /// </summary>
    /// <remarks>
    /// An e-invoice is the common case: a ZUGFeRD or Factur-X PDF carries its machine-readable XML
    /// as an attachment. The name tree can be split into <c>/Kids</c> nodes, as a large one is, and
    /// is walked whole.
    /// </remarks>
    public IReadOnlyList<PdfAttachment> Attachments
    {
        get
        {
            var result = new List<PdfAttachment>();

            if (EmbeddedFilesTree() is { } tree)
            {
                foreach (var (name, spec) in NameTreeEntries(tree, 0))
                {
                    if (ReadFileSpec(spec, name) is { } attachment)
                    {
                        result.Add(attachment);
                    }
                }
            }

            foreach (var page in Pages)
            {
                foreach (var annotation in page.Annotations.Select(Follow).OfType<PdfDictionary>())
                {
                    if ((annotation[PdfName.Subtype] as PdfName)?.Value == "FileAttachment" &&
                        Follow(annotation[PdfName.Get("FS")]) is PdfDictionary spec &&
                        ReadFileSpec(spec, null) is { } attachment)
                    {
                        result.Add(attachment);
                    }
                }
            }

            return result;
        }
    }

    /// <summary>Embeds a file, replacing one already embedded under the same name.</summary>
    /// <param name="name">The file name a viewer shows.</param>
    /// <param name="data">The file's bytes.</param>
    /// <param name="mimeType">Its media type; PDF/A-3 requires one.</param>
    /// <param name="description">Shown beside it in a viewer.</param>
    /// <param name="relationship">
    /// For PDF/A-3: <c>Source</c>, <c>Data</c>, <c>Alternative</c>, <c>Supplement</c> or
    /// <c>Unspecified</c>. An e-invoice's XML is <c>Alternative</c> or <c>Data</c>.
    /// </param>
    public void AddAttachment(string name, byte[] data, string? mimeType = null, string? description = null,
        string relationship = "Unspecified")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(data);

        RemoveAttachment(name);

        var stream = new PdfStream();
        stream[PdfName.Type] = PdfName.Get("EmbeddedFile");

        if (mimeType is not null)
        {
            // A name, so the slash is written #2F — PdfName escapes it.
            stream[PdfName.Subtype] = PdfName.Get(mimeType);
        }

        var parameters = new PdfDictionary();
        parameters[PdfName.Get("Size")] = new PdfNumber(data.LongLength);
        parameters[PdfName.Get("ModDate")] = new PdfString(PdfDate(DateTimeOffset.Now));
        parameters[PdfName.Get("CheckSum")] = new PdfString(MD5.HashData(data)) { IsHex = true };
        stream[PdfName.Get("Params")] = parameters;
        stream.SetDecoded(data);

        var files = new PdfDictionary();
        files[PdfName.Get("F")] = AddObject(stream);

        var spec = new PdfDictionary();
        spec[PdfName.Type] = PdfName.Get("Filespec");
        spec[PdfName.Get("F")] = new PdfString(name);
        spec[PdfName.Get("UF")] = new PdfString(name);
        spec[PdfName.Get("EF")] = files;
        spec[PdfName.Get("AFRelationship")] = PdfName.Get(relationship);

        if (description is not null)
        {
            spec[PdfName.Get("Desc")] = new PdfString(description);
        }

        var specReference = AddObject(spec);

        // The name tree's /Names must stay sorted by key; a viewer may binary-search it.
        var tree = EnsureEmbeddedFilesTree();
        var names = tree[PdfName.Get("Names")] as PdfArray ?? [];
        var entries = Pairs(names).Append((name, (PdfObject)specReference))
            .OrderBy(e => e.Item1, StringComparer.Ordinal)
            .ToList();

        var sorted = new PdfArray();

        foreach (var (key, value) in entries)
        {
            sorted.Add(new PdfString(key));
            sorted.Add(value);
        }

        tree[PdfName.Get("Names")] = sorted;

        // PDF/A-3 also wants every associated file listed in the catalog's /AF.
        var associated = Catalog[PdfName.Get("AF")] as PdfArray ?? [];
        associated.Add(specReference);
        Catalog[PdfName.Get("AF")] = associated;
    }

    /// <summary>Removes an embedded file by name. Returns false when there was none.</summary>
    public bool RemoveAttachment(string name)
    {
        if (EmbeddedFilesTree() is not { } tree || tree[PdfName.Get("Names")] is not PdfArray names)
        {
            return false;
        }

        var kept = new PdfArray();
        var removed = false;

        foreach (var (key, value) in Pairs(names))
        {
            if (key == name)
            {
                removed = true;

                if (Catalog[PdfName.Get("AF")] is PdfArray associated)
                {
                    var reference = value as PdfReference;
                    var remaining = new PdfArray([.. associated.Where(a => !(a is PdfReference r && reference is not null && r.Number == reference.Number))]);
                    Catalog[PdfName.Get("AF")] = remaining;
                }

                continue;
            }

            kept.Add(new PdfString(key));
            kept.Add(value);
        }

        tree[PdfName.Get("Names")] = kept;
        return removed;
    }

    // ---- Name tree -----------------------------------------------------------------------------

    private PdfDictionary? EmbeddedFilesTree() =>
        Follow(Catalog[PdfName.Get("Names")]) is PdfDictionary names
            ? Follow(names[PdfName.Get("EmbeddedFiles")]) as PdfDictionary
            : null;

    private PdfDictionary EnsureEmbeddedFilesTree()
    {
        if (Follow(Catalog[PdfName.Get("Names")]) is not PdfDictionary names)
        {
            names = new PdfDictionary();
            Catalog[PdfName.Get("Names")] = names;
        }

        if (Follow(names[PdfName.Get("EmbeddedFiles")]) is not PdfDictionary tree)
        {
            tree = new PdfDictionary();
            names[PdfName.Get("EmbeddedFiles")] = AddObject(tree);
        }

        return tree;
    }

    /// <summary>Every (key, value) leaf of a name tree, depth-first, guarded against loops.</summary>
    private IEnumerable<(string Key, PdfDictionary Value)> NameTreeEntries(PdfDictionary node, int depth)
    {
        if (depth > 32)
        {
            yield break;
        }

        if (node[PdfName.Get("Names")] is PdfArray names)
        {
            foreach (var (key, value) in Pairs(names))
            {
                if (Follow(value) is PdfDictionary dictionary)
                {
                    yield return (key, dictionary);
                }
            }
        }

        if (Follow(node[PdfName.Get("Kids")]) is PdfArray kids)
        {
            foreach (var kid in kids.Select(Follow).OfType<PdfDictionary>())
            {
                foreach (var entry in NameTreeEntries(kid, depth + 1))
                {
                    yield return entry;
                }
            }
        }
    }

    private IEnumerable<(string Key, PdfObject Value)> Pairs(PdfArray names)
    {
        for (var i = 0; i + 1 < names.Count; i += 2)
        {
            if (Follow(names[i]) is PdfString key)
            {
                yield return (key.AsText(), names[i + 1]);
            }
        }
    }

    private PdfAttachment? ReadFileSpec(PdfDictionary spec, string? key)
    {
        if (Follow(spec[PdfName.Get("EF")]) is not PdfDictionary files ||
            Follow(files[PdfName.Get("UF")] ?? files[PdfName.Get("F")]) is not PdfStream stream)
        {
            return null;
        }

        var name = (Follow(spec[PdfName.Get("UF")]) as PdfString)?.AsText()
                   ?? (Follow(spec[PdfName.Get("F")]) as PdfString)?.AsText()
                   ?? key
                   ?? "attachment";

        var modified = Follow(stream[PdfName.Get("Params")]) is PdfDictionary parameters &&
                       Follow(parameters[PdfName.Get("ModDate")]) is PdfString date
            ? ParsePdfDate(date.AsText())
            : null;

        return new PdfAttachment(name, stream.Decoded, (stream[PdfName.Subtype] as PdfName)?.Value,
            (Follow(spec[PdfName.Get("Desc")]) as PdfString)?.AsText(),
            (spec[PdfName.Get("AFRelationship")] as PdfName)?.Value, modified);
    }

    // ---- Dates ---------------------------------------------------------------------------------

    /// <summary>Formats a date in the PDF form: <c>D:20261028153000+07'00'</c>.</summary>
    public static string PdfDate(DateTimeOffset value)
    {
        var offset = value.Offset;
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        offset = offset.Duration();

        return string.Create(CultureInfo.InvariantCulture,
            $"D:{value:yyyyMMddHHmmss}{sign}{offset.Hours:00}'{offset.Minutes:00}'");
    }

    /// <summary>Parses a PDF date, <c>D:YYYYMMDDHHmmSS</c> with an optional offset or <c>Z</c>.</summary>
    public static DateTimeOffset? ParsePdfDate(string text)
    {
        var s = text.StartsWith("D:", StringComparison.Ordinal) ? text[2..] : text;

        if (s.Length < 14 ||
            !DateTime.TryParseExact(s[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        var offset = TimeSpan.Zero;
        var zone = s[14..].Replace("'", "", StringComparison.Ordinal);

        if (zone.Length >= 5 && zone[0] is '+' or '-' &&
            int.TryParse(zone.AsSpan(1, 2), CultureInfo.InvariantCulture, out var hours) &&
            int.TryParse(zone.AsSpan(3, 2), CultureInfo.InvariantCulture, out var minutes))
        {
            offset = new TimeSpan(hours, minutes, 0) * (zone[0] == '-' ? -1 : 1);
        }

        return new DateTimeOffset(local, offset);
    }
}
