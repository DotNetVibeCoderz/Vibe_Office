using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VibeDesk.Application.Documents;

namespace VibeDesk.Office;

/// <summary>
/// The one implementation of <see cref="IOfficeConverter"/>, dispatching to the per-format converters.
/// </summary>
public sealed class OfficeConverter : IOfficeConverter
{
    /// <summary>
    /// Extension to format. The extension decides, not the content type: browsers send
    /// <c>application/octet-stream</c> for these files often enough that trusting the header would
    /// mean refusing legitimate uploads.
    /// </summary>
    private static readonly Dictionary<string, OfficeFormat> ByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".docx"] = OfficeFormat.Word,
            [".docm"] = OfficeFormat.Word,
            [".xlsx"] = OfficeFormat.Excel,
            [".xlsm"] = OfficeFormat.Excel,
            [".pptx"] = OfficeFormat.PowerPoint,
            [".pptm"] = OfficeFormat.PowerPoint,
        };

    /// <summary>
    /// The legacy binary formats, refused deliberately. They are not OOXML at all, the SDK cannot
    /// read them, and letting one through as a "document" would produce a file full of mojibake
    /// rather than an honest "this stays as an attachment".
    /// </summary>
    private static readonly string[] LegacyExtensions = [".doc", ".xls", ".ppt"];

    public OfficeFormat? Detect(string fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName);

        if (LegacyExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return null;

        return ByExtension.TryGetValue(extension, out var format) ? format : null;
    }

    public DocumentModel ReadWord(Stream source) => WordConverter.Read(source);

    public SpreadsheetModel ReadExcel(Stream source) => ExcelConverter.Read(source);

    public PresentationModel ReadPowerPoint(Stream source) => PowerPointConverter.Read(source);

    public void WriteWord(Stream destination, DocumentModel model, string title) =>
        WordWriter.Write(destination, model, title);

    public void WriteExcel(Stream destination, SpreadsheetModel model) =>
        ExcelConverter.Write(destination, model);

    public void WritePowerPoint(Stream destination, PresentationModel model, string title) =>
        PowerPointConverter.Write(destination, model, title);
}

public static class DependencyInjection
{
    /// <summary>Registers Office import and export. Stateless, so one instance serves every request.</summary>
    public static IServiceCollection AddVibeDeskOffice(this IServiceCollection services)
    {
        services.TryAddSingleton<IOfficeConverter, OfficeConverter>();

        return services;
    }
}
