// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core;
using PdfNet.Text;
using WordNet.Export;
using WordNet.Styles;
using Xunit;

namespace WordNet.Tests;

/// <summary>
/// Covers the style chain the PDF export walks to resolve a run's formatting.
/// </summary>
/// <remarks>
/// The export caches each chain, because a document has a handful of styles and a great many
/// paragraphs, and every walk used to rescan the whole style part once per link. Caching is only
/// safe if the chain it caches is the chain the walk would have produced, so these pin the two
/// properties that are easy to lose: inheritance runs root first, and a malformed chain that
/// points back at itself terminates instead of hanging.
/// </remarks>
public class StyleChainTests
{
    private static IReadOnlyList<TextFragment> Fragments(WordDocument document)
    {
        using var pdf = WordToPdf.Convert(document);
        return TextExtractor.ExtractFragments(pdf.Pages[0]);
    }

    [Fact]
    public void ANearerStyleOverridesTheOneItIsBasedOn()
    {
        using var document = WordDocument.Create();

        var root = document.Styles.Add("Dasar", "Dasar", StyleType.Paragraph, basedOn: null);
        root.RunFormat.FontSize = Length.FromPoints(30);
        root.RunFormat.Bold = true;

        var middle = document.Styles.Add("Tengah", "Tengah", StyleType.Paragraph, basedOn: "Dasar");
        middle.RunFormat.FontSize = Length.FromPoints(20);

        var leaf = document.Styles.Add("Daun", "Daun", StyleType.Paragraph, basedOn: "Tengah");
        leaf.RunFormat.FontSize = Length.FromPoints(10);

        document.AddParagraph("Laporan.").StyleId = "Daun";

        var fragment = Fragments(document)[0];

        // The nearest size wins; the bold, defined only at the root, is still inherited.
        Assert.Equal(10, fragment.FontSize, 1);
        Assert.Contains("Bold", fragment.FontName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSameChainResolvesTheSameWayForEveryParagraphThatUsesIt()
    {
        // The cache is keyed by style id and filled on the first paragraph that needs it. If it
        // ever returned a chain built for a different id, the second paragraph would come out
        // formatted like the first.
        using var document = WordDocument.Create();

        var big = document.Styles.Add("Besar", "Besar", StyleType.Paragraph, basedOn: null);
        big.RunFormat.FontSize = Length.FromPoints(24);

        var small = document.Styles.Add("Kecil", "Kecil", StyleType.Paragraph, basedOn: null);
        small.RunFormat.FontSize = Length.FromPoints(8);

        foreach (var id in new[] { "Besar", "Kecil", "Besar", "Kecil", "Besar" })
        {
            document.AddParagraph($"Baris {id}.").StyleId = id;
        }

        var sizes = Fragments(document).Select(f => Math.Round(f.FontSize, 1)).ToList();

        Assert.Equal([24d, 8d, 24d, 8d, 24d], sizes);
    }

    [Fact]
    public void AStyleChainThatPointsBackAtItselfTerminates()
    {
        // Malformed, but conversion tools produce it. Following it forever hangs the export, and a
        // cache that stored a partial walk would hang only on the first document to hit it.
        using var document = WordDocument.Create();

        var first = document.Styles.Add("Satu", "Satu", StyleType.Paragraph, basedOn: null);
        first.RunFormat.FontSize = Length.FromPoints(14);

        var second = document.Styles.Add("Dua", "Dua", StyleType.Paragraph, basedOn: "Satu");

        first.BasedOn = "Dua";

        document.AddParagraph("Melingkar.").StyleId = "Dua";
        document.AddParagraph("Melingkar lagi.").StyleId = "Dua";

        var fragments = Fragments(document);

        Assert.Equal(2, fragments.Count);
        Assert.Equal(14, fragments[0].FontSize, 1);
        Assert.Equal(14, fragments[1].FontSize, 1);
    }
}
