// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core.Documents;

namespace OfficeNet;

/// <summary>
/// A document format OfficeNet did not ship with.
/// </summary>
/// <remarks>
/// <para>
/// Implement this to teach <see cref="Office"/> a new format — Visio's <c>.vsdx</c> and OneNote's
/// <c>.one</c> are the obvious candidates, and both are OPC packages, so
/// <c>OfficeNet.Core.Packaging</c> already handles the container and only the document model is
/// new work.
/// </para>
/// <para>
/// A handler is asked two separate questions, and they are not the same one:
/// <see cref="Extensions"/> is a cheap guess used for filtering a folder listing, while
/// <see cref="CanOpen"/> looks inside and is what actually decides. Extensions lie — a renamed
/// download is the normal case, not the exotic one — so never let a handler claim a file on its
/// name alone.
/// </para>
/// </remarks>
public interface IOfficeFormatHandler
{
    /// <summary>A short name for the format, unique among registered handlers. For example "Visio".</summary>
    string Name { get; }

    /// <summary>The extensions this format usually carries, each including the dot.</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>
    /// Decides whether this handler can open the content, by looking at it.
    /// </summary>
    /// <param name="stream">
    /// Positioned at the start and seekable. Leave the position wherever you like; the caller
    /// restores it.
    /// </param>
    bool CanOpen(Stream stream);

    /// <summary>Opens the document.</summary>
    IOfficeDocument Open(Stream stream);
}

/// <summary>
/// The registry of formats beyond the four OfficeNet ships.
/// </summary>
/// <remarks>
/// <para>
/// Registration is process-wide and explicit — there is no assembly scanning. Scanning would make
/// the set of supported formats depend on which assemblies happened to be loaded, which turns a
/// missing format into a mystery instead of a missing line of code.
/// </para>
/// <para>
/// Built-in formats always win. A handler cannot claim <c>.docx</c> out from under WordNet, so
/// adding a plugin can never change how existing files are read.
/// </para>
/// </remarks>
public static class OfficeFormats
{
    private static readonly Lock Gate = new();
    private static readonly List<IOfficeFormatHandler> Handlers = [];

    /// <summary>The handlers registered so far, in registration order.</summary>
    public static IReadOnlyList<IOfficeFormatHandler> Registered
    {
        get
        {
            lock (Gate)
            {
                return [.. Handlers];
            }
        }
    }

    /// <summary>Registers a handler, replacing any earlier one with the same name.</summary>
    /// <exception cref="ArgumentException">The handler declares no extensions.</exception>
    public static void Register(IOfficeFormatHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(handler.Name);

        if (handler.Extensions.Count == 0)
        {
            throw new ArgumentException(
                $"Handler '{handler.Name}' declares no extensions, so nothing would ever route to " +
                "it.", nameof(handler));
        }

        lock (Gate)
        {
            // Replace rather than append: registering twice is what happens when a host reloads,
            // and two handlers for one format would make the winner depend on ordering.
            Handlers.RemoveAll(h => string.Equals(h.Name, handler.Name, StringComparison.OrdinalIgnoreCase));
            Handlers.Add(handler);
        }
    }

    /// <summary>Removes a handler by name.</summary>
    /// <returns>True when one was registered under that name.</returns>
    public static bool Unregister(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (Gate)
        {
            return Handlers.RemoveAll(
                h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
        }
    }

    /// <summary>Removes every handler. Intended for tests.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Handlers.Clear();
        }
    }

    /// <summary>Every extension any registered handler claims.</summary>
    public static IEnumerable<string> Extensions =>
        Registered.SelectMany(h => h.Extensions).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>The handler that claims this extension, if any.</summary>
    /// <remarks>
    /// Only a hint. Use <see cref="FindByContent"/> to decide what a file actually is.
    /// </remarks>
    public static IOfficeFormatHandler? FindByExtension(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var extension = Path.GetExtension(path);

        return extension.Length == 0
            ? null
            : Registered.FirstOrDefault(
                h => h.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The handler that can actually open this content.</summary>
    public static IOfficeFormatHandler? FindByContent(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            throw new ArgumentException(
                "Detection rewinds the stream, so it must be seekable. Copy it to a MemoryStream " +
                "first.", nameof(stream));
        }

        var start = stream.Position;

        foreach (var handler in Registered)
        {
            stream.Position = start;

            try
            {
                if (handler.CanOpen(stream))
                {
                    stream.Position = start;
                    return handler;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // A handler that throws while sniffing has disqualified itself. Letting the
                // exception out would mean one bad plugin breaks detection for every format.
            }
        }

        stream.Position = start;
        return null;
    }
}
