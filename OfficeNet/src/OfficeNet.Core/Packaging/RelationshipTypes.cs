// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace OfficeNet.Core.Packaging;

/// <summary>
/// The relationship type URIs OOXML uses. These are opaque identifiers, not fetchable addresses.
/// </summary>
/// <remarks>
/// Two families exist and they are not interchangeable. Package-level metadata relationships live
/// under <c>.../package/2006/relationships/</c>; everything a document part points at lives under
/// <c>.../officeDocument/2006/relationships/</c>. Writing the wrong prefix produces a package that
/// unzips fine and that Word reports as corrupt.
/// </remarks>
public static class RelationshipTypes
{
    private const string Package = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string Office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Microsoft = "http://schemas.microsoft.com/office/2007/relationships";

    // ---- Package level ------------------------------------------------------------------------

    /// <summary>The main document part: <c>word/document.xml</c>, <c>xl/workbook.xml</c>, <c>ppt/presentation.xml</c>.</summary>
    public const string OfficeDocument = Office + "/officeDocument";

    /// <summary>Dublin Core document properties (<c>docProps/core.xml</c>).</summary>
    public const string CoreProperties = Package + "/metadata/core-properties";

    /// <summary>Application properties (<c>docProps/app.xml</c>).</summary>
    public const string ExtendedProperties = Office + "/extended-properties";

    /// <summary>User-defined document properties (<c>docProps/custom.xml</c>).</summary>
    public const string CustomProperties = Office + "/custom-properties";

    /// <summary>A package thumbnail image.</summary>
    public const string Thumbnail = Package + "/metadata/thumbnail";

    // ---- Shared across formats ----------------------------------------------------------------

    /// <summary>An embedded image, video or audio part.</summary>
    public const string Image = Office + "/image";

    /// <summary>An external or internal hyperlink.</summary>
    public const string Hyperlink = Office + "/hyperlink";

    /// <summary>The theme part (<c>theme/theme1.xml</c>).</summary>
    public const string Theme = Office + "/theme";

    /// <summary>A DrawingML chart part.</summary>
    public const string Chart = Office + "/chart";

    /// <summary>The chart's cached or live data workbook.</summary>
    public const string Package_ = Office + "/package";

    /// <summary>A custom XML data store item.</summary>
    public const string CustomXml = Office + "/customXml";

    /// <summary>An OLE object embedded in the document.</summary>
    public const string OleObject = Office + "/oleObject";

    // ---- WordprocessingML ---------------------------------------------------------------------

    /// <summary>Style definitions (<c>word/styles.xml</c>).</summary>
    public const string Styles = Office + "/styles";

    /// <summary>Numbering definitions (<c>word/numbering.xml</c>).</summary>
    public const string Numbering = Office + "/numbering";

    /// <summary>Document settings (<c>word/settings.xml</c>).</summary>
    public const string Settings = Office + "/settings";

    /// <summary>Web settings (<c>word/webSettings.xml</c>).</summary>
    public const string WebSettings = Office + "/webSettings";

    /// <summary>Font table (<c>word/fontTable.xml</c>).</summary>
    public const string FontTable = Office + "/fontTable";

    /// <summary>A section header.</summary>
    public const string Header = Office + "/header";

    /// <summary>A section footer.</summary>
    public const string Footer = Office + "/footer";

    /// <summary>Footnote definitions.</summary>
    public const string Footnotes = Office + "/footnotes";

    /// <summary>Endnote definitions.</summary>
    public const string Endnotes = Office + "/endnotes";

    /// <summary>Comment definitions.</summary>
    public const string Comments = Office + "/comments";

    /// <summary>The glossary (building blocks) document.</summary>
    public const string GlossaryDocument = Office + "/glossaryDocument";

    // ---- SpreadsheetML ------------------------------------------------------------------------

    /// <summary>A worksheet part.</summary>
    public const string Worksheet = Office + "/worksheet";

    /// <summary>A chartsheet part.</summary>
    public const string Chartsheet = Office + "/chartsheet";

    /// <summary>The shared string table.</summary>
    public const string SharedStrings = Office + "/sharedStrings";

    /// <summary>Cell formats and styles (<c>xl/styles.xml</c>).</summary>
    public const string SpreadsheetStyles = Office + "/styles";

    /// <summary>A pivot table definition.</summary>
    public const string PivotTable = Office + "/pivotTable";

    /// <summary>A pivot cache definition.</summary>
    public const string PivotCacheDefinition = Office + "/pivotCacheDefinition";

    /// <summary>A pivot cache records part.</summary>
    public const string PivotCacheRecords = Office + "/pivotCacheRecords";

    /// <summary>A worksheet's drawing (shapes, charts, images) part.</summary>
    public const string Drawing = Office + "/drawing";

    /// <summary>A defined table (ListObject) part.</summary>
    public const string Table = Office + "/table";

    /// <summary>The calculation chain.</summary>
    public const string CalcChain = Office + "/calcChain";

    /// <summary>Volatile dependencies, written by newer Excel versions.</summary>
    public const string VolatileDependencies = Microsoft + "/volatileDependencies";

    // ---- PresentationML -----------------------------------------------------------------------

    /// <summary>A slide part.</summary>
    public const string Slide = Office + "/slide";

    /// <summary>A slide layout part.</summary>
    public const string SlideLayout = Office + "/slideLayout";

    /// <summary>A slide master part.</summary>
    public const string SlideMaster = Office + "/slideMaster";

    /// <summary>A notes slide part.</summary>
    public const string NotesSlide = Office + "/notesSlide";

    /// <summary>The notes master part.</summary>
    public const string NotesMaster = Office + "/notesMaster";

    /// <summary>The handout master part.</summary>
    public const string HandoutMaster = Office + "/handoutMaster";

    /// <summary>Presentation-wide properties.</summary>
    public const string PresProps = Office + "/presProps";

    /// <summary>View properties.</summary>
    public const string ViewProps = Office + "/viewProps";

    /// <summary>Table styles for PresentationML tables.</summary>
    public const string TableStyles = Office + "/tableStyles";

    // ---- Diagrams (SmartArt) ---------------------------------------------------------------

    /// <summary>A diagram's data model.</summary>
    public const string DiagramData = Office + "/diagramData";

    /// <summary>A diagram's layout algorithm.</summary>
    public const string DiagramLayout = Office + "/diagramLayout";

    /// <summary>A diagram's colour scheme.</summary>
    public const string DiagramColors = Office + "/diagramColors";

    /// <summary>A diagram's visual style.</summary>
    public const string DiagramQuickStyle = Office + "/diagramQuickStyle";

    /// <summary>
    /// A diagram's rendered shapes, in the Microsoft extension namespace.
    /// </summary>
    /// <remarks>
    /// Held by the data part rather than by the slide, and named under
    /// <c>schemas.microsoft.com/office/2007</c> rather than the ECMA prefix, because it is an
    /// extension to the standard rather than part of it.
    /// </remarks>
    public const string DiagramDrawing =
        "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";

    /// <summary>
    /// Embedded media referenced by a slide, in the Microsoft extension namespace.
    /// </summary>
    /// <remarks>
    /// A media clip needs two relationships to the same part: this one, which the PowerPoint 2010
    /// <c>p14:media</c> extension reads, and <see cref="Video"/> or <see cref="Audio"/>, which the
    /// 2007 <c>a:videoFile</c> element reads. A file carrying only one plays in some versions of
    /// PowerPoint and shows a black rectangle in others.
    /// </remarks>
    public const string Media = Microsoft + "/media";

    /// <summary>Video referenced by a slide.</summary>
    public const string Video = Office + "/video";

    /// <summary>Audio referenced by a slide.</summary>
    public const string Audio = Office + "/audio";
}
