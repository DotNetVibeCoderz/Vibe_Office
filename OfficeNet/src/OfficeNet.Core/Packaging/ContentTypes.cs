// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace OfficeNet.Core.Packaging;

/// <summary>The MIME content types OOXML parts declare in <c>[Content_Types].xml</c>.</summary>
public static class ContentTypes
{
    private const string Word = "application/vnd.openxmlformats-officedocument.wordprocessingml";
    private const string Excel = "application/vnd.openxmlformats-officedocument.spreadsheetml";
    private const string Ppt = "application/vnd.openxmlformats-officedocument.presentationml";
    private const string Drawing = "application/vnd.openxmlformats-officedocument.drawingml";
    private const string PackageNs = "application/vnd.openxmlformats-package";

    /// <summary>The relationship part type, declared as a Default for the <c>rels</c> extension.</summary>
    public const string Relationships = PackageNs + ".relationships+xml";

    /// <summary>Core (Dublin Core) document properties.</summary>
    public const string CoreProperties = PackageNs + ".core-properties+xml";

    /// <summary>Extended (application) document properties.</summary>
    public const string ExtendedProperties =
        "application/vnd.openxmlformats-officedocument.extended-properties+xml";

    /// <summary>Custom document properties.</summary>
    public const string CustomProperties =
        "application/vnd.openxmlformats-officedocument.custom-properties+xml";

    /// <summary>The DrawingML theme part.</summary>
    public const string Theme = Drawing + ".theme+xml";

    /// <summary>A DrawingML chart part.</summary>
    public const string Chart = Drawing + ".chart+xml";

    /// <summary>
    /// A spreadsheet drawing part, which anchors charts and pictures to a worksheet.
    /// </summary>
    /// <remarks>
    /// Note the shape of this one: it is <c>…officedocument.drawing+xml</c>, without the
    /// <c>drawingml</c> segment the theme and chart types carry. Using the DrawingML form here
    /// produces a package Excel repairs by discarding the drawing.
    /// </remarks>
    public const string SpreadsheetDrawing =
        "application/vnd.openxmlformats-officedocument.drawing+xml";

    /// <summary>A generic XML part.</summary>
    public const string Xml = "application/xml";

    // ---- WordprocessingML ---------------------------------------------------------------------

    /// <summary>The main part of a .docx.</summary>
    public const string WordDocument = Word + ".document.main+xml";

    /// <summary>The main part of a macro-enabled .docm.</summary>
    public const string WordDocumentMacroEnabled = "application/vnd.ms-word.document.macroEnabled.main+xml";

    /// <summary>The main part of a .dotx template.</summary>
    public const string WordTemplate = Word + ".template.main+xml";

    /// <summary>Word style definitions.</summary>
    public const string WordStyles = Word + ".styles+xml";

    /// <summary>Word numbering definitions.</summary>
    public const string WordNumbering = Word + ".numbering+xml";

    /// <summary>Word document settings.</summary>
    public const string WordSettings = Word + ".settings+xml";

    /// <summary>Word web settings.</summary>
    public const string WordWebSettings = Word + ".webSettings+xml";

    /// <summary>Word font table.</summary>
    public const string WordFontTable = Word + ".fontTable+xml";

    /// <summary>A Word section header.</summary>
    public const string WordHeader = Word + ".header+xml";

    /// <summary>A Word section footer.</summary>
    public const string WordFooter = Word + ".footer+xml";

    /// <summary>Word footnote definitions.</summary>
    public const string WordFootnotes = Word + ".footnotes+xml";

    /// <summary>Word endnote definitions.</summary>
    public const string WordEndnotes = Word + ".endnotes+xml";

    /// <summary>Word comment definitions.</summary>
    public const string WordComments = Word + ".comments+xml";

    // ---- SpreadsheetML ------------------------------------------------------------------------

    /// <summary>The main part of a .xlsx.</summary>
    public const string ExcelWorkbook = Excel + ".sheet.main+xml";

    /// <summary>The main part of a macro-enabled .xlsm.</summary>
    public const string ExcelWorkbookMacroEnabled = "application/vnd.ms-excel.sheet.macroEnabled.main+xml";

    /// <summary>The main part of a .xltx template.</summary>
    public const string ExcelTemplate = Excel + ".template.main+xml";

    /// <summary>A worksheet.</summary>
    public const string ExcelWorksheet = Excel + ".worksheet+xml";

    /// <summary>A chartsheet.</summary>
    public const string ExcelChartsheet = Excel + ".chartsheet+xml";

    /// <summary>The shared string table.</summary>
    public const string ExcelSharedStrings = Excel + ".sharedStrings+xml";

    /// <summary>Cell format and style definitions.</summary>
    public const string ExcelStyles = Excel + ".styles+xml";

    /// <summary>A pivot table definition.</summary>
    public const string ExcelPivotTable = Excel + ".pivotTable+xml";

    /// <summary>A pivot cache definition.</summary>
    public const string ExcelPivotCacheDefinition = Excel + ".pivotCacheDefinition+xml";

    /// <summary>A pivot cache records part.</summary>
    public const string ExcelPivotCacheRecords = Excel + ".pivotCacheRecords+xml";

    /// <summary>A defined table (ListObject).</summary>
    public const string ExcelTable = Excel + ".table+xml";

    /// <summary>The calculation chain.</summary>
    public const string ExcelCalcChain = Excel + ".calcChain+xml";

    // ---- PresentationML -----------------------------------------------------------------------

    /// <summary>The main part of a .pptx.</summary>
    public const string PowerPointPresentation = Ppt + ".presentation.main+xml";

    /// <summary>The main part of a .ppsx slideshow.</summary>
    public const string PowerPointSlideshow = Ppt + ".slideshow.main+xml";

    /// <summary>The main part of a .potx template.</summary>
    public const string PowerPointTemplate = Ppt + ".template.main+xml";

    /// <summary>A slide.</summary>
    public const string PowerPointSlide = Ppt + ".slide+xml";

    /// <summary>A slide layout.</summary>
    public const string PowerPointSlideLayout = Ppt + ".slideLayout+xml";

    /// <summary>A slide master.</summary>
    public const string PowerPointSlideMaster = Ppt + ".slideMaster+xml";

    /// <summary>A notes slide.</summary>
    public const string PowerPointNotesSlide = Ppt + ".notesSlide+xml";

    /// <summary>The notes master.</summary>
    public const string PowerPointNotesMaster = Ppt + ".notesMaster+xml";

    /// <summary>The handout master.</summary>
    public const string PowerPointHandoutMaster = Ppt + ".handoutMaster+xml";

    /// <summary>Presentation-wide properties.</summary>
    public const string PowerPointPresProps = Ppt + ".presProps+xml";

    /// <summary>View properties.</summary>
    public const string PowerPointViewProps = Ppt + ".viewProps+xml";

    /// <summary>Table style definitions.</summary>
    public const string PowerPointTableStyles = Ppt + ".tableStyles+xml";

    // ---- Diagrams (SmartArt) ---------------------------------------------------------------

    /// <summary>A diagram's data model: its nodes, their text, and how they connect.</summary>
    public const string DiagramData = Drawing + ".diagramData+xml";

    /// <summary>A diagram's layout algorithm.</summary>
    public const string DiagramLayout = Drawing + ".diagramLayout+xml";

    /// <summary>A diagram's colour scheme.</summary>
    public const string DiagramColors = Drawing + ".diagramColors+xml";

    /// <summary>A diagram's visual style.</summary>
    public const string DiagramStyle = Drawing + ".diagramStyle+xml";

    /// <summary>
    /// A diagram's rendered shapes, in the Microsoft extension namespace.
    /// </summary>
    /// <remarks>
    /// Not one of the four standard parts. It caches what the layout algorithm produced, and it is
    /// what a reader that does not run that algorithm draws instead.
    /// </remarks>
    public const string DiagramDrawing = "application/vnd.ms-office.drawingml.diagramDrawing+xml";

    // ---- Media --------------------------------------------------------------------------------

    /// <summary>A PDF part or file.</summary>
    public const string Pdf = "application/pdf";

    /// <summary>
    /// The content type for a media extension, or <c>null</c> when the extension is unknown.
    /// </summary>
    /// <remarks>
    /// Only extensions that go into <c>[Content_Types].xml</c> as a Default entry belong here.
    /// A part whose extension is not registered makes the whole package invalid, so an unknown
    /// extension is reported rather than guessed at with <c>application/octet-stream</c> — Word
    /// accepts the octet-stream default but then refuses to display the image.
    /// </remarks>
    public static string? ForExtension(string extension)
    {
        return extension.TrimStart('.').ToLowerInvariant() switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            "tif" or "tiff" => "image/tiff",
            "svg" => "image/svg+xml",
            "webp" => "image/webp",
            "ico" => "image/x-icon",
            "emf" => "image/x-emf",
            "wmf" => "image/x-wmf",
            "mp4" => "video/mp4",
            "m4v" => "video/mp4",
            "avi" => "video/avi",
            "mov" => "video/quicktime",
            "wmv" => "video/x-ms-wmv",
            "mp3" => "audio/mpeg",
            "wav" => "audio/wav",
            "m4a" => "audio/mp4",
            "pdf" => Pdf,
            "xml" => Xml,
            "rels" => Relationships,
            "bin" => "application/vnd.openxmlformats-officedocument.oleObject",
            "vml" => "application/vnd.openxmlformats-officedocument.vmlDrawing",
            "txt" => "text/plain",
            "csv" => "text/csv",
            "json" => "application/json",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => null,
        };
    }
}
