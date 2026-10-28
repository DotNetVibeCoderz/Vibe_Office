// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core.Xml;

namespace WordNet;

/// <summary>
/// Finds the runs a paragraph actually shows, wherever they are nested.
/// </summary>
/// <remarks>
/// <para>
/// A paragraph's direct <c>w:r</c> children are not all of its runs. Runs also sit inside a
/// hyperlink, a tracked insertion (<c>w:ins</c>), the destination of a move (<c>w:moveTo</c>), a
/// content control (<c>w:sdt</c> → <c>w:sdtContent</c>), a simple field, a smart tag and a custom
/// XML element — and those wrappers nest. Reading only direct children and hyperlinks, as this
/// library used to, drops every tracked insertion from the text, the PDF and the HTML.
/// </para>
/// <para>
/// What it yields is the <em>final</em> view: the document as it reads with every revision
/// accepted. A deletion (<c>w:del</c>) and the source of a move (<c>w:moveFrom</c>) are skipped,
/// because their runs are text that has been taken out. Every consumer goes through this one walker
/// so that the text, the PDF and the HTML can never disagree about what a paragraph says.
/// </para>
/// </remarks>
internal static class ParagraphContent
{
    private static readonly XName R = Ns.W + "r";
    private static readonly XName Hyperlink = Ns.W + "hyperlink";
    private static readonly XName SdtContent = Ns.W + "sdtContent";

    /// <summary>Wrappers whose runs are part of the text.</summary>
    private static readonly HashSet<XName> Transparent =
    [
        Hyperlink, Ns.W + "ins", Ns.W + "moveTo", Ns.W + "smartTag", Ns.W + "fldSimple",
        Ns.W + "customXml", Ns.W + "sdt", SdtContent, Ns.W + "dir", Ns.W + "bdo",
    ];

    /// <summary>The runs shown, each with the hyperlink it sits in, if any.</summary>
    internal static IEnumerable<(XElement Run, XElement? Hyperlink)> Runs(XElement paragraph) =>
        Walk(paragraph, null);

    private static IEnumerable<(XElement Run, XElement? Hyperlink)> Walk(XElement container,
        XElement? hyperlink)
    {
        foreach (var child in container.Elements())
        {
            if (child.Name == R)
            {
                yield return (child, hyperlink);
            }
            else if (child.Name.Namespace == Ns.W && Transparent.Contains(child.Name))
            {
                // An sdt's properties and end-properties hold no text; only sdtContent does.
                if (child.Name == Ns.W + "sdt")
                {
                    if (child.Element(SdtContent) is { } content)
                    {
                        foreach (var item in Walk(content, hyperlink))
                        {
                            yield return item;
                        }
                    }

                    continue;
                }

                foreach (var item in Walk(child, child.Name == Hyperlink ? child : hyperlink))
                {
                    yield return item;
                }
            }
        }
    }
}
