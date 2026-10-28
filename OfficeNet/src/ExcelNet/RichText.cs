// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;

namespace ExcelNet;

/// <summary>
/// One differently-formatted stretch of a cell's text: the bold word in "Status: <b>LUNAS</b>".
/// </summary>
/// <remarks>
/// Every property is optional. A run that sets nothing takes the cell's own font, which is what a
/// run without <c>rPr</c> means in the file.
/// </remarks>
public sealed record RichTextRun(string Text)
{
    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Underline { get; init; }

    public bool Strike { get; init; }

    public OfficeColor? Color { get; init; }

    public double? SizePoints { get; init; }

    public string? FontName { get; init; }

    /// <summary>
    /// The run properties exactly as read, written back unchanged when the run is.
    /// </summary>
    /// <remarks>
    /// <c>rPr</c> can carry what this record does not model — superscript, a theme font scheme, a
    /// charset. A run that came from a file and was not replaced keeps all of it.
    /// </remarks>
    internal XElement? Raw { get; init; }

    internal bool HasFormat =>
        Bold || Italic || Underline || Strike || Color is not null || SizePoints is not null || FontName is not null;

    /// <summary>Order of CT_RPrElt; note <c>rFont</c>, not <c>name</c> as in a cell font.</summary>
    private static readonly string[] Order =
    [
        "rFont", "charset", "family", "b", "i", "strike", "outline", "shadow", "condense",
        "extend", "color", "sz", "u", "vertAlign", "scheme",
    ];

    internal static RichTextRun Read(XElement run)
    {
        var text = string.Concat(run.Elements(Ns.S + "t").Select(t => t.Value));
        var properties = run.Element(Ns.S + "rPr");

        if (properties is null)
        {
            return new RichTextRun(text);
        }

        static bool On(XElement? e) => e is not null && XmlUtil.OoxmlBool(e.Attr("val")) != false;

        return new RichTextRun(text)
        {
            Bold = On(properties.Element(Ns.S + "b")),
            Italic = On(properties.Element(Ns.S + "i")),
            Strike = On(properties.Element(Ns.S + "strike")),
            Underline = properties.Element(Ns.S + "u") is { } u && u.Attr("val") != "none",
            Color = properties.Element(Ns.S + "color")?.Attr("rgb") is { Length: >= 6 } rgb &&
                    int.TryParse(rgb[^6..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
                ? OfficeColor.FromRgb(value)
                : null,
            SizePoints = double.TryParse(properties.Element(Ns.S + "sz")?.Attr("val"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var size)
                ? size
                : null,
            FontName = properties.Element(Ns.S + "rFont")?.Attr("val"),
            Raw = new XElement(properties),
        };
    }

    internal void Write(XmlWriter writer)
    {
        var ns = Ns.S.NamespaceName;
        writer.WriteStartElement("r", ns);

        if (Raw is not null)
        {
            Raw.WriteTo(writer);
        }
        else if (HasFormat)
        {
            writer.WriteStartElement("rPr", ns);

            foreach (var name in Order)
            {
                switch (name)
                {
                    case "rFont" when FontName is not null:
                        Value(writer, "rFont", FontName);
                        break;
                    case "b" when Bold:
                        writer.WriteElementString("b", ns, null);
                        break;
                    case "i" when Italic:
                        writer.WriteElementString("i", ns, null);
                        break;
                    case "strike" when Strike:
                        writer.WriteElementString("strike", ns, null);
                        break;
                    case "color" when Color is { } color:
                        writer.WriteStartElement("color", ns);
                        writer.WriteAttributeString("rgb", "FF" + color.ToHex());
                        writer.WriteEndElement();
                        break;
                    case "sz" when SizePoints is { } points:
                        Value(writer, "sz", points.ToString(CultureInfo.InvariantCulture));
                        break;
                    case "u" when Underline:
                        writer.WriteElementString("u", ns, null);
                        break;
                }
            }

            writer.WriteEndElement();
        }

        writer.WriteStartElement("t", ns);

        // A run's spaces are content: "Status: " before a bold word loses its space without this.
        if (Text.Length > 0 && (char.IsWhiteSpace(Text[0]) || char.IsWhiteSpace(Text[^1])))
        {
            writer.WriteAttributeString("xml", "space", null, "preserve");
        }

        writer.WriteString(SharedStrings.Sanitize(Text));
        writer.WriteEndElement();
        writer.WriteEndElement();

        static void Value(XmlWriter writer, string name, string value)
        {
            writer.WriteStartElement(name, Ns.S.NamespaceName);
            writer.WriteAttributeString("val", value);
            writer.WriteEndElement();
        }
    }

    /// <summary>A key that two runs share exactly when they would be written identically.</summary>
    internal string Key() =>
        $"{Text}\u0001{Bold}{Italic}{Underline}{Strike}{Color?.ToHex()}{SizePoints}{FontName}\u0001{Raw}";
}
