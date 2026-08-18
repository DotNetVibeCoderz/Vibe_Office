namespace VibeDesk.Cli;

/// <summary>
/// Terminal writing in one place, so colour is applied consistently and only where a terminal is
/// actually attached — piping <c>vibedesk list</c> into another tool should not deliver escape codes.
/// </summary>
public static class Output
{
    private static readonly bool Colour =
        !Console.IsOutputRedirected &&
        Environment.GetEnvironmentVariable("NO_COLOR") is null;

    public static void Write(string text) => Console.WriteLine(text);

    public static void Heading(string text) => Console.WriteLine(Paint(text, "\u001b[1m"));

    public static void Muted(string text) => Console.WriteLine(Paint(text, "\u001b[90m"));

    public static void Success(string text) => Console.WriteLine(Paint("✓ " + text, "\u001b[32m"));

    public static void Hint(string text) => Console.Error.WriteLine(Paint(text, "\u001b[90m"));

    public static void Error(string text) => Console.Error.WriteLine(Paint("✗ " + text, "\u001b[31m"));

    public static string Status(string status) => status switch
    {
        "Succeeded" => Paint(status, "\u001b[32m"),
        "Failed" => Paint(status, "\u001b[31m"),
        "TimedOut" => Paint(status, "\u001b[33m"),
        "Refused" => Paint(status, "\u001b[35m"),
        _ => status,
    };

    /// <summary>Pads to a column width by display length, ignoring any escape codes already applied.</summary>
    public static string Pad(string text, int width)
    {
        var visible = Visible(text);

        return visible >= width ? text : text + new string(' ', width - visible);
    }

    private static int Visible(string text)
    {
        var count = 0;
        var escaped = false;

        foreach (var c in text)
        {
            if (escaped) { if (c == 'm') escaped = false; continue; }
            if (c == '\u001b') { escaped = true; continue; }
            count++;
        }

        return count;
    }

    private static string Paint(string text, string code) => Colour ? code + text + "\u001b[0m" : text;
}
