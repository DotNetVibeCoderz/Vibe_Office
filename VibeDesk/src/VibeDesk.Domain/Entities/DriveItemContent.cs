namespace VibeDesk.Domain.Entities;

/// <summary>
/// The editable payload of a Docs/Sheets/Slides item, split 1:1 off <see cref="DriveItem"/> so that
/// folder listings, search and permission checks never drag the (potentially large) body along.
/// </summary>
/// <remarks>
/// <see cref="Data"/> is provider-agnostic JSON rather than a normalised cell/paragraph table.
/// A spreadsheet with 10k populated cells is one row here instead of 10k, which is the difference
/// between a snappy editor and one that spends its time in the change tracker.
/// Shapes are defined by the <c>VibeDesk.Application.Documents.Models</c> records.
/// </remarks>
public class DriveItemContent
{
    public Guid DriveItemId { get; set; }
    public DriveItem? DriveItem { get; set; }

    /// <summary>JSON document whose schema depends on <see cref="DriveItem.Type"/>.</summary>
    public string Data { get; set; } = "{}";

    /// <summary>Plain-text projection of <see cref="Data"/>, kept for search and AI grounding.</summary>
    public string? PlainText { get; set; }

    /// <summary>
    /// Monotonic revision counter incremented on every accepted edit. Collaborative clients send the
    /// revision they based their change on; the hub rejects anything older to avoid lost updates.
    /// </summary>
    public long Revision { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedById { get; set; }
}
