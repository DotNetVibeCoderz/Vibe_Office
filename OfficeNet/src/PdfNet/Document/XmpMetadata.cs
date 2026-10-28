// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Security;
using System.Text;
using PdfNet.Objects;

namespace PdfNet.Document;

public sealed partial class PdfDocument
{
    /// <summary>
    /// The document's XMP metadata packet (the catalog's <c>/Metadata</c> stream), or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Setting it stores the packet uncompressed, because PDF/A forbids compressing the metadata
    /// stream — it is meant to be findable by tools that do not parse PDF.
    /// </remarks>
    public string? XmpMetadata
    {
        get => Follow(Catalog[PdfName.Get("Metadata")]) is PdfStream stream
            ? Encoding.UTF8.GetString(stream.Decoded)
            : null;
        set
        {
            if (value is null)
            {
                Catalog.Remove(PdfName.Get("Metadata"));
                return;
            }

            // Reused when present: a new object on every save would leave the old packets behind.
            if (Follow(Catalog[PdfName.Get("Metadata")]) is not PdfStream stream)
            {
                stream = new PdfStream();
                Catalog[PdfName.Get("Metadata")] = AddObject(stream);
            }

            stream[PdfName.Type] = PdfName.Get("Metadata");
            stream.SetName(PdfName.Subtype, "XML");
            stream.Remove(PdfName.Filter);
            stream.SetDecoded(Encoding.UTF8.GetBytes(value), compress: false);
        }
    }

    /// <summary>
    /// When true, an XMP packet mirroring <see cref="Info"/> is written on every save.
    /// </summary>
    /// <remarks>
    /// The two must agree — title, author, dates to the second, and the time zone — or a PDF/A
    /// validator rejects the file. Writing the packet from the information dictionary after it has
    /// been stamped with the save's dates is the only way to make that true by construction.
    /// </remarks>
    public bool WriteXmpMetadata { get; set; }

    /// <summary>The PDF/A part and conformance to declare in XMP, set by PDF/A conversion.</summary>
    internal (int Part, string Conformance)? PdfAIdentification { get; set; }

    /// <summary>Builds the XMP packet from the information dictionary.</summary>
    internal string BuildXmp()
    {
        static string Escape(string text) => SecurityElement.Escape(text);

        static string Date(DateTimeOffset value) =>
            value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) +
            (value.Offset == TimeSpan.Zero ? "Z" : value.ToString("zzz", CultureInfo.InvariantCulture));

        var builder = new StringBuilder();
        builder.Append("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");
        builder.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n");
        builder.Append(" <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");
        builder.Append("  <rdf:Description rdf:about=\"\"");
        builder.Append(" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"");
        builder.Append(" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"");
        builder.Append(" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\"");

        if (PdfAIdentification is not null)
        {
            builder.Append(" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\"");
        }

        builder.Append(">\n");
        builder.Append("   <dc:format>application/pdf</dc:format>\n");

        if (Info.Title is { } title)
        {
            builder.Append($"   <dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">{Escape(title)}</rdf:li></rdf:Alt></dc:title>\n");
        }

        if (Info.Author is { } author)
        {
            builder.Append($"   <dc:creator><rdf:Seq><rdf:li>{Escape(author)}</rdf:li></rdf:Seq></dc:creator>\n");
        }

        if (Info.Subject is { } subject)
        {
            builder.Append($"   <dc:description><rdf:Alt><rdf:li xml:lang=\"x-default\">{Escape(subject)}</rdf:li></rdf:Alt></dc:description>\n");
        }

        if (Info.Keywords is { } keywords)
        {
            builder.Append($"   <pdf:Keywords>{Escape(keywords)}</pdf:Keywords>\n");
        }

        if (Info.Producer is { } producer)
        {
            builder.Append($"   <pdf:Producer>{Escape(producer)}</pdf:Producer>\n");
        }

        if (Info.Creator is { } creator)
        {
            builder.Append($"   <xmp:CreatorTool>{Escape(creator)}</xmp:CreatorTool>\n");
        }

        if (Info.CreationDate is { } created)
        {
            builder.Append($"   <xmp:CreateDate>{Date(created)}</xmp:CreateDate>\n");
        }

        if (Info.ModificationDate is { } modified)
        {
            builder.Append($"   <xmp:ModifyDate>{Date(modified)}</xmp:ModifyDate>\n");
            builder.Append($"   <xmp:MetadataDate>{Date(modified)}</xmp:MetadataDate>\n");
        }

        if (PdfAIdentification is { } id)
        {
            builder.Append($"   <pdfaid:part>{id.Part}</pdfaid:part>\n");
            builder.Append($"   <pdfaid:conformance>{id.Conformance}</pdfaid:conformance>\n");
        }

        builder.Append("  </rdf:Description>\n </rdf:RDF>\n</x:xmpmeta>\n");

        // Padding lets a later editor grow the packet in place; the trailer says it may.
        builder.Append(new string(' ', 2000)).Append('\n');
        builder.Append("<?xpacket end=\"w\"?>");
        return builder.ToString();
    }
}
