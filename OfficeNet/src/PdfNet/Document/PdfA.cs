// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.RegularExpressions;
using OfficeNet.Core;
using PdfNet.Objects;

namespace PdfNet.Document;

/// <summary>An archival standard a document is written to.</summary>
public enum PdfConformance
{
    /// <summary>No claim beyond being a PDF.</summary>
    None,

    /// <summary>PDF/A-2b (ISO 19005-2), level B: the file reproduces its appearance exactly, for good.</summary>
    PdfA2B,

    /// <summary>
    /// PDF/A-3b (ISO 19005-3): PDF/A-2b, plus attachments of any kind — the form ZUGFeRD and
    /// Factur-X e-invoices take.
    /// </summary>
    PdfA3B,
}

public sealed partial class PdfDocument
{
    /// <summary>
    /// The archival standard to write the document to on save.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Setting it makes every save carry what PDF/A requires and a writer can supply: the
    /// identification in an XMP packet that agrees with <see cref="Info"/>, and an sRGB output
    /// intent that gives the file's colours a meaning. What a writer cannot supply is checked, and a
    /// save that would make a false claim throws instead — a font that is not embedded (the 14
    /// standard fonts never are), encryption, or under PDF/A-2 an attachment that is not itself a
    /// PDF.
    /// </para>
    /// <para>
    /// The claim is checked against veraPDF, the reference validator, in this library's tests.
    /// </para>
    /// </remarks>
    public PdfConformance Conformance { get; set; }

    /// <summary>Adds what PDF/A requires, or explains why this document cannot have it.</summary>
    private void ApplyConformance()
    {
        if (Conformance == PdfConformance.None)
        {
            return;
        }

        var problems = new List<string>();

        if (_encryptDictionary is not null)
        {
            problems.Add("it is encrypted, and PDF/A forbids encryption — call Decrypt() first");
        }

        var unembedded = UnembeddedFonts();

        if (unembedded.Fonts.Count > 0)
        {
            problems.Add($"these fonts are not embedded: {string.Join(", ", unembedded.Fonts)}. PDF/A requires every " +
                         "font's program in the file, and the 14 standard fonts never are — draw with EmbedFont() instead" +
                         (unembedded.InFields
                             ? ". Form fields are drawn in Helvetica unless AcroForm.Font is set to an embedded font before they are added or filled"
                             : string.Empty));
        }

        foreach (var font in _embeddedFonts.Where(f => f.MissingDrawnCharacters.Count > 0))
        {
            var characters = string.Join(" ", font.MissingDrawnCharacters.Select(c => $"{c} (U+{char.ConvertToUtf32(c, 0):X4})"));
            problems.Add($"{font.Font.FullName} has no glyph for {characters}, so it was drawn as the empty .notdef " +
                         "glyph, which PDF/A forbids — draw it in a font that covers it");
        }

        if (Conformance == PdfConformance.PdfA2B)
        {
            var foreign = Attachments.Where(a => a.MimeType != "application/pdf").Select(a => a.Name).ToList();

            if (foreign.Count > 0)
            {
                problems.Add($"PDF/A-2 allows only PDF attachments, and these are not: {string.Join(", ", foreign)}. " +
                             "PDF/A-3 allows any — use PdfConformance.PdfA3B");
            }
        }

        if (problems.Count > 0)
        {
            throw new OfficeNetException($"The document cannot be saved as {Name(Conformance)}: " +
                                         string.Join("; ", problems) + ".");
        }

        // PDF/A-2 and -3 are built on PDF 1.7.
        Version = "1.7";
        WriteXmpMetadata = true;
        PdfAIdentification = (Conformance == PdfConformance.PdfA3B ? 3 : 2, "B");

        EnsureOutputIntent();
        PrepareAnnotations();
    }

    private static string Name(PdfConformance conformance) => conformance switch
    {
        PdfConformance.PdfA2B => "PDF/A-2b",
        PdfConformance.PdfA3B => "PDF/A-3b",
        _ => conformance.ToString(),
    };

    /// <summary>
    /// The sRGB output intent, unless the document already has one — a file opened for
    /// conversion may carry its own, and replacing it would change what its colours mean.
    /// </summary>
    private void EnsureOutputIntent()
    {
        if (Catalog.GetArray(PdfName.Get("OutputIntents")).Count > 0)
        {
            return;
        }

        var profile = new PdfStream();
        profile[PdfName.Get("N")] = new PdfNumber(3);
        profile.SetDecoded(SrgbProfile.Bytes);

        var intent = new PdfDictionary();
        intent[PdfName.Type] = PdfName.Get("OutputIntent");
        intent[PdfName.Get("S")] = PdfName.Get("GTS_PDFA1");
        intent[PdfName.Get("OutputConditionIdentifier")] = new PdfString(SrgbProfile.Description);
        intent[PdfName.Get("Info")] = new PdfString(SrgbProfile.Description);
        intent[PdfName.Get("DestOutputProfile")] = AddObject(profile);

        Catalog[PdfName.Get("OutputIntents")] = new PdfArray { AddObject(intent) };
    }

    /// <summary>
    /// Annotations must print, and a form must not ask the viewer to draw its own fields — the
    /// appearance has to be in the file, or it is not fixed.
    /// </summary>
    private void PrepareAnnotations()
    {
        const int Invisible = 1, Hidden = 2, Print = 4, NoView = 32;

        foreach (var page in Pages)
        {
            foreach (var annotation in page.Dictionary.GetArray(PdfName.Get("Annots")).Select(Follow).OfType<PdfDictionary>())
            {
                if (annotation.GetName(PdfName.Subtype) == "Popup")
                {
                    continue;
                }

                var flags = annotation.GetInt(PdfName.Get("F"));
                annotation[PdfName.Get("F")] = new PdfNumber((flags | Print) & ~(Invisible | Hidden | NoView));
            }
        }

        if (Catalog.Get<PdfDictionary>(PdfName.Get("AcroForm")) is { } form)
        {
            form.Remove(PdfName.Get("NeedAppearances"));
        }
    }

    /// <summary>
    /// The names of fonts the document draws with whose programs are not in it, and whether any
    /// of them draws an annotation — a form field, in practice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What counts is what a content stream selects with <c>Tf</c>, not what a resource dictionary
    /// lists. A check box's appearance carries Helvetica in its resources and draws its tick with
    /// paths; a form's <c>/DR</c> lists fonts a viewer could redraw fields with, which PDF/A forbids
    /// anyway. Neither puts a glyph on the page, and the validator does not count them either.
    /// </para>
    /// <para>
    /// Streams are followed through <c>Do</c> into form XObjects, each with its own resources or
    /// its parent's.
    /// </para>
    /// </remarks>
    private (List<string> Fonts, bool InFields) UnembeddedFonts()
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
        var inFields = false;

        foreach (var page in Pages)
        {
            var resources = PageResources(page.Dictionary);

            foreach (var contents in page.Dictionary.GetArray(PdfName.Contents).Select(Follow).OfType<PdfStream>())
            {
                Content(contents.DecodedText, resources);
            }

            foreach (var annotation in page.Dictionary.GetArray(PdfName.Get("Annots")).Select(Follow).OfType<PdfDictionary>())
            {
                if (annotation.Get<PdfDictionary>(PdfName.Get("AP")) is { } appearances)
                {
                    var before = missing.Count;

                    foreach (var (_, value) in appearances)
                    {
                        Appearance(Follow(value));
                    }

                    inFields |= missing.Count > before;
                }
            }
        }

        return ([.. missing], inFields);

        void Appearance(PdfObject? value)
        {
            switch (value)
            {
                case PdfStream stream:
                    Form(stream, null);
                    break;

                // An appearance with states — on and off for a check box.
                case PdfDictionary states:
                    foreach (var (_, state) in states)
                    {
                        Appearance(Follow(state));
                    }

                    break;
            }
        }

        void Form(PdfStream form, PdfDictionary? inherited)
        {
            if (seen.Add(form))
            {
                Content(form.DecodedText, form.Get<PdfDictionary>(PdfName.Resources) ?? inherited);
            }
        }

        void Content(string content, PdfDictionary? resources)
        {
            foreach (Match match in FontSelection().Matches(content))
            {
                if (Follow(resources?.Get<PdfDictionary>(PdfName.Get("Font"))?.Get(PdfName.Get(Unescape(match.Groups[1].Value))))
                        is PdfDictionary font && !IsEmbedded(font))
                {
                    missing.Add(font.GetName(PdfName.Get("BaseFont")) ?? "(unnamed)");
                }
            }

            foreach (Match match in XObjectUse().Matches(content))
            {
                if (Follow(resources?.Get<PdfDictionary>(PdfName.Get("XObject"))?.Get(PdfName.Get(Unescape(match.Groups[1].Value))))
                        is PdfStream { } xobject && xobject.GetName(PdfName.Subtype) == "Form")
                {
                    Form(xobject, resources);
                }
            }
        }

        static bool IsEmbedded(PdfDictionary font)
        {
            switch (font.GetName(PdfName.Subtype))
            {
                // Its glyphs are content streams in the font dictionary itself.
                case "Type3":
                    return true;

                case "Type0":
                    return font.GetArray(PdfName.Get("DescendantFonts")).Select(f => f is PdfReference r ? r.Resolve() : f)
                        .OfType<PdfDictionary>().All(IsEmbedded);
            }

            var descriptor = font.Get<PdfDictionary>(PdfName.Get("FontDescriptor"));

            return descriptor is not null &&
                   (descriptor.Get(PdfName.Get("FontFile")) ?? descriptor.Get(PdfName.Get("FontFile2")) ??
                    descriptor.Get(PdfName.Get("FontFile3"))) is not null;
        }
    }

    /// <summary>A page's resources, which it may inherit from a node above it in the page tree.</summary>
    private static PdfDictionary? PageResources(PdfDictionary page)
    {
        for (var node = page; node is not null; node = node.Get<PdfDictionary>(PdfName.Get("Parent")))
        {
            if (node.Get<PdfDictionary>(PdfName.Resources) is { } resources)
            {
                return resources;
            }
        }

        return null;
    }

    /// <summary>A name as written in a content stream, with its <c>#xx</c> escapes resolved.</summary>
    private static string Unescape(string name) =>
        name.Contains('#', StringComparison.Ordinal)
            ? NameEscape().Replace(name, m => ((char)Convert.ToInt32(m.Value[1..], 16)).ToString())
            : name;

    [GeneratedRegex(@"/([^\s/\[\]()<>{}%]+)\s+[-+.\d]+\s+Tf\b")]
    private static partial Regex FontSelection();

    [GeneratedRegex(@"/([^\s/\[\]()<>{}%]+)\s+Do\b")]
    private static partial Regex XObjectUse();

    [GeneratedRegex("#[0-9A-Fa-f]{2}")]
    private static partial Regex NameEscape();
}
