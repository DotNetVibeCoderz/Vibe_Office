using VibeDesk.Domain;

namespace VibeDesk.Application.Documents;

/// <summary>The Office file formats VibeDesk converts to and from.</summary>
public enum OfficeFormat
{
    Word,
    Excel,
    PowerPoint,
}

/// <summary>
/// Converts between OOXML files and VibeDesk's own content models.
/// </summary>
/// <remarks>
/// <para>
/// Declared here so Drive's upload path and the export endpoint depend on the contract rather than
/// on the OpenXML SDK, which stays behind <c>VibeDesk.Office</c>.
/// </para>
/// <para>
/// <b>Conversion is lossy in both directions</b>, and deliberately so: VibeDesk's models describe a
/// document as HTML, a sparse cell map and a list of slide elements, none of which is OOXML. Text,
/// structure, formulas and basic formatting survive; the things that have no home in the target model
/// do not. See <c>docs/apps.md</c> for what is carried and what is dropped.
/// </para>
/// </remarks>
public interface IOfficeConverter
{
    /// <summary>
    /// The format of an uploaded file, or null when it is not something we can convert. Matches on
    /// the extension and the declared content type, so a correct file with a lazy content type
    /// (<c>application/octet-stream</c>, which browsers send often) is still recognised.
    /// </summary>
    OfficeFormat? Detect(string fileName, string? contentType);

    /// <summary>The Drive item type a format becomes once imported.</summary>
    static DriveItemType TargetType(OfficeFormat format) => format switch
    {
        OfficeFormat.Excel => DriveItemType.Spreadsheet,
        OfficeFormat.PowerPoint => DriveItemType.Presentation,
        _ => DriveItemType.Document,
    };

    DocumentModel ReadWord(Stream source);
    SpreadsheetModel ReadExcel(Stream source);
    PresentationModel ReadPowerPoint(Stream source);

    void WriteWord(Stream destination, DocumentModel model, string title);
    void WriteExcel(Stream destination, SpreadsheetModel model);
    void WritePowerPoint(Stream destination, PresentationModel model, string title);

    /// <summary>The file extension and MIME type a format is served as.</summary>
    static (string Extension, string ContentType) Descriptor(OfficeFormat format) => format switch
    {
        OfficeFormat.Excel => (".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
        OfficeFormat.PowerPoint => (".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
        _ => (".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
    };
}
