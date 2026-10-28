// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;

namespace WordNet;

/// <summary>
/// Reads and writes <c>word/settings.xml</c> in schema order.
/// </summary>
/// <remarks>
/// <c>CT_Settings</c> is a sequence like every other OOXML property bag. Elements used to be added
/// with <c>AddFirst</c>, on the belief that Word is tolerant here — which put <c>updateFields</c>
/// and <c>evenAndOddHeaders</c> ahead of <c>zoom</c>, and would have put <c>trackRevisions</c> and
/// <c>documentProtection</c> in the wrong place too. Everything now goes through
/// <see cref="XmlUtil.SetOrdered"/> with the full order.
/// </remarks>
internal static class DocumentSettings
{
    private static readonly OpcPartName PartName = "/word/settings.xml";

    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private static readonly XNamespace Sl = "http://schemas.openxmlformats.org/schemaLibrary/2006/main";

    /// <summary>The CT_Settings sequence, ECMA-376 Part 1 §17.15.1.78.</summary>
    internal static readonly XName[] Order =
    [
        .. new[]
        {
            "writeProtection", "view", "zoom", "removePersonalInformation", "removeDateAndTime",
            "doNotDisplayPageBoundaries", "displayBackgroundShape", "printPostScriptOverText",
            "printFractionalCharacterWidth", "printFormsData", "embedTrueTypeFonts",
            "embedSystemFonts", "saveSubsetFonts", "saveFormsData", "mirrorMargins",
            "alignBordersAndEdges", "bordersDoNotSurroundHeader", "bordersDoNotSurroundFooter",
            "gutterAtTop", "hideSpellingErrors", "hideGrammaticalErrors", "activeWritingStyle",
            "proofState", "formsDesign", "attachedTemplate", "linkStyles", "stylePaneFormatFilter",
            "stylePaneSortMethod", "documentType", "mailMerge", "revisionView", "trackRevisions",
            "doNotTrackMoves", "doNotTrackFormatting", "documentProtection", "autoFormatOverride",
            "styleLockTheme", "styleLockQFSet", "defaultTabStop", "autoHyphenation",
            "consecutiveHyphenLimit", "hyphenationZone", "doNotHyphenateCaps", "showEnvelope",
            "summaryLength", "clickAndTypeStyle", "defaultTableStyle", "evenAndOddHeaders",
            "bookFoldRevPrinting", "bookFoldPrinting", "bookFoldPrintingSheets",
            "drawingGridHorizontalSpacing", "drawingGridVerticalSpacing",
            "displayHorizontalDrawingGridEvery", "displayVerticalDrawingGridEvery",
            "doNotUseMarginsForDrawingGridOrigin", "drawingGridHorizontalOrigin",
            "drawingGridVerticalOrigin", "doNotShadeFormData", "noPunctuationKerning",
            "characterSpacingControl", "printTwoOnOne", "strictFirstAndLastChars",
            "noLineBreaksAfter", "noLineBreaksBefore", "savePreviewPicture",
            "doNotValidateAgainstSchema", "saveInvalidXml", "ignoreMixedContent",
            "alwaysShowPlaceholderText", "doNotDemarcateInvalidXml", "saveXmlDataOnly",
            "useXSLTWhenSaving", "saveThroughXslt", "showXMLTags", "alwaysMergeEmptyNamespace",
            "updateFields", "hdrShapeDefaults", "footnotePr", "endnotePr", "compat", "docVars",
            "rsids",
        }.Select(n => Ns.W + n),
        M + "mathPr",
        Ns.W + "attachedSchema",
        .. new[]
        {
            "themeFontLang", "clrSchemeMapping", "doNotIncludeSubdocsInStats",
            "doNotAutoCompressPictures", "forceUpgrade", "captions", "readModeInkLockDown",
            "smartTagType",
        }.Select(n => Ns.W + n),
        Sl + "schemaLibrary",
        .. new[] { "shapeDefaults", "doNotEmbedSmartTags", "decimalSymbol", "listSeparator" }
            .Select(n => Ns.W + n),
    ];

    /// <summary>The settings root, creating the part when the package has none.</summary>
    internal static XElement Root(WordDocument document)
    {
        var part = document.DocumentPart.RelatedPartByType(RelationshipTypes.Settings);

        if (part is null)
        {
            part = document.Package.AddXmlPart(PartName, ContentTypes.WordSettings,
                DefaultParts.Settings());
            document.DocumentPart.AddRelationship(part, RelationshipTypes.Settings);
        }

        return part.Xml.Root ?? throw new OfficeNet.Core.OfficeNetException("word/settings.xml is empty.");
    }

    /// <summary>The settings root when the part exists, without creating it.</summary>
    internal static XElement? Find(WordDocument document) =>
        document.DocumentPart.RelatedPartByType(RelationshipTypes.Settings)?.Xml.Root;

    /// <summary>Sets, replaces or (with <c>null</c>) removes one setting.</summary>
    internal static void Set(WordDocument document, XName name, XElement? element)
    {
        var root = element is null ? Find(document) : Root(document);

        if (root is null)
        {
            return;
        }

        XmlUtil.SetOrdered(root, name, element, Order);
        document.Touch();
    }

    /// <summary>Adds a setting when it is absent; leaves an existing one alone.</summary>
    internal static void Ensure(WordDocument document, XElement element)
    {
        var root = Root(document);

        if (root.Element(element.Name) is not null)
        {
            return;
        }

        XmlUtil.SetOrdered(root, element.Name, element, Order);
        document.Touch();
    }
}
