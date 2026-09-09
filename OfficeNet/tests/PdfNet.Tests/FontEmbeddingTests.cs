// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core;
using OfficeNet.TestKit;
using PdfNet.Content;
using PdfNet.Document;
using PdfNet.Fonts;
using PdfNet.Objects;
using PdfNet.Text;
using Xunit;

namespace PdfNet.Tests;

public class TrueTypeFontTests
{
    [Fact]
    public void AFontReadsBackTheMetricsItWasBuiltWith()
    {
        var font = TrueTypeFont.Load(SyntheticFont.Build("ABC"));

        Assert.Equal(1000, font.UnitsPerEm);
        Assert.Equal(800, font.Ascender);
        Assert.Equal(-200, font.Descender);
        Assert.Equal(700, font.CapHeight);
        Assert.Equal(400, font.Weight);
        Assert.False(font.EmbeddingRestricted);

        // .notdef plus one per character.
        Assert.Equal(4, font.GlyphCount);
    }

    [Fact]
    public void CharactersMapToGlyphsAndUnknownOnesMapToNotdef()
    {
        var font = TrueTypeFont.Load(SyntheticFont.Build("ABC"));

        Assert.Equal(1, font.GlyphFor('A'));
        Assert.Equal(2, font.GlyphFor('B'));
        Assert.Equal(3, font.GlyphFor('C'));

        // Glyph 0 is .notdef — the empty box a reader draws for a character the font lacks. Saying
        // so beats throwing: a document usually has one character the font cannot show.
        Assert.Equal(0, font.GlyphFor('Z'));
        Assert.Equal(0, font.GlyphFor('中'));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothGlyphOffsetFormatsAreRead(bool longLoca)
    {
        // The short format stores every offset halved. A reader that ignores indexToLocFormat gets
        // offsets wrong by a factor of two, and the font renders as a page of blanks.
        var font = TrueTypeFont.Load(SyntheticFont.Build("ABC", longLoca: longLoca));

        for (var glyph = 1; glyph <= 3; glyph++)
        {
            Assert.True(font.HasOutline(glyph), $"Glyph {glyph} came back with no outline.");
        }
    }

    [Fact]
    public void AMappingIsNotAnOutline()
    {
        // A space maps to a glyph, and that glyph is deliberately empty. Telling the two apart is
        // how a dropped outline is distinguished from a character the font simply does not have.
        var font = TrueTypeFont.Load(SyntheticFont.Build("A "));

        Assert.NotEqual(0, font.GlyphFor(' '));
        Assert.False(font.HasOutline(font.GlyphFor(' ')));
        Assert.True(font.HasOutline(font.GlyphFor('A')));
    }

    [Fact]
    public void WidthsAreScaledToThousandthsOfAnEm()
    {
        // A font in 2048 units per em and one in 1000 both have to come out in PDF's own units, or
        // every measurement in the document is out by the ratio between them.
        var font = TrueTypeFont.Load(SyntheticFont.Build("A"));

        // The synthetic font advances 500 + 3n at 1000/em, so glyph 1 is 503.
        Assert.Equal(503, font.AdvanceWidth(1));
        Assert.Equal(503, font.AdvanceWidthPdf(1));

        Assert.Equal(503 * 12 / 1000.0, font.MeasureText("A", 12), 6);
    }

    [Fact]
    public void SomethingThatIsNotAFontSaysSoUsefully()
    {
        var exception = Assert.Throws<OfficeNetException>(() =>
            TrueTypeFont.Load(new byte[] { 0x25, 0x50, 0x44, 0x46, 0, 0, 0, 0, 0, 0, 0, 0 }));

        Assert.Contains("not a TrueType font", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOpenTypeFontWithPostScriptOutlinesIsRefusedByName()
    {
        // "OTTO" marks CFF outlines, which need a different PDF font type and a different subsetter.
        // Loading it and producing a PDF with no glyphs would be worse than saying so.
        var otto = new byte[] { 0x4F, 0x54, 0x54, 0x4F, 0, 0, 0, 0, 0, 0, 0, 0 };

        var exception = Assert.Throws<OfficeNetException>(() => TrueTypeFont.Load(otto));

        Assert.Contains("CFF", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACollectionIsRefusedByName()
    {
        var ttcf = new byte[] { 0x74, 0x74, 0x63, 0x66, 0, 0, 0, 0, 0, 0, 0, 0 };

        var exception = Assert.Throws<OfficeNetException>(() => TrueTypeFont.Load(ttcf));

        Assert.Contains("collection", exception.Message, StringComparison.Ordinal);
    }
}

public class FontEmbeddingTests
{
    private static byte[] Draw(string text, out int glyphsUsed, out int pdfBytes)
    {
        using var stream = new MemoryStream();

        using (var pdf = PdfDocument.Create())
        {
            var font = pdf.EmbedFont(SyntheticFont.Build(SyntheticFont.DefaultCharacters, composite: 'É'));
            var page = pdf.Pages.Add(PageSize.A4);

            using (var canvas = page.OpenCanvas())
            {
                canvas.SetFont(font, 14);
                canvas.DrawText(text, 72, 700);
            }

            pdf.Save(stream);
            glyphsUsed = font.UsedGlyphCount;
        }

        var bytes = stream.ToArray();
        pdfBytes = bytes.Length;

        return bytes;
    }

    [Fact]
    public void TextWrittenWithAnEmbeddedFontComesBackOut()
    {
        // The whole point of the ToUnicode CMap. A composite font writes glyph ids, and without the
        // map a reader sees numbers and has nothing to turn them into — the text is unsearchable,
        // uncopyable, and gone.
        var bytes = Draw("Pendapatan naik 32 persen.", out _, out _);

        using var reopened = PdfDocument.Open(new MemoryStream(bytes, writable: false));

        Assert.Equal("Pendapatan naik 32 persen.",
            TextExtractor.Extract(reopened.Pages[0]).Trim());
    }

    [Fact]
    public void OnlyTheGlyphsUsedAreEmbedded()
    {
        Draw("AB", out var few, out var smallPdf);
        Draw(SyntheticFont.DefaultCharacters, out var many, out var largePdf);

        // A and B. .notdef is not counted, because nothing needed it — every character in the text
        // had a glyph — even though the subset carries it anyway.
        Assert.Equal(2, few);
        Assert.True(many > 30, $"Only {many} glyphs came through for the whole alphabet.");
        Assert.True(smallPdf < largePdf,
            $"Two glyphs gave {smallPdf} bytes against {largePdf} for the alphabet.");
    }

    [Fact]
    public void AComposedGlyphBringsItsComponentsWithIt()
    {
        // The case a subsetter gets wrong. A composite glyph is built from others by id, and a
        // subset that does not follow those references produces a font whose accented characters
        // are blank — with nothing anywhere to say why.
        using var stream = new MemoryStream();
        int used;

        using (var pdf = PdfDocument.Create())
        {
            var font = pdf.EmbedFont(SyntheticFont.Build("ABC", composite: 'É'));
            var page = pdf.Pages.Add(PageSize.A4);

            using (var canvas = page.OpenCanvas())
            {
                canvas.SetFont(font, 12);

                // Only the composite is drawn. Its two components have to come along anyway.
                canvas.DrawText("É", 72, 700);
            }

            pdf.Save(stream);
            used = font.UsedGlyphCount;
        }

        // One glyph was drawn: the composite itself.
        Assert.Equal(1, used);

        // But the subset holds four: .notdef, the composite, and the two glyphs it is built from.
        var subset = ExtractFontFile(stream.ToArray());
        var reparsed = TrueTypeFont.Load(subset);

        Assert.Equal(4, reparsed.GlyphCount);

        for (var glyph = 1; glyph < 4; glyph++)
        {
            Assert.True(reparsed.HasOutline(glyph), $"Glyph {glyph} of the subset has no outline.");
        }
    }

    [Fact]
    public void TheSubsetIsAFontThisLibraryCanReadBack()
    {
        // Not proof that a PDF reader accepts it, but it catches the structural mistakes: a wrong
        // table directory, a loca in the wrong format, a length that does not match.
        var bytes = Draw("Pendapatan naik", out _, out _);

        var reparsed = TrueTypeFont.Load(ExtractFontFile(bytes));

        Assert.Equal(1000, reparsed.UnitsPerEm);
        Assert.True(reparsed.GlyphCount > 1);
        // Not every glyph: the text has a space, and a space is deliberately empty. But most of
        // them, or the outlines did not survive the rebuild.
        var withOutlines = Enumerable.Range(1, reparsed.GlyphCount - 1).Count(reparsed.HasOutline);

        Assert.True(withOutlines >= reparsed.GlyphCount - 2,
            $"Only {withOutlines} of {reparsed.GlyphCount} subset glyphs have outlines.");
    }

    [Fact]
    public void TheFontIsAType0WithEverythingItNeeds()
    {
        var bytes = Draw("Halo", out _, out _);

        using var reopened = PdfDocument.Open(new MemoryStream(bytes, writable: false));

        var font = FindDictionary(reopened,
            d => d.GetName(PdfName.Subtype) == "Type0");

        Assert.NotNull(font);
        Assert.Equal("Identity-H", font.GetName(PdfName.Get("Encoding")));

        // Without ToUnicode the text cannot be decoded at all, which is why it is never optional.
        Assert.True(font.ContainsKey(PdfName.Get("ToUnicode")));

        var descendant = FindDictionary(reopened, d => d.GetName(PdfName.Subtype) == "CIDFontType2");

        Assert.NotNull(descendant);
        Assert.True(descendant.ContainsKey(PdfName.Get("W")));
        Assert.True(descendant.ContainsKey(PdfName.Get("CIDToGIDMap")));
        Assert.True(descendant.ContainsKey(PdfName.Get("FontDescriptor")));
    }

    [Fact]
    public void TheDescriptorSetsExactlyOneOfSymbolicAndNonsymbolic()
    {
        // Setting both, or neither, makes some readers fall back to a substitute font and ignore
        // the one embedded right there in the file.
        var bytes = Draw("Halo", out _, out _);

        using var reopened = PdfDocument.Open(new MemoryStream(bytes, writable: false));

        var descriptor = FindDictionary(reopened, d => d.GetName(PdfName.Type) == "FontDescriptor");

        Assert.NotNull(descriptor);

        var flags = descriptor.GetInt(PdfName.Get("Flags"));
        var symbolic = (flags & 4) != 0;
        var nonsymbolic = (flags & 32) != 0;

        Assert.True(symbolic ^ nonsymbolic, $"Flags {flags} set both or neither.");
    }

    [Fact]
    public void TheFontFileRecordsItsUncompressedLength()
    {
        // A reader that inflates the stream has no other way to know how much font it should have
        // got, and some validators reject a FontFile2 without it.
        var bytes = Draw("Halo", out _, out _);

        using var reopened = PdfDocument.Open(new MemoryStream(bytes, writable: false));

        var stream = FindStream(reopened, s => s.ContainsKey(PdfName.Get("Length1")));

        Assert.NotNull(stream);
        Assert.Equal(stream.Decoded.Length, stream.GetInt(PdfName.Get("Length1")));
    }

    [Fact]
    public void AnEmbeddedFontAndAStandardOneShareAPage()
    {
        // Switching back has to clear the embedded encoding, or the standard font's text is still
        // written as glyph ids for a font that is no longer selected — and draws as nothing.
        using var stream = new MemoryStream();

        using (var pdf = PdfDocument.Create())
        {
            var font = pdf.EmbedFont(SyntheticFont.Build());
            var page = pdf.Pages.Add(PageSize.A4);

            using (var canvas = page.OpenCanvas())
            {
                canvas.SetFont(font, 12);
                canvas.DrawText("Embedded line", 72, 700);

                canvas.SetFont(StandardFont.Helvetica, 12);
                canvas.DrawText("Standard line", 72, 680);
            }

            pdf.Save(stream);
        }

        using var reopened = PdfDocument.Open(new MemoryStream(stream.ToArray(), writable: false));
        var text = TextExtractor.Extract(reopened.Pages[0]);

        Assert.Contains("Embedded line", text, StringComparison.Ordinal);
        Assert.Contains("Standard line", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddingTheSameFontTwiceGivesOneCopy()
    {
        using var pdf = PdfDocument.Create();

        var bytes = SyntheticFont.Build();
        var first = pdf.EmbedFont(bytes);
        var second = pdf.EmbedFont((byte[])bytes.Clone());

        Assert.Same(first, second);
        Assert.Single(pdf.EmbeddedFonts);
    }

    [Fact]
    public void MissingCharactersAreReportedBeforeTheyBecomeEmptyBoxes()
    {
        using var pdf = PdfDocument.Create();
        var font = pdf.EmbedFont(SyntheticFont.Build("ABC"));

        Assert.True(font.CanRender("ABC"));
        Assert.False(font.CanRender("ABCD"));

        Assert.Equal(["D", "中"], font.MissingCharacters("ABCD中D"));
    }

    [Fact]
    public void MeasuringUsesTheEmbeddedFontOnceItIsSelected()
    {
        using var pdf = PdfDocument.Create();
        var font = pdf.EmbedFont(SyntheticFont.Build("AB"));
        var page = pdf.Pages.Add(PageSize.A4);

        using var canvas = page.OpenCanvas();

        canvas.SetFont(StandardFont.Helvetica, 10);
        var standard = canvas.MeasureText("AB");

        canvas.SetFont(font, 10);
        var embedded = canvas.MeasureText("AB");

        // The synthetic font advances 503 and 506 per thousand, which no standard face matches.
        Assert.Equal((503 + 506) * 10 / 1000.0, embedded, 6);
        Assert.NotEqual(standard, embedded, 3);
    }

    [Fact]
    public void APdfWithAnEmbeddedFontIsStillAValidPdf()
    {
        var bytes = Draw("Pendapatan naik 32 persen.", out _, out _);

        using var reopened = PdfDocument.Open(new MemoryStream(bytes, writable: false));

        Assert.Single(reopened.Pages);
        Assert.NotEmpty(TextExtractor.Extract(reopened.Pages[0]));
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static byte[] ExtractFontFile(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf, writable: false));

        var stream = FindStream(document, s => s.ContainsKey(PdfName.Get("Length1")));

        Assert.NotNull(stream);

        return stream.Decoded;
    }

    private static PdfDictionary? FindDictionary(PdfDocument document, Func<PdfDictionary, bool> match)
    {
        for (var number = 1; number <= document.ObjectCount + 20; number++)
        {
            if (document.Resolve(number, 0) is PdfDictionary dictionary &&
                dictionary is not PdfStream && match(dictionary))
            {
                return dictionary;
            }
        }

        return null;
    }

    private static PdfStream? FindStream(PdfDocument document, Func<PdfStream, bool> match)
    {
        for (var number = 1; number <= document.ObjectCount + 20; number++)
        {
            if (document.Resolve(number, 0) is PdfStream stream && match(stream))
            {
                return stream;
            }
        }

        return null;
    }
}
