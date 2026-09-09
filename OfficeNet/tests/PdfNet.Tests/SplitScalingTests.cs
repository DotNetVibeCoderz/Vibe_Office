// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using PdfNet.Content;
using PdfNet.Document;
using PdfNet.Text;
using Xunit;

namespace PdfNet.Tests;

/// <summary>
/// Guards the regression where splitting a document copied all of it into every part.
/// </summary>
/// <remarks>
/// <para>
/// The bug produced <em>correct</em> output — every part held the right page and rendered
/// identically — so no assertion about content could see it. What it also held was every other page
/// in the source, orphaned: a 100-page file split into 100 parts gave 100 files of the original's
/// size, and the split itself was quadratic.
/// </para>
/// <para>
/// So these assert object counts and sizes rather than time. Both are deterministic, neither varies
/// with what else the machine is doing, and both go wrong the moment a part starts carrying the
/// whole document again.
/// </para>
/// </remarks>
public class SplitScalingTests
{
    /// <summary>
    /// Builds a document and reopens it from its own bytes.
    /// </summary>
    /// <remarks>
    /// The round trip is not incidental — it is what makes the bug visible at all. A page only gains
    /// its <c>/Parent</c> when the page tree is written, so a document built in memory and never
    /// saved has no parent link for the import to follow, and every assertion here passes against
    /// the broken code. Opening a file is also what a caller actually does before splitting one.
    /// </remarks>
    private static PdfDocument Build(int pages)
    {
        using var built = PdfDocument.Create();

        for (var p = 0; p < pages; p++)
        {
            var page = built.Pages.Add(PageSize.A4);

            using var canvas = page.OpenCanvas();
            canvas.SetFont(StandardFont.Helvetica, 11);

            for (var line = 0; line < 10; line++)
            {
                canvas.DrawText($"Halaman {p} baris {line}.", 56, 700 - (line * 18));
            }
        }

        return PdfDocument.Open(new MemoryStream(Save(built), writable: false));
    }

    private static byte[] Save(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void APartHoldsOnePageAndNotTheWholeDocument()
    {
        // The one that matters. A part of a 60-page document held 185 objects before this; a page
        // needs about six.
        using var source = Build(60);
        var parts = source.Split();

        try
        {
            Assert.All(parts, part => Assert.True(part.ObjectCount < 20,
                $"A one-page part carries {part.ObjectCount} objects, which is the whole document " +
                "coming along through the page's /Parent again."));
        }
        finally
        {
            foreach (var part in parts)
            {
                part.Dispose();
            }
        }
    }

    [Fact]
    public void APartDoesNotGrowWithTheDocumentItCameFrom()
    {
        // Object count is the signal, because it does not depend on the machine. A part of a
        // ten-page document and a part of a hundred-page one hold the same one page, so they must
        // hold the same number of objects.
        static int ObjectsInFirstPart(int pages)
        {
            using var source = Build(pages);
            var parts = source.Split();

            try
            {
                return parts[0].ObjectCount;
            }
            finally
            {
                foreach (var part in parts)
                {
                    part.Dispose();
                }
            }
        }

        Assert.Equal(ObjectsInFirstPart(10), ObjectsInFirstPart(100));
    }

    [Fact]
    public void APartIsFarSmallerThanTheDocumentItCameFrom()
    {
        using var source = Build(50);

        var whole = Save(source).Length;
        var parts = source.Split();

        try
        {
            var part = Save(parts[0]).Length;

            // A fiftieth of the pages should not be most of the bytes. Generous, because a part
            // repeats the font and the trailer; the bug made it 100%.
            Assert.True(part < whole / 5,
                $"A one-page part is {part} bytes of the source's {whole}.");
        }
        finally
        {
            foreach (var part in parts)
            {
                part.Dispose();
            }
        }
    }

    [Fact]
    public void EachPartCarriesItsOwnPageAndNoOtherPagesText()
    {
        // The other half of the guard: making parts smaller must not make them wrong. A fix that
        // dropped the page's own content would satisfy every size assertion above.
        using var source = Build(12);
        var parts = source.Split();

        try
        {
            for (var i = 0; i < parts.Count; i++)
            {
                using var reopened = PdfDocument.Open(
                    new MemoryStream(Save(parts[i]), writable: false));

                var text = TextExtractor.Extract(reopened.Pages[0]);

                Assert.Single(reopened.Pages);
                Assert.Contains($"Halaman {i} baris 0.", text, StringComparison.Ordinal);

                for (var other = 0; other < parts.Count; other++)
                {
                    if (other != i)
                    {
                        Assert.DoesNotContain($"Halaman {other} baris 0.", text,
                            StringComparison.Ordinal);
                    }
                }
            }
        }
        finally
        {
            foreach (var part in parts)
            {
                part.Dispose();
            }
        }
    }

    [Fact]
    public void AnInheritedPageSizeSurvivesTheSplit()
    {
        // /MediaBox usually lives on the page tree, not the page — and the page tree is exactly what
        // is no longer imported. Materialising the inherited attributes is what keeps this true, and
        // it is the thing most likely to break in a fix aimed at the parent link.
        using var source = PdfDocument.Create();

        source.Pages.Add(PageSize.A5);
        source.Pages.Add(PageSize.A5);
        source.Pages.Add(PageSize.A5);

        var parts = source.Split();

        try
        {
            foreach (var part in parts)
            {
                using var reopened = PdfDocument.Open(
                    new MemoryStream(Save(part), writable: false));

                Assert.Equal(PageSize.A5.Width, reopened.Pages[0].MediaBox.Width, 1);
                Assert.Equal(PageSize.A5.Height, reopened.Pages[0].MediaBox.Height, 1);
            }
        }
        finally
        {
            foreach (var part in parts)
            {
                part.Dispose();
            }
        }
    }

    [Fact]
    public void MergingStillSharesWhatTheSourcesShared()
    {
        // Merge goes through the same import path. Two copies of one document must not cost twice
        // the objects: the map is what makes shared structure survive, and a fix to the parent link
        // must not disturb it.
        using var first = Build(10);
        using var second = Build(10);

        var singleObjects = first.ObjectCount;

        first.Merge(second);

        Assert.Equal(20, first.Pages.Count);

        // Ten more pages cost ten more pages' worth of objects, not a second whole document tree.
        Assert.True(first.ObjectCount < singleObjects * 2.4,
            $"Merging doubled a {singleObjects}-object document to {first.ObjectCount}.");
    }
}
