// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet;
using OfficeNet.Core;
using OfficeNet.Core.Documents;
using OfficeNet.Core.Packaging;
using OfficeNet.TestKit;
using Xunit;

namespace OfficeNet.Docs.Tests;

/// <summary>
/// The extensibility point, exercised by adding a format OfficeNet does not ship.
/// </summary>
/// <remarks>
/// <para>
/// The spec asks for plugins with VisioNet and OneNoteNet as the examples. What that needs is a
/// registry the facade consults, not those two libraries — so this proves the registry by teaching
/// <see cref="Office"/> a real new format end to end.
/// </para>
/// <para>
/// The format used here is a genuine OPC package with its own content type, which is exactly the
/// shape <c>.vsdx</c> and <c>.one</c> have: the container is already handled by
/// <c>OfficeNet.Core.Packaging</c>, and only the document model would be new work.
/// </para>
/// </remarks>
public class PluginRegistryTests : IDisposable
{
    private const string SketchContentType = "application/vnd.gravicode.sketch+xml";
    private static readonly OpcPartName SketchPartName = "/sketch/document.xml";

    public PluginRegistryTests() => OfficeFormats.Clear();

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        // The registry is process-wide, so a test that leaves a handler behind changes the result
        // of every test after it.
        OfficeFormats.Clear();
    }

    // ---- A minimal third-party format ----------------------------------------------------------

    /// <summary>A stand-in for VisioNet or OneNoteNet: an OPC package with its own content type.</summary>
    private sealed class SketchDocument : OfficeDocument
    {
        private SketchDocument(OpcPackage package) : base(package)
        {
        }

        public static SketchDocument Create(string text)
        {
            var package = OpcPackage.Create();

            var part = package.AddXmlPart(SketchPartName, SketchContentType,
                new System.Xml.Linq.XDocument(
                    new System.Xml.Linq.XElement("sketch", text)));

            package.AddRootRelationship(part, RelationshipTypes.OfficeDocument);
            return new SketchDocument(package);
        }

        public static SketchDocument Open(Stream stream) => new(OpcPackage.Open(stream));

        public override string ExtractText() =>
            Package.MainDocumentPart?.Xml.Root?.Value ?? string.Empty;
    }

    private sealed class SketchHandler : IOfficeFormatHandler
    {
        public string Name => "Sketch";

        public IReadOnlyList<string> Extensions => [".sketchx"];

        public bool CanOpen(Stream stream)
        {
            // Looks inside rather than trusting the name, which is the contract the interface asks
            // for and the reason a renamed file still routes correctly.
            try
            {
                using var package = OpcPackage.Open(stream);
                return package.MainDocumentPart?.ContentType == SketchContentType;
            }
            catch (OfficeNetException)
            {
                return false;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        public IOfficeDocument Open(Stream stream) => SketchDocument.Open(stream);
    }

    private static string WriteSketch(string directory, string fileName, string text)
    {
        var path = Path.Combine(directory, fileName);

        using var document = SketchDocument.Create(text);
        document.Save(path);

        return path;
    }

    // ---- Registration --------------------------------------------------------------------------

    [Fact]
    public void RegisteringAHandlerAddsItsExtensions()
    {
        Assert.DoesNotContain(".sketchx", Office.SupportedExtensions);

        OfficeFormats.Register(new SketchHandler());

        Assert.Contains(".sketchx", Office.SupportedExtensions);
        Assert.True(Office.IsSupportedExtension("drawing.sketchx"));

        // The built-ins are untouched.
        Assert.Contains(".docx", Office.SupportedExtensions);
    }

    [Fact]
    public void RegisteringTwiceReplacesRatherThanDuplicates()
    {
        OfficeFormats.Register(new SketchHandler());
        OfficeFormats.Register(new SketchHandler());

        Assert.Single(OfficeFormats.Registered);
    }

    [Fact]
    public void AHandlerWithNoExtensionsIsRejected()
    {
        Assert.Throws<ArgumentException>(() => OfficeFormats.Register(new EmptyHandler()));
    }

    private sealed class EmptyHandler : IOfficeFormatHandler
    {
        public string Name => "Empty";

        public IReadOnlyList<string> Extensions => [];

        public bool CanOpen(Stream stream) => false;

        public IOfficeDocument Open(Stream stream) => throw new NotSupportedException();
    }

    [Fact]
    public void UnregisterRemovesIt()
    {
        OfficeFormats.Register(new SketchHandler());

        Assert.True(OfficeFormats.Unregister("sketch"));
        Assert.False(OfficeFormats.Unregister("sketch"));
        Assert.Empty(OfficeFormats.Registered);
    }

    // ---- Opening -------------------------------------------------------------------------------

    [Fact]
    public void OfficeOpenRoutesToTheHandler()
    {
        using var directory = new TempDirectory();
        var path = WriteSketch(directory.Path, "drawing.sketchx", "Halo dari plugin");

        OfficeFormats.Register(new SketchHandler());

        using var document = Office.Open(path);

        Assert.IsType<SketchDocument>(document);
        Assert.Equal("Halo dari plugin", document.ExtractText());
    }

    [Fact]
    public void OfficeExtractTextWorksThroughTheHandler()
    {
        using var directory = new TempDirectory();
        var path = WriteSketch(directory.Path, "drawing.sketchx", "Teks yang bisa diekstrak");

        OfficeFormats.Register(new SketchHandler());

        Assert.Equal("Teks yang bisa diekstrak", Office.ExtractText(path));
    }

    [Fact]
    public void ContentDecidesTheRouting_NotTheExtension()
    {
        // The whole reason CanOpen exists. A sketch named .docx must still reach the handler, and
        // a real .docx named .sketchx must not.
        using var directory = new TempDirectory();
        var mislabelled = WriteSketch(directory.Path, "actually-a-sketch.docx", "Isi sketch");

        var word = Path.Combine(directory.Path, "actually-word.sketchx");

        using (var document = WordNet.WordDocument.Create())
        {
            document.AddParagraph("Isi Word");
            document.Save(word);
        }

        OfficeFormats.Register(new SketchHandler());

        Assert.Equal("Isi sketch", Office.ExtractText(mislabelled));

        using var opened = Office.Open(word);
        Assert.IsType<WordNet.WordDocument>(opened);
    }

    [Fact]
    public void AHandlerCannotInterceptABuiltInFormat()
    {
        // A handler that claims everything must still not take .docx: the built-ins are consulted
        // first, so adding a plugin can never change how existing files are read.
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "report.docx");

        using (var document = WordNet.WordDocument.Create())
        {
            document.AddParagraph("Isi Word");
            document.Save(path);
        }

        OfficeFormats.Register(new GreedyHandler());

        using var opened = Office.Open(path);
        Assert.IsType<WordNet.WordDocument>(opened);
    }

    private sealed class GreedyHandler : IOfficeFormatHandler
    {
        public string Name => "Greedy";

        public IReadOnlyList<string> Extensions => [".docx", ".anything"];

        public bool CanOpen(Stream stream) => true;

        public IOfficeDocument Open(Stream stream) =>
            throw new InvalidOperationException("A plugin must not be reached for a built-in format.");
    }

    [Fact]
    public void AHandlerThatThrowsWhileSniffingIsSkipped()
    {
        // One misbehaving plugin must not break detection for the others.
        using var directory = new TempDirectory();
        var path = WriteSketch(directory.Path, "drawing.sketchx", "Masih terbaca");

        OfficeFormats.Register(new ThrowingHandler());
        OfficeFormats.Register(new SketchHandler());

        Assert.Equal("Masih terbaca", Office.ExtractText(path));
    }

    private sealed class ThrowingHandler : IOfficeFormatHandler
    {
        public string Name => "Throwing";

        public IReadOnlyList<string> Extensions => [".sketchx"];

        public bool CanOpen(Stream stream) => throw new InvalidOperationException("boom");

        public IOfficeDocument Open(Stream stream) => throw new NotSupportedException();
    }

    [Fact]
    public void AnUnclaimedFileSaysHowManyHandlersWereAsked()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "notes.txt");
        File.WriteAllText(path, "not a document");

        var withoutPlugins = Assert.Throws<OfficeNetException>(() => Office.Open(path));
        Assert.Contains("OfficeFormats.Register", withoutPlugins.Message, StringComparison.Ordinal);

        OfficeFormats.Register(new SketchHandler());

        var withPlugins = Assert.Throws<OfficeNetException>(() => Office.Open(path));
        Assert.Contains("1 plugin handler", withPlugins.Message, StringComparison.Ordinal);
    }
}
