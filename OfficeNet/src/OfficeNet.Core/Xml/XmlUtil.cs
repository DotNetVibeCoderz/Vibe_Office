// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;

namespace OfficeNet.Core.Xml;

/// <summary>Helpers for reading and writing the OOXML element shapes that repeat everywhere.</summary>
public static class XmlUtil
{
    /// <summary>An empty OOXML part document with the declaration Office writes.</summary>
    public static XDocument NewDocument(XElement root) =>
        new(new XDeclaration("1.0", "UTF-8", "yes"), root);

    /// <summary>
    /// The <c>w:val</c>-style value of a child element, or <c>null</c> when the child is absent.
    /// </summary>
    /// <remarks>
    /// OOXML expresses most scalar properties as <c>&lt;w:jc w:val="center"/&gt;</c> rather than as
    /// an attribute on the parent, so this shape is worth a helper. The attribute is in the same
    /// namespace as the element for WordprocessingML (<c>w:val</c>) but unqualified for DrawingML
    /// and SpreadsheetML (<c>val</c>) — both are tried, because getting it wrong reads as "property
    /// not set" instead of failing.
    /// </remarks>
    public static string? Val(this XElement? parent, XName child)
    {
        var element = parent?.Element(child);
        if (element is null)
        {
            return null;
        }

        return element.Attribute(child.Namespace + "val")?.Value
            ?? element.Attribute("val")?.Value;
    }

    /// <summary>The <c>val</c> attribute of this element itself.</summary>
    public static string? Val(this XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return element.Attribute(element.Name.Namespace + "val")?.Value
            ?? element.Attribute("val")?.Value;
    }

    /// <summary>An attribute's value, or <c>null</c>.</summary>
    public static string? Attr(this XElement? element, XName name) => element?.Attribute(name)?.Value;

    /// <summary>An attribute parsed as an invariant integer, or <paramref name="fallback"/>.</summary>
    public static int IntAttr(this XElement? element, XName name, int fallback = 0) =>
        int.TryParse(element?.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;

    /// <summary>An attribute parsed as an invariant long, or <paramref name="fallback"/>.</summary>
    public static long LongAttr(this XElement? element, XName name, long fallback = 0) =>
        long.TryParse(element?.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;

    /// <summary>An attribute parsed as an invariant double, or <paramref name="fallback"/>.</summary>
    public static double DoubleAttr(this XElement? element, XName name, double fallback = 0) =>
        double.TryParse(element?.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;

    /// <summary>
    /// An OOXML boolean attribute. Absent means the documented default, present-and-empty means
    /// true, and the value may be <c>1</c>/<c>0</c>, <c>true</c>/<c>false</c> or
    /// <c>on</c>/<c>off</c>.
    /// </summary>
    /// <remarks>
    /// The tri-state is what makes this worth a helper: <c>&lt;w:b/&gt;</c> means bold on,
    /// <c>&lt;w:b w:val="0"/&gt;</c> means bold explicitly off (which is not the same as unset —
    /// it overrides an inherited style), and no element at all means inherit. Collapsing the last
    /// two is the classic way to lose a "not bold" override inside a bold style.
    /// </remarks>
    public static bool? OoxmlBool(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length == 0)
        {
            return true;
        }

        return value switch
        {
            "1" or "true" or "on" or "True" or "TRUE" => true,
            "0" or "false" or "off" or "False" or "FALSE" => false,
            _ => null,
        };
    }

    /// <summary>
    /// Reads a toggle property such as <c>w:b</c>: <c>null</c> when the element is absent,
    /// otherwise the element's boolean value defaulting to true.
    /// </summary>
    public static bool? ToggleValue(this XElement? parent, XName child)
    {
        var element = parent?.Element(child);
        if (element is null)
        {
            return null;
        }

        var raw = element.Attribute(child.Namespace + "val")?.Value ?? element.Attribute("val")?.Value;
        return raw is null ? true : OoxmlBool(raw) ?? true;
    }

    /// <summary>Writes an OOXML boolean the way Office does: omitted for true, <c>val="0"</c> for false.</summary>
    public static XElement Toggle(XName name, bool value) =>
        value ? new XElement(name) : new XElement(name, new XAttribute(name.Namespace + "val", "0"));

    /// <summary>Creates a <c>&lt;name val="..."/&gt;</c> element in the element's own namespace.</summary>
    public static XElement ValElement(XName name, string value) =>
        new(name, new XAttribute(name.Namespace + "val", value));

    /// <summary>Creates a <c>&lt;name val="..."/&gt;</c> element with an unqualified attribute (DrawingML style).</summary>
    public static XElement PlainValElement(XName name, string value) =>
        new(name, new XAttribute("val", value));

    /// <summary>
    /// Sets or replaces a child element, keeping OOXML's mandatory element ordering.
    /// </summary>
    /// <param name="parent">The parent to modify.</param>
    /// <param name="name">The child element's name, used to find and replace an existing one.</param>
    /// <param name="child">The element to insert; a <c>null</c> value removes the child instead.</param>
    /// <param name="order">
    /// The complete ordered sequence of names the schema allows in <paramref name="parent"/>.
    /// </param>
    /// <remarks>
    /// OOXML content models are sequences, not choices: <c>w:rPr</c> must list <c>w:b</c> before
    /// <c>w:i</c> before <c>w:sz</c>, and Word rejects the part outright when they are out of
    /// order. Appending is therefore never safe, and every property setter in this library goes
    /// through here with the schema order it belongs to.
    /// </remarks>
    public static void SetOrdered(XElement parent, XName name, XElement? child, IReadOnlyList<XName> order)
    {
        parent.Elements(name).Remove();

        if (child is null)
        {
            return;
        }

        var index = order.ToList().IndexOf(name);
        if (index < 0)
        {
            parent.Add(child);
            return;
        }

        // Insert before the first element that must come after this one.
        var successorNames = new HashSet<XName>(order.Skip(index + 1));
        var successor = parent.Elements().FirstOrDefault(e => successorNames.Contains(e.Name));

        if (successor is not null)
        {
            successor.AddBeforeSelf(child);
        }
        else
        {
            parent.Add(child);
        }
    }

    /// <summary>Gets a child element, creating it in schema order when it does not exist.</summary>
    public static XElement GetOrCreate(XElement parent, XName name, IReadOnlyList<XName> order)
    {
        var existing = parent.Element(name);
        if (existing is not null)
        {
            return existing;
        }

        var created = new XElement(name);
        SetOrdered(parent, name, created, order);
        return created;
    }

    /// <summary>
    /// Rewrites ECMA-376 strict namespaces to their transitional equivalents, in place.
    /// </summary>
    /// <returns>True when anything was rewritten.</returns>
    public static bool NormalizeStrictNamespaces(XDocument document)
    {
        var map = Ns.StrictToTransitional;
        var changed = false;

        foreach (var element in document.Descendants().Prepend(document.Root!).Where(e => e is not null))
        {
            if (map.TryGetValue(element.Name.Namespace, out var replacement))
            {
                element.Name = replacement + element.Name.LocalName;
                changed = true;
            }

            foreach (var attribute in element.Attributes().ToList())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                if (map.TryGetValue(attribute.Name.Namespace, out var attrReplacement))
                {
                    var value = attribute.Value;
                    attribute.Remove();
                    element.SetAttributeValue(attrReplacement + attribute.Name.LocalName, value);
                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Applies <c>xml:space="preserve"</c> when the text needs it.
    /// </summary>
    /// <remarks>
    /// A <c>w:t</c> or <c>a:t</c> whose text has leading or trailing whitespace loses that
    /// whitespace on the round trip unless the attribute is present — which is how "Hello " +
    /// "world" silently becomes "Helloworld" after a save.
    /// </remarks>
    public static XElement TextElement(XName name, string text)
    {
        var element = new XElement(name, text);
        if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])))
        {
            element.SetAttributeValue(XNamespace.Xml + "space", "preserve");
        }

        return element;
    }

    /// <summary>Formats a double the way OOXML attributes expect: invariant, no exponent.</summary>
    public static string Num(double value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>Formats an integer invariantly.</summary>
    public static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);
}
