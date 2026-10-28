// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace PdfNet.Fonts;

/// <summary>
/// Finds TrueType fonts by family and style in folders on disk — the resolver an exporter needs to
/// embed fonts, for PDF/A or for text outside Latin-1.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is read until the first lookup, and then each file once: its family name, weight and
/// italic angle say what it is. A font whose licence forbids embedding is skipped, because
/// embedding it anyway would make a file its recipient may not keep.
/// </para>
/// <para>
/// A style the folder does not have falls back to the family's regular face, as a word processor
/// does; a family it does not have at all is <c>null</c>, for the caller to decide about.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var fonts = FontDirectory.System();
/// var options = new PdfExportOptions { Conformance = PdfConformance.PdfA2B, FontResolver = fonts.Resolve };
/// </code>
/// </example>
public sealed class FontDirectory
{
    private readonly string[] _folders;
    private Dictionary<(string Family, bool Bold, bool Italic), string>? _faces;

    public FontDirectory(params string[] folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        _folders = folders;
    }

    /// <summary>The operating system's own font folders, those that exist.</summary>
    public static FontDirectory System()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] candidates =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"),
            "/usr/share/fonts", "/usr/local/share/fonts", Path.Combine(home, ".fonts"), Path.Combine(home, ".local", "share", "fonts"),
            "/System/Library/Fonts", "/Library/Fonts", Path.Combine(home, "Library", "Fonts"),
        ];

        return new FontDirectory([.. candidates.Where(f => f.Length > 0 && Directory.Exists(f)).Distinct()]);
    }

    /// <summary>The families found, for showing a choice or explaining a miss.</summary>
    public IReadOnlyList<string> Families => [.. Faces().Keys.Select(k => k.Family).Distinct().Order()];

    /// <summary>
    /// The bytes of the face for a family and style, or <c>null</c> when the family is not there.
    /// Its signature is what <c>FontResolver</c> options take.
    /// </summary>
    public byte[]? Resolve(string family, bool bold, bool italic)
    {
        ArgumentNullException.ThrowIfNull(family);

        var faces = Faces();
        var key = family.Trim().ToLowerInvariant();

        foreach (var candidate in new[] { (key, bold, italic), (key, bold, false), (key, false, italic), (key, false, false) })
        {
            if (faces.TryGetValue(candidate, out var path))
            {
                return File.ReadAllBytes(path);
            }
        }

        return null;
    }

    private Dictionary<(string Family, bool Bold, bool Italic), string> Faces()
    {
        if (_faces is not null)
        {
            return _faces;
        }

        var faces = new Dictionary<(string, bool, bool), string>();

        foreach (var folder in _folders.Where(Directory.Exists))
        {
            IEnumerable<string> files;

            try
            {
                files = Directory.EnumerateFiles(folder, "*.ttf", SearchOption.AllDirectories)
                    .Order(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    var font = TrueTypeFont.Load(file);

                    if (font.EmbeddingRestricted)
                    {
                        continue;
                    }

                    // The first file found for a face wins, so a user's folder listed first can
                    // override the system's copy.
                    faces.TryAdd((font.FamilyName.Trim().ToLowerInvariant(), font.Weight >= 600, font.ItalicAngle != 0), file);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Not a font this library can read — a variable font, a damaged file. Skipped.
                }
            }
        }

        return _faces = faces;
    }
}
