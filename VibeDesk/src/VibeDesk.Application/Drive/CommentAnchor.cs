namespace VibeDesk.Application.Drive;

/// <summary>
/// Parsed form of <c>Comment.Anchor</c>. The anchor is app-specific JSON, so parsing lives in
/// one place rather than being re-derived by every caller.
/// </summary>
public readonly record struct CommentAnchor(
    int? From, int? To, int? SheetIndex, string? A1, int? SlideIndex, string? ElementId)
{
    public static CommentAnchor Parse(string? anchor)
    {
        if (string.IsNullOrWhiteSpace(anchor)) return default;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(anchor);
            var root = document.RootElement;

            return new CommentAnchor(
                TryInt(root, "from"),
                TryInt(root, "to"),
                TryInt(root, "sheet"),
                TryString(root, "a1"),
                TryInt(root, "slide"),
                TryString(root, "element"));
        }
        catch (System.Text.Json.JsonException)
        {
            return default;
        }
    }

    private static int? TryInt(System.Text.Json.JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;

    private static string? TryString(System.Text.Json.JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;
}
