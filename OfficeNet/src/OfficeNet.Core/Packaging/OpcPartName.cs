// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics.CodeAnalysis;

namespace OfficeNet.Core.Packaging;

/// <summary>
/// A validated OPC part name: an absolute, forward-slashed, lower-cased-extension path such as
/// <c>/word/document.xml</c>.
/// </summary>
/// <remarks>
/// Part names are compared case-insensitively by the OPC specification (ECMA-376 Part 2 §9.1.1.5)
/// but stored case-preserved, because relationship targets in the wild are written with the case
/// the producer used and some consumers echo it back. Making this a struct with its own equality
/// is what stops <c>/word/Document.xml</c> and <c>/word/document.xml</c> becoming two parts in the
/// same package — a package that opens in this library and is rejected by Word.
/// </remarks>
public readonly struct OpcPartName : IEquatable<OpcPartName>
{
    /// <summary>The absolute part name, always beginning with <c>/</c>.</summary>
    public string Value { get; }

    /// <summary>Creates a part name, normalising a leading slash and back-slashes.</summary>
    /// <exception cref="ArgumentException">The name is empty, relative, or ends with a slash.</exception>
    public OpcPartName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Replace('\\', '/');
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        if (normalized.EndsWith('/'))
        {
            throw new ArgumentException($"A part name may not end with '/': '{value}'.", nameof(value));
        }

        if (normalized.Contains("//", StringComparison.Ordinal))
        {
            throw new ArgumentException($"A part name may not contain an empty segment: '{value}'.", nameof(value));
        }

        Value = normalized;
    }

    /// <summary>The part name without its leading slash — the entry name inside the zip.</summary>
    public string ZipEntryName => Value[1..];

    /// <summary>The extension without the dot, lower-cased; empty when there is none.</summary>
    public string Extension
    {
        get
        {
            var lastSlash = Value.LastIndexOf('/');
            var lastDot = Value.LastIndexOf('.');
            return lastDot > lastSlash && lastDot < Value.Length - 1
                ? Value[(lastDot + 1)..].ToLowerInvariant()
                : string.Empty;
        }
    }

    /// <summary>The final segment of the part name, for example <c>document.xml</c>.</summary>
    public string FileName => Value[(Value.LastIndexOf('/') + 1)..];

    /// <summary>The directory portion, for example <c>/word</c>; <c>""</c> at the package root.</summary>
    public string Directory
    {
        get
        {
            var lastSlash = Value.LastIndexOf('/');
            return lastSlash <= 0 ? string.Empty : Value[..lastSlash];
        }
    }

    /// <summary>
    /// The part holding this part's relationships, for example
    /// <c>/word/_rels/document.xml.rels</c>. The package root's relationships live at
    /// <c>/_rels/.rels</c>, which is why the empty directory is special-cased.
    /// </summary>
    public OpcPartName RelationshipPartName =>
        new($"{Directory}/_rels/{FileName}.rels");

    /// <summary>True when this part is itself a relationship part.</summary>
    public bool IsRelationshipPart =>
        Value.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) &&
        Value.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a relationship target against this part's directory. Absolute targets (starting
    /// with <c>/</c>) are taken as-is; relative ones are combined and <c>..</c> segments collapsed.
    /// </summary>
    public OpcPartName Resolve(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        // A URI fragment or query has no meaning for a part name.
        var cut = target.IndexOfAny(['#', '?']);
        if (cut >= 0)
        {
            target = target[..cut];
        }

        target = Uri.UnescapeDataString(target.Replace('\\', '/'));

        if (target.StartsWith('/'))
        {
            return new OpcPartName(target);
        }

        var segments = new List<string>(Directory.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var segment in target.Split('/'))
        {
            switch (segment)
            {
                case "" or ".":
                    break;
                case "..":
                    if (segments.Count > 0)
                    {
                        segments.RemoveAt(segments.Count - 1);
                    }
                    break;
                default:
                    segments.Add(segment);
                    break;
            }
        }

        return new OpcPartName("/" + string.Join('/', segments));
    }

    /// <summary>
    /// Expresses <paramref name="other"/> relative to this part's directory, which is the form
    /// relationship targets take inside a package (<c>media/image1.png</c>, <c>../styles.xml</c>).
    /// </summary>
    public string RelativeTo(OpcPartName other)
    {
        var from = Directory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var to = other.Value.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var common = 0;
        while (common < from.Length && common < to.Length - 1 &&
               string.Equals(from[common], to[common], StringComparison.OrdinalIgnoreCase))
        {
            common++;
        }

        var up = string.Concat(Enumerable.Repeat("../", from.Length - common));
        return up + string.Join('/', to.Skip(common));
    }

    public bool Equals(OpcPartName other) =>
        string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is OpcPartName other && Equals(other);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(OpcPartName a, OpcPartName b) => a.Equals(b);
    public static bool operator !=(OpcPartName a, OpcPartName b) => !a.Equals(b);

    public static implicit operator OpcPartName(string value) => new(value);
}
