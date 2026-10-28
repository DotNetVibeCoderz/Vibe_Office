// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections;
using System.Text.RegularExpressions;
using OfficeNet.Core;

namespace ExcelNet;

/// <summary>
/// A named range or constant: <c>Penjualan</c> standing for <c>Data!$B$2:$B$100</c>.
/// </summary>
public sealed class DefinedName
{
    internal DefinedName(string name, string refersTo, Worksheet? scope, bool hidden, string? comment)
    {
        Name = name;
        RefersTo = refersTo;
        Scope = scope;
        Hidden = hidden;
        Comment = comment;
    }

    /// <summary>The name, as typed in formulas. Matching is case-insensitive, as in Excel.</summary>
    public string Name { get; }

    /// <summary>
    /// What the name stands for, without a leading <c>=</c>: usually a sheet-qualified absolute
    /// reference (<c>'Data Penjualan'!$B$2:$B$100</c>), but a constant or a formula is allowed too.
    /// </summary>
    public string RefersTo { get; internal set; }

    /// <summary>The sheet the name is local to, or <c>null</c> for a workbook-wide name.</summary>
    public Worksheet? Scope { get; internal set; }

    /// <summary>Hidden from Excel's Name Manager. Excel's own built-in names often are.</summary>
    public bool Hidden { get; }

    /// <summary>A description shown in the Name Manager.</summary>
    public string? Comment { get; }

    /// <summary>The sheet and range the name refers to, when it refers to one.</summary>
    public bool TryGetRange(out string? sheetName, out CellRangeReference range) =>
        DefinedNameCollection.TryParseReference(RefersTo, out sheetName, out range);

    public override string ToString() => $"{Name} = {RefersTo}{(Scope is null ? "" : $" (local to {Scope.Name})")}";
}

/// <summary>
/// The workbook's defined names (<c>definedNames</c> in <c>workbook.xml</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A local name points at its sheet by position, not by name.</b> The file stores
/// <c>localSheetId</c>, the sheet's index in the tab order. Here the scope is the
/// <see cref="Worksheet"/> itself and the index is worked out when the file is written, so moving a
/// sheet cannot silently hand its print area to the sheet that took its place.
/// </para>
/// <para>
/// Renaming a sheet rewrites the names that refer to it, and removing a sheet removes the names
/// local to it — the two things Excel does that leave a file consistent. A name referring to a
/// removed sheet is left as it is; Excel would show <c>#REF!</c> there too.
/// </para>
/// </remarks>
public sealed partial class DefinedNameCollection : IReadOnlyCollection<DefinedName>
{
    /// <summary>The prefix Excel reserves for its own names: print areas, filter ranges.</summary>
    internal const string BuiltInPrefix = "_xlnm.";

    private readonly Workbook _workbook;
    private readonly List<DefinedName> _names = [];

    internal DefinedNameCollection(Workbook workbook) => _workbook = workbook;

    public int Count => _names.Count;

    /// <summary>The name visible from <paramref name="sheet"/>: its local name if it has one, else the global one.</summary>
    public DefinedName? Find(string name, Worksheet? sheet = null) =>
        (sheet is null ? null : _names.FirstOrDefault(n => n.Scope == sheet && Same(n.Name, name)))
        ?? _names.FirstOrDefault(n => n.Scope is null && Same(n.Name, name));

    /// <summary>A workbook-wide name.</summary>
    /// <exception cref="KeyNotFoundException">There is no such name.</exception>
    public DefinedName this[string name] =>
        _names.FirstOrDefault(n => n.Scope is null && Same(n.Name, name))
        ?? throw new KeyNotFoundException($"The workbook has no name '{name}'.");

    /// <summary>Names a range on a sheet: <c>Add("Penjualan", sheet, "B2:B100")</c>.</summary>
    public DefinedName Add(string name, Worksheet sheet, string range, bool localToSheet = false)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return Add(name, Qualified(sheet.Name, CellRangeReference.Parse(range)), localToSheet ? sheet : null);
    }

    /// <summary>Adds a name for any reference, constant or formula (a leading <c>=</c> is optional).</summary>
    /// <exception cref="ArgumentException">The name is not one Excel accepts.</exception>
    /// <exception cref="OfficeNetException">The name is already defined in that scope.</exception>
    public DefinedName Add(string name, string refersTo, Worksheet? scope = null, string? comment = null)
    {
        Validate(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(refersTo);

        if (_names.Any(n => n.Scope == scope && Same(n.Name, name)))
        {
            throw new OfficeNetException(
                $"'{name}' is already defined {(scope is null ? "in the workbook" : $"on {scope.Name}")}.");
        }

        var defined = new DefinedName(name, refersTo.TrimStart('='), scope, hidden: false, comment);
        _names.Add(defined);
        _workbook.Package.MarkDirty();
        return defined;
    }

    /// <summary>Removes a name from a scope (<c>null</c> for the workbook).</summary>
    public bool Remove(string name, Worksheet? scope = null)
    {
        var removed = _names.RemoveAll(n => n.Scope == scope && Same(n.Name, name)) > 0;

        if (removed)
        {
            _workbook.Package.MarkDirty();
        }

        return removed;
    }

    public IEnumerator<DefinedName> GetEnumerator() => _names.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ---- Built-in names ------------------------------------------------------------------------

    internal string? GetBuiltIn(Worksheet sheet, string name) =>
        _names.FirstOrDefault(n => n.Scope == sheet && Same(n.Name, BuiltInPrefix + name))?.RefersTo;

    internal void SetBuiltIn(Worksheet sheet, string name, string? refersTo)
    {
        _names.RemoveAll(n => n.Scope == sheet && Same(n.Name, BuiltInPrefix + name));

        if (refersTo is not null)
        {
            _names.Add(new DefinedName(BuiltInPrefix + name, refersTo, sheet, hidden: false, comment: null));
        }

        _workbook.Package.MarkDirty();
    }

    // ---- Workbook events -----------------------------------------------------------------------

    internal void Load(string name, string refersTo, Worksheet? scope, bool hidden, string? comment) =>
        _names.Add(new DefinedName(name, refersTo.TrimStart('='), scope, hidden, comment));

    internal void SheetRenamed(string oldName, string newName)
    {
        foreach (var name in _names)
        {
            name.RefersTo = name.RefersTo
                .Replace(Quote(oldName) + "!", Quote(newName) + "!", StringComparison.Ordinal);
        }
    }

    internal void SheetRemoved(Worksheet sheet) => _names.RemoveAll(n => n.Scope == sheet);

    /// <summary>Copies a sheet's local names — its print area, say — onto its copy.</summary>
    internal void SheetCopied(Worksheet source, Worksheet copy)
    {
        foreach (var name in _names.Where(n => n.Scope == source).ToList())
        {
            _names.Add(new DefinedName(name.Name,
                name.RefersTo.Replace(Quote(source.Name) + "!", Quote(copy.Name) + "!", StringComparison.Ordinal),
                copy, name.Hidden, name.Comment));
        }
    }

    // ---- Syntax --------------------------------------------------------------------------------

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^[A-Za-z_\\][A-Za-z0-9_.\\]*$")]
    private static partial Regex NameSyntax();

    [GeneratedRegex(@"^[Rr]\d*([Cc]\d*)?$|^[Cc]\d*$")]
    private static partial Regex R1C1Like();

    /// <summary>
    /// Excel's rules: a letter, underscore or backslash first; letters, digits, dots and
    /// underscores after; never something that reads as a cell (<c>A1</c>, <c>XFD1</c>) or an R1C1
    /// reference (<c>R</c>, <c>C</c>, <c>R2C3</c>), because a formula could not tell them apart.
    /// </summary>
    internal static void Validate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Length > 255 || !NameSyntax().IsMatch(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a valid name: start with a letter or underscore, then use letters, " +
                "digits, dots and underscores only, at most 255 characters.", nameof(name));
        }

        if (CellReference.TryParse(name, out _) || R1C1Like().IsMatch(name))
        {
            throw new ArgumentException($"'{name}' reads as a cell reference and cannot be a name.", nameof(name));
        }

        if (name.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"'{name}' uses Excel's reserved prefix; set Worksheet.PrintArea or PrintTitleRows instead.",
                nameof(name));
        }
    }

    /// <summary><c>Data</c> stays bare; <c>Data Penjualan</c> and <c>2026</c> are quoted, with quotes doubled.</summary>
    internal static string Quote(string sheetName) =>
        Regex.IsMatch(sheetName, @"^[A-Za-z_][A-Za-z0-9_.]*$") && !CellReference.TryParse(sheetName, out _)
            ? sheetName
            : "'" + sheetName.Replace("'", "''", StringComparison.Ordinal) + "'";

    internal static string Qualified(string sheetName, CellRangeReference range)
    {
        static string Absolute(CellReference c) => $"${c.ColumnName}${c.RowNumber}";

        var reference = range.Start == range.End
            ? Absolute(range.Start)
            : $"{Absolute(range.Start)}:{Absolute(range.End)}";

        return Quote(sheetName) + "!" + reference;
    }

    /// <summary>Reads <c>'Data Penjualan'!$B$2:$B$100</c> into a sheet name and a range.</summary>
    internal static bool TryParseReference(string refersTo, out string? sheetName, out CellRangeReference range)
    {
        sheetName = null;
        range = default;

        var text = refersTo.Trim().TrimStart('=');
        var bang = text.LastIndexOf('!');
        var rangeText = text;

        if (bang >= 0)
        {
            var sheetPart = text[..bang];
            rangeText = text[(bang + 1)..];

            sheetName = sheetPart.Length >= 2 && sheetPart[0] == '\'' && sheetPart[^1] == '\''
                ? sheetPart[1..^1].Replace("''", "'", StringComparison.Ordinal)
                : sheetPart;
        }

        return CellRangeReference.TryParse(rangeText.Replace("$", "", StringComparison.Ordinal), out range);
    }
}
