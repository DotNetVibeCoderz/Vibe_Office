// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;

namespace OfficeNet.Core.Xml;

/// <summary>The XML namespaces OOXML parts are written in.</summary>
/// <remarks>
/// <para>
/// Namespace URIs, not prefixes, are what identify an element. Word writes the WordprocessingML
/// namespace with the prefix <c>w</c>, but a producer is free to use any prefix or a default
/// namespace, so every lookup in this library goes through <see cref="XName"/> rather than through
/// a string like <c>"w:p"</c>.
/// </para>
/// <para>
/// The transitional and strict variants of ECMA-376 use different URIs for the main namespaces.
/// Office writes transitional by default and that is what these constants hold; a strict document
/// is detected on open and its namespaces are mapped before anything else looks at the tree.
/// </para>
/// </remarks>
public static class Ns
{
    /// <summary>WordprocessingML main namespace (prefix <c>w</c>).</summary>
    public static readonly XNamespace W =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>SpreadsheetML main namespace (prefix <c>x</c> or default).</summary>
    public static readonly XNamespace S =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>PresentationML main namespace (prefix <c>p</c>).</summary>
    public static readonly XNamespace P =
        "http://schemas.openxmlformats.org/presentationml/2006/main";

    /// <summary>DrawingML main namespace (prefix <c>a</c>).</summary>
    public static readonly XNamespace A =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    /// <summary>DrawingML chart namespace (prefix <c>c</c>).</summary>
    public static readonly XNamespace C =
        "http://schemas.openxmlformats.org/drawingml/2006/chart";

    /// <summary>DrawingML picture namespace (prefix <c>pic</c>).</summary>
    public static readonly XNamespace Pic =
        "http://schemas.openxmlformats.org/drawingml/2006/picture";

    /// <summary>DrawingML wordprocessing drawing namespace (prefix <c>wp</c>).</summary>
    public static readonly XNamespace Wp =
        "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    /// <summary>DrawingML spreadsheet drawing namespace (prefix <c>xdr</c>).</summary>
    public static readonly XNamespace Xdr =
        "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    /// <summary>Relationship reference namespace (prefix <c>r</c>) — the <c>r:id</c>/<c>r:embed</c> attributes.</summary>
    public static readonly XNamespace R =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>Shared document properties namespace (prefix <c>dc</c> extensions).</summary>
    public static readonly XNamespace Ep =
        "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";

    /// <summary>Custom document properties namespace.</summary>
    public static readonly XNamespace Cp =
        "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";

    /// <summary>Variant types used by both extended and custom properties (prefix <c>vt</c>).</summary>
    public static readonly XNamespace Vt =
        "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

    /// <summary>Package core properties namespace (prefix <c>cp</c>).</summary>
    public static readonly XNamespace CoreProps =
        "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";

    /// <summary>Dublin Core elements namespace (prefix <c>dc</c>).</summary>
    public static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    /// <summary>Dublin Core terms namespace (prefix <c>dcterms</c>).</summary>
    public static readonly XNamespace DcTerms = "http://purl.org/dc/terms/";

    /// <summary>XML Schema instance namespace (prefix <c>xsi</c>).</summary>
    public static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>The <c>xml:</c> namespace, for <c>xml:space="preserve"</c>.</summary>
    public static readonly XNamespace Xml = XNamespace.Xml;

    /// <summary>Markup Compatibility (prefix <c>mc</c>) — <c>AlternateContent</c> and friends.</summary>
    public static readonly XNamespace Mc =
        "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>VML drawing namespace (prefix <c>v</c>), still used for legacy header images and comments.</summary>
    public static readonly XNamespace V = "urn:schemas-microsoft-com:vml";

    /// <summary>Office VML extensions (prefix <c>o</c>).</summary>
    public static readonly XNamespace O = "urn:schemas-microsoft-com:office:office";

    /// <summary>Word VML extensions (prefix <c>w10</c>).</summary>
    public static readonly XNamespace W10 = "urn:schemas-microsoft-com:office:word";

    /// <summary>Word 2010 extensions (prefix <c>w14</c>).</summary>
    public static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    /// <summary>PowerPoint 2010 extensions (prefix <c>p14</c>).</summary>
    public static readonly XNamespace P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";

    /// <summary>Excel 2010 extensions (prefix <c>x14</c>) — data bars, icon sets, sparklines.</summary>
    public static readonly XNamespace X14 = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    /// <summary>Excel 2010 AC extensions (prefix <c>x14ac</c>).</summary>
    public static readonly XNamespace X14Ac = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac";

    /// <summary>DrawingML 2010 chart extensions (prefix <c>c14</c>).</summary>
    public static readonly XNamespace C14 = "http://schemas.microsoft.com/office/drawing/2007/8/2/chart";

    /// <summary>The ECMA-376 <em>strict</em> WordprocessingML namespace.</summary>
    public static readonly XNamespace WStrict = "http://purl.oclc.org/ooxml/wordprocessingml/main";

    /// <summary>The ECMA-376 <em>strict</em> SpreadsheetML namespace.</summary>
    public static readonly XNamespace SStrict = "http://purl.oclc.org/ooxml/spreadsheetml/main";

    /// <summary>The ECMA-376 <em>strict</em> PresentationML namespace.</summary>
    public static readonly XNamespace PStrict = "http://purl.oclc.org/ooxml/presentationml/main";

    /// <summary>The ECMA-376 <em>strict</em> DrawingML namespace.</summary>
    public static readonly XNamespace AStrict = "http://purl.oclc.org/ooxml/drawingml/main";

    /// <summary>The ECMA-376 <em>strict</em> relationships namespace.</summary>
    public static readonly XNamespace RStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships";

    /// <summary>
    /// Maps the strict namespaces onto the transitional ones this library works in.
    /// </summary>
    /// <remarks>
    /// A strict document is otherwise structurally identical, so rewriting the namespace on every
    /// element and attribute at load time is the whole conversion. Without it a strict .docx opens
    /// with zero paragraphs — every <c>w:p</c> lookup misses, silently, because the URI differs.
    /// </remarks>
    public static readonly IReadOnlyDictionary<XNamespace, XNamespace> StrictToTransitional =
        new Dictionary<XNamespace, XNamespace>
        {
            [WStrict] = W,
            [SStrict] = S,
            [PStrict] = P,
            [AStrict] = A,
            [RStrict] = R,
            ["http://purl.oclc.org/ooxml/drawingml/chart"] = C,
            ["http://purl.oclc.org/ooxml/drawingml/picture"] = Pic,
            ["http://purl.oclc.org/ooxml/drawingml/wordprocessingDrawing"] = Wp,
            ["http://purl.oclc.org/ooxml/drawingml/spreadsheetDrawing"] = Xdr,
            ["http://purl.oclc.org/ooxml/officeDocument/extendedProperties"] = Ep,
            ["http://purl.oclc.org/ooxml/officeDocument/customProperties"] = Cp,
            ["http://purl.oclc.org/ooxml/officeDocument/docPropsVTypes"] = Vt,
        };
}
