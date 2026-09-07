// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections;
using System.Text;
using System.Xml.Linq;
using ExcelNet.Styles;
using OfficeNet.Core;
using OfficeNet.Core.Documents;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;

namespace ExcelNet;

/// <summary>
/// A spreadsheet workbook — the entry point of ExcelNet, and the analogue of openpyxl's
/// <c>Workbook</c>.
/// </summary>
/// <remarks>
/// <para>
/// Worksheets are parsed into memory on open and written back on save. That differs from WordNet's
/// live-XML approach for a reason: a worksheet's XML is a flat sequence of rows and cells, random
/// access to which would be a linear scan, and there is no producer-specific markup inside it worth
/// preserving byte for byte.
/// </para>
/// <para>
/// Parts the library does not model — pivot caches, VML drawings, macros, custom XML — travel
/// through a round trip untouched, because <see cref="OpcPackage"/> keeps their bytes and only the
/// sheets, styles and string table are rewritten.
/// </para>
/// </remarks>
public sealed class Workbook : OfficeDocument, IReadOnlyList<Worksheet>
{
    private static readonly OpcPartName WorkbookPartName = "/xl/workbook.xml";
    private static readonly OpcPartName StylesPartName = "/xl/styles.xml";
    private static readonly OpcPartName SharedStringsPartName = "/xl/sharedStrings.xml";

    private readonly OpcPart _workbookPart;
    private readonly List<Worksheet> _sheets = [];

    // Pivot caches are workbook-wide: the cacheId ties the workbook's <pivotCaches> entry to the
    // pivot table that uses it, and the two must agree or Excel reports the file as damaged.
    private readonly List<(int CacheId, OpcPart Definition)> _pivotCaches = [];
    private int _nextPivotCacheId = 1;
    private OpcPart? _sharedStringsPart;

    private Workbook(OpcPackage package, OpcPart workbookPart) : base(package)
    {
        _workbookPart = workbookPart;
    }

    /// <summary>The <c>xl/workbook.xml</c> part.</summary>
    public OpcPart WorkbookPart => _workbookPart;

    /// <summary>The workbook's format table.</summary>
    public Stylesheet Styles { get; private set; } = Stylesheet.CreateDefault();

    /// <summary>The workbook's shared string table.</summary>
    public SharedStrings SharedStrings { get; private set; } = new();

    internal int NextPivotCacheId() => _nextPivotCacheId++;

    /// <summary>Records a pivot cache so the workbook part can list it on save.</summary>
    internal void RegisterPivotCache(int cacheId, OpcPart definition) =>
        _pivotCaches.Add((cacheId, definition));

    /// <summary>The worksheets, in tab order.</summary>
    public IReadOnlyList<Worksheet> Worksheets => _sheets;

    /// <inheritdoc />
    public int Count => _sheets.Count;

    /// <inheritdoc />
    public Worksheet this[int index] => _sheets[index];

    /// <summary>A worksheet by name.</summary>
    /// <exception cref="OfficeNetException">No sheet has that name.</exception>
    public Worksheet this[string name] =>
        Find(name) ?? throw new OfficeNetException(
            $"The workbook has no sheet named '{name}'. It has: " +
            string.Join(", ", _sheets.Select(s => $"'{s.Name}'")));

    /// <summary>A worksheet by name, or <c>null</c>.</summary>
    public Worksheet? Find(string name) =>
        _sheets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The first worksheet.</summary>
    /// <exception cref="OfficeNetException">The workbook has no sheets.</exception>
    public Worksheet ActiveSheet => _sheets.Count > 0
        ? _sheets.FirstOrDefault(s => s.IsSelected) ?? _sheets[0]
        : throw new OfficeNetException("The workbook has no worksheets.");

    // ---- Construction --------------------------------------------------------------------------

    /// <summary>Creates a workbook with one empty sheet.</summary>
    public static Workbook Create(string firstSheetName = "Sheet1")
    {
        var package = OpcPackage.Create();

        var workbookPart = package.AddXmlPart(WorkbookPartName, ContentTypes.ExcelWorkbook,
            ExcelDefaultParts.Workbook());

        package.AddRootRelationship(workbookPart, RelationshipTypes.OfficeDocument);

        var workbook = new Workbook(package, workbookPart);
        workbook.AddSheet(firstSheetName);
        workbook._sheets[0].IsSelected = true;

        workbook.Properties.Created = DateTime.UtcNow;
        workbook.AppProperties.StampProducer();
        return workbook;
    }

    /// <summary>Opens an .xlsx from a file.</summary>
    public static Workbook Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromPackage(OpcPackage.Open(path));
    }

    /// <summary>Opens an .xlsx from a stream.</summary>
    public static Workbook Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return FromPackage(OpcPackage.Open(stream));
    }

    /// <summary>Opens an .xlsx from bytes.</summary>
    public static Workbook Open(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        return FromPackage(OpcPackage.Open(stream));
    }

    private static Workbook FromPackage(OpcPackage package)
    {
        var workbookPart = package.MainDocumentPart
            ?? throw new OfficeNetException(
                "The package has no main workbook part. It is not an .xlsx — check whether it is " +
                "a .docx or .pptx, which use the same container.");

        var expected = new[]
        {
            ContentTypes.ExcelWorkbook, ContentTypes.ExcelWorkbookMacroEnabled, ContentTypes.ExcelTemplate,
        };

        if (!expected.Contains(workbookPart.ContentType))
        {
            throw new OfficeNetException(
                $"The main part's content type is '{workbookPart.ContentType}', which is not " +
                "SpreadsheetML. Open it with WordNet or PowerPointNet instead.");
        }

        if (XmlUtil.NormalizeStrictNamespaces(workbookPart.Xml))
        {
            package.MarkDirty();
        }

        var workbook = new Workbook(package, workbookPart);
        workbook.Load();
        return workbook;
    }

    private void Load()
    {
        // Styles must be read before any sheet, because telling a date from a number needs the
        // cell's number format.
        var stylesPart = _workbookPart.RelatedPartByType(RelationshipTypes.SpreadsheetStyles);

        if (stylesPart?.Xml.Root is { } stylesRoot)
        {
            Styles = Stylesheet.Read(stylesRoot);
        }

        _sharedStringsPart = _workbookPart.RelatedPartByType(RelationshipTypes.SharedStrings);

        if (_sharedStringsPart is not null)
        {
            SharedStrings = SharedStrings.Read(_sharedStringsPart.GetBytes());
        }

        var root = _workbookPart.Xml.Root
            ?? throw new OfficeNetException("xl/workbook.xml is empty.");

        foreach (var element in root.Element(Ns.S + "sheets")?.Elements(Ns.S + "sheet") ?? [])
        {
            var name = element.Attr("name");
            var relationshipId = element.Attr(Ns.R + "id");

            if (name is null || relationshipId is null)
            {
                continue;
            }

            var part = _workbookPart.RelatedPart(relationshipId);

            // A chartsheet is listed among the sheets but has no cells; skipping it keeps its part
            // in the package while leaving it out of the worksheet collection.
            if (part is null || part.ContentType != ContentTypes.ExcelWorksheet)
            {
                continue;
            }

            var sheet = new Worksheet(this, name, part)
            {
                IsHidden = element.Attr("state") is "hidden" or "veryHidden",
            };

            SheetXml.Read(sheet, part.GetBytes(), SharedStrings);
            _sheets.Add(sheet);
        }

        if (_sheets.Count > 0 && !_sheets.Any(s => s.IsSelected))
        {
            _sheets[0].IsSelected = true;
        }
    }

    // ---- Sheets --------------------------------------------------------------------------------

    /// <summary>Adds a worksheet.</summary>
    /// <exception cref="OfficeNetException">A sheet with that name already exists.</exception>
    public Worksheet AddSheet(string name)
    {
        Worksheet.ValidateName(name);

        if (Find(name) is not null)
        {
            throw new OfficeNetException($"The workbook already has a sheet named '{name}'.");
        }

        var partName = Package.NextPartName("/xl/worksheets/sheet{0}.xml");
        var part = Package.AddPart(partName, ContentTypes.ExcelWorksheet,
            ExcelDefaultParts.EmptySheet());

        _workbookPart.AddRelationship(part, RelationshipTypes.Worksheet);

        var sheet = new Worksheet(this, name, part);
        _sheets.Add(sheet);
        Package.MarkDirty();
        return sheet;
    }

    /// <summary>Adds a worksheet, giving it a unique name when the requested one is taken.</summary>
    public Worksheet AddSheetUnique(string baseName)
    {
        Worksheet.ValidateName(baseName);

        if (Find(baseName) is null)
        {
            return AddSheet(baseName);
        }

        for (var i = 2; ; i++)
        {
            var suffix = $" ({i})";
            // The 31-character cap applies to the final name, so the base is trimmed to fit.
            var trimmed = baseName.Length + suffix.Length > 31
                ? baseName[..(31 - suffix.Length)]
                : baseName;

            var candidate = trimmed + suffix;

            if (Find(candidate) is null)
            {
                return AddSheet(candidate);
            }
        }
    }

    /// <summary>Removes a worksheet and its part.</summary>
    /// <exception cref="OfficeNetException">It is the workbook's only sheet.</exception>
    public bool RemoveSheet(string name)
    {
        var sheet = Find(name);

        if (sheet is null)
        {
            return false;
        }

        if (_sheets.Count == 1)
        {
            throw new OfficeNetException(
                "A workbook must contain at least one worksheet; removing the last one produces a " +
                "file Excel cannot open.");
        }

        _sheets.Remove(sheet);
        Package.RemovePart(sheet.Part.Name);
        Package.MarkDirty();

        if (!_sheets.Any(s => s.IsSelected))
        {
            _sheets[0].IsSelected = true;
        }

        return true;
    }

    /// <summary>Moves a worksheet to a different tab position.</summary>
    public void MoveSheet(string name, int newIndex)
    {
        var sheet = this[name];
        ArgumentOutOfRangeException.ThrowIfNegative(newIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(newIndex, _sheets.Count);

        _sheets.Remove(sheet);
        _sheets.Insert(newIndex, sheet);
        Package.MarkDirty();
    }

    /// <summary>
    /// Copies a worksheet, including its cells, styles, merges and column widths.
    /// </summary>
    public Worksheet CopySheet(string sourceName, string? newName = null)
    {
        var source = this[sourceName];
        var target = AddSheetUnique(newName ?? sourceName);

        foreach (var (reference, data) in source.RawCells)
        {
            target.PutCellData(reference, data);
        }

        foreach (var merge in source.MergedRanges)
        {
            target.AddMergeUnchecked(merge);
        }

        foreach (var (column, width) in source.ColumnWidths)
        {
            target.SetColumnWidth(column, width);
        }

        foreach (var (row, height) in source.RowHeights)
        {
            target.SetRowHeight(row, height);
        }

        foreach (var rule in source.ConditionalRules)
        {
            target.AddConditionalRuleUnchecked(rule);
        }

        target.Frozen = source.Frozen;
        target.AutoFilter = source.AutoFilter;
        target.ShowGridLines = source.ShowGridLines;
        target.TabColor = source.TabColor;

        return target;
    }

    // ---- Formulas ------------------------------------------------------------------------------

    /// <summary>
    /// Evaluates every formula in the workbook and caches the results.
    /// </summary>
    /// <remarks>
    /// Excel recalculates on open, so this is not needed for a file destined for Excel. It matters
    /// for everything else: a PDF export, a CSV export, or a consumer reading values will show the
    /// cached result, and a freshly-written formula has none.
    /// </remarks>
    public int Recalculate()
    {
        var engine = new Formulas.FormulaEngine(this);
        return engine.EvaluateAll();
    }

    // ---- Relationships ---------------------------------------------------------------------------

    internal string EnsureHyperlink(Worksheet sheet, string target)
    {
        var existing = sheet.Part.RelationshipsByType(RelationshipTypes.Hyperlink)
            .FirstOrDefault(r => r.TargetMode == TargetMode.External && r.Target == target);

        return existing?.Id
               ?? sheet.Part.AddExternalRelationship(RelationshipTypes.Hyperlink, target).Id;
    }

    internal OpcPart AddImage(byte[] imageBytes, out OfficeNet.Core.Drawing.ImageInfo info) =>
        AddImagePart(imageBytes, "/xl/media", out info);

    // ---- Saving --------------------------------------------------------------------------------

    /// <inheritdoc />
    protected override void FlushToPackage()
    {
        if (_sheets.Count == 0)
        {
            throw new OfficeNetException(
                "A workbook must contain at least one worksheet before it can be saved.");
        }

        // The reference count is recomputed as the sheets are written, so it has to start clean or
        // repeated saves inflate it.
        SharedStrings.ResetReferenceCount();

        foreach (var sheet in _sheets)
        {
            sheet.Part.SetBytes(SheetXml.Write(sheet, SharedStrings));
        }

        WriteWorkbookXml();
        WriteStyles();
        WriteSharedStrings();
    }

    private void WriteWorkbookXml()
    {
        var root = _workbookPart.Xml.Root
            ?? throw new OfficeNetException("xl/workbook.xml is empty.");

        var sheets = root.Element(Ns.S + "sheets");

        if (sheets is null)
        {
            sheets = new XElement(Ns.S + "sheets");
            root.Add(sheets);
        }

        // Sheets not modelled here (chartsheets) keep their entries; the worksheet entries are
        // rebuilt because their order is the tab order.
        var keep = sheets.Elements(Ns.S + "sheet")
            .Where(e =>
            {
                var id = e.Attr(Ns.R + "id");
                var part = id is null ? null : _workbookPart.RelatedPart(id);
                return part is not null && part.ContentType != ContentTypes.ExcelWorksheet;
            })
            .Select(e => new XElement(e))
            .ToList();

        sheets.RemoveNodes();

        var sheetId = 1;

        foreach (var sheet in _sheets)
        {
            var relationship = _workbookPart.RelationshipsByType(RelationshipTypes.Worksheet)
                .FirstOrDefault(r => r.TargetMode == TargetMode.Internal &&
                                     r.TargetPartName == sheet.Part.Name)
                ?? _workbookPart.AddRelationship(sheet.Part, RelationshipTypes.Worksheet);

            var element = new XElement(Ns.S + "sheet",
                new XAttribute("name", sheet.Name),
                // sheetId is an internal identifier, unrelated to tab order and to the
                // relationship id. Excel tolerates gaps but not duplicates.
                new XAttribute("sheetId", sheetId++),
                new XAttribute(Ns.R + "id", relationship.Id));

            if (sheet.IsHidden)
            {
                element.SetAttributeValue("state", "hidden");
            }

            sheets.Add(element);
        }

        foreach (var element in keep)
        {
            sheets.Add(element);
        }

        var activeIndex = Math.Max(0, _sheets.FindIndex(s => s.IsSelected));

        var views = root.Element(Ns.S + "bookViews");

        if (views is null)
        {
            views = new XElement(Ns.S + "bookViews");
            // bookViews must precede sheets in the schema sequence.
            sheets.AddBeforeSelf(views);
        }

        views.RemoveNodes();
        views.Add(new XElement(Ns.S + "workbookView",
            new XAttribute("activeTab", activeIndex)));

        WritePivotCaches(root);

        Package.MarkDirty();
    }

    /// <summary>
    /// Lists the pivot caches on the workbook part.
    /// </summary>
    /// <remarks>
    /// <c>pivotCaches</c> sits near the end of the CT_Workbook sequence — after <c>sheets</c> and
    /// <c>definedNames</c> — so it is appended rather than inserted. A cache the workbook does not
    /// list is a cache no pivot table can reach, and Excel reports the file as damaged.
    /// </remarks>
    private void WritePivotCaches(XElement root)
    {
        root.Elements(Ns.S + "pivotCaches").Remove();

        if (_pivotCaches.Count == 0)
        {
            return;
        }

        var caches = new XElement(Ns.S + "pivotCaches");

        foreach (var (cacheId, definition) in _pivotCaches)
        {
            var relationship = _workbookPart
                .RelationshipsByType(RelationshipTypes.PivotCacheDefinition)
                .FirstOrDefault(r => r.TargetPartName == definition.Name)
                ?? _workbookPart.AddRelationship(definition, RelationshipTypes.PivotCacheDefinition);

            caches.Add(new XElement(Ns.S + "pivotCache",
                new XAttribute("cacheId", cacheId),
                new XAttribute(Ns.R + "id", relationship.Id)));
        }

        root.Add(caches);
    }

    private void WriteStyles()
    {
        var part = _workbookPart.RelatedPartByType(RelationshipTypes.SpreadsheetStyles);

        if (part is null)
        {
            part = Package.AddPart(StylesPartName, ContentTypes.ExcelStyles);
            _workbookPart.AddRelationship(part, RelationshipTypes.SpreadsheetStyles);
        }

        part.Xml = Styles.ToXml();
    }

    private void WriteSharedStrings()
    {
        if (SharedStrings.Count == 0)
        {
            // An empty table is legal but pointless; leaving the part out keeps the package clean.
            return;
        }

        if (_sharedStringsPart is null)
        {
            _sharedStringsPart = Package.AddPart(SharedStringsPartName, ContentTypes.ExcelSharedStrings);
            _workbookPart.AddRelationship(_sharedStringsPart, RelationshipTypes.SharedStrings);
        }

        _sharedStringsPart.SetBytes(SharedStrings.Write());
    }

    // ---- Text ----------------------------------------------------------------------------------

    /// <inheritdoc />
    public override string ExtractText()
    {
        var builder = new StringBuilder();

        foreach (var sheet in _sheets)
        {
            // Newlines are written explicitly rather than through AppendLine: Environment.NewLine
            // would make the same workbook extract differently on Windows and on Linux.
            builder.Append("# ").Append(sheet.Name).Append('\n');

            if (sheet.UsedRange is not { } used)
            {
                continue;
            }

            for (var row = used.Start.Row; row <= used.End.Row; row++)
            {
                var line = new List<string>();

                for (var column = used.Start.Column; column <= used.End.Column; column++)
                {
                    line.Add(sheet[new CellReference(row, column)].Text);
                }

                // A row that is entirely empty inside the used range adds nothing to extracted text.
                if (line.Any(v => v.Length > 0))
                {
                    builder.Append(string.Join('\t', line)).Append('\n');
                }
            }

            builder.Append('\n');
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Renders the workbook to PDF.</summary>
    public void SaveAsPdf(string path, Io.ExcelPdfOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var pdf = Io.ExcelToPdf.Convert(this, options);
        pdf.Save(path);
    }

    /// <summary>Renders the workbook to PDF and writes it to a stream.</summary>
    /// <remarks>
    /// The stream overload exists so a workbook can be converted without touching the file system —
    /// a web handler returning bytes, or a test asserting on the result.
    /// </remarks>
    public void SaveAsPdf(Stream stream, Io.ExcelPdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var pdf = Io.ExcelToPdf.Convert(this, options);
        pdf.Save(stream);
    }

    /// <summary>Renders the workbook to a PDF document.</summary>
    public PdfNet.Document.PdfDocument ToPdf(Io.ExcelPdfOptions? options = null) =>
        Io.ExcelToPdf.Convert(this, options);

    /// <inheritdoc />
    public IEnumerator<Worksheet> GetEnumerator() => _sheets.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() =>
        $"Workbook({_sheets.Count} sheets: {string.Join(", ", _sheets.Select(s => s.Name))})";
}
