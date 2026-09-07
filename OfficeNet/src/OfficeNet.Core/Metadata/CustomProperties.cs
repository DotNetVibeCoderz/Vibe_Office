// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections;
using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;

namespace OfficeNet.Core.Metadata;

/// <summary>
/// The user-defined document properties in <c>docProps/custom.xml</c> — the ones Word surfaces as
/// <c>DOCPROPERTY</c> fields and Excel as <c>=CELL</c>-adjacent document metadata.
/// </summary>
/// <remarks>
/// <para>
/// Each property is typed. The value element's name is the type (<c>vt:lpwstr</c>,
/// <c>vt:i4</c>, <c>vt:r8</c>, <c>vt:bool</c>, <c>vt:filetime</c>), and Office honours it: a number
/// written as <c>lpwstr</c> shows up in a field as text and will not participate in a calculation.
/// </para>
/// <para>
/// The <c>pid</c> attribute is a property id that must be unique and must start at 2 — 0 and 1 are
/// reserved by the underlying OLE property-set format. A custom.xml whose first property has pid 1
/// opens, and then Word silently drops the property on its next save.
/// </para>
/// </remarks>
public sealed class CustomProperties : IEnumerable<KeyValuePair<string, object?>>
{
    private const string FormatId = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}";
    private static readonly OpcPartName PartName = "/docProps/custom.xml";

    private readonly OpcPackage _package;
    private readonly OpcPart _part;

    private CustomProperties(OpcPackage package, OpcPart part)
    {
        _package = package;
        _part = part;
    }

    /// <summary>Opens the package's custom properties, creating the part when there is none.</summary>
    public static CustomProperties Open(OpcPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var existing = package.RootRelatedPart(RelationshipTypes.CustomProperties)
            ?? package.FindPart(PartName);

        if (existing is not null)
        {
            return new CustomProperties(package, existing);
        }

        var root = new XElement(Ns.Cp + "Properties",
            new XAttribute(XNamespace.Xmlns + "vt", Ns.Vt.NamespaceName));

        var part = package.AddXmlPart(PartName, ContentTypes.CustomProperties, XmlUtil.NewDocument(root));
        package.AddRootRelationship(part, RelationshipTypes.CustomProperties);
        return new CustomProperties(package, part);
    }

    private XElement Root => _part.Xml.Root!;

    private IEnumerable<XElement> PropertyElements => Root.Elements(Ns.Cp + "property");

    /// <summary>The number of custom properties.</summary>
    public int Count => PropertyElements.Count();

    /// <summary>The property names, in document order.</summary>
    public IEnumerable<string> Names =>
        PropertyElements.Select(e => e.Attribute("name")?.Value ?? string.Empty);

    /// <summary>Reads or writes a property by name. Reading an absent property gives <c>null</c>.</summary>
    public object? this[string name]
    {
        get => TryGetValue(name, out var value) ? value : null;
        set => Set(name, value);
    }

    /// <summary>True when a property with this name exists.</summary>
    public bool Contains(string name) => Find(name) is not null;

    /// <summary>Reads a property.</summary>
    public bool TryGetValue(string name, out object? value)
    {
        var element = Find(name);
        if (element is null)
        {
            value = null;
            return false;
        }

        value = ReadValue(element);
        return true;
    }

    /// <summary>Reads a property as a specific type, or returns <paramref name="fallback"/>.</summary>
    public T? GetValueOrDefault<T>(string name, T? fallback = default)
    {
        if (!TryGetValue(name, out var value) || value is null)
        {
            return fallback;
        }

        if (value is T typed)
        {
            return typed;
        }

        try
        {
            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return fallback;
        }
    }

    /// <summary>
    /// Sets a property, choosing the OOXML variant type from the value's runtime type. A
    /// <c>null</c> value removes the property.
    /// </summary>
    /// <exception cref="ArgumentException">The value's type has no OOXML variant equivalent.</exception>
    public void Set(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var existing = Find(name);

        if (value is null)
        {
            existing?.Remove();
            _package.MarkDirty();
            return;
        }

        var valueElement = WriteValue(value);

        if (existing is not null)
        {
            existing.Elements().Remove();
            existing.Add(valueElement);
        }
        else
        {
            Root.Add(new XElement(Ns.Cp + "property",
                new XAttribute("fmtid", FormatId),
                new XAttribute("pid", NextPid()),
                new XAttribute("name", name),
                valueElement));
        }

        _package.MarkDirty();
    }

    /// <summary>Removes a property. Returns false when it did not exist.</summary>
    public bool Remove(string name)
    {
        var element = Find(name);
        if (element is null)
        {
            return false;
        }

        element.Remove();
        RenumberPids();
        _package.MarkDirty();
        return true;
    }

    /// <summary>Removes every custom property.</summary>
    public void Clear()
    {
        Root.Elements().Remove();
        _package.MarkDirty();
    }

    private XElement? Find(string name) =>
        PropertyElements.FirstOrDefault(e =>
            string.Equals(e.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));

    private int NextPid()
    {
        var used = PropertyElements
            .Select(e => e.IntAttr("pid"))
            .Where(pid => pid >= 2)
            .ToHashSet();

        var pid = 2;
        while (used.Contains(pid))
        {
            pid++;
        }

        return pid;
    }

    private void RenumberPids()
    {
        // Removing a property leaves a gap, and gaps are legal — but a duplicate is not, and
        // renumbering after a removal is the cheapest way to guarantee the invariant holds no
        // matter how the caller interleaves adds and removes.
        var pid = 2;
        foreach (var element in PropertyElements)
        {
            element.SetAttributeValue("pid", pid++);
        }
    }

    private static object? ReadValue(XElement property)
    {
        var value = property.Elements().FirstOrDefault();
        if (value is null)
        {
            return null;
        }

        var text = value.Value;

        return value.Name.LocalName switch
        {
            "lpwstr" or "lpstr" or "bstr" => text,
            "i1" or "i2" or "i4" or "int" =>
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : text,
            "i8" =>
                long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : text,
            "ui1" or "ui2" or "ui4" or "uint" =>
                uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) ? u : text,
            "r4" =>
                float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : text,
            "r8" or "decimal" =>
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : text,
            // OOXML writes "true"/"false" here; the older OLE convention wrote "1"/"0" and files
            // in the wild still contain both.
            "bool" => text is "true" or "1" or "TRUE",
            "filetime" or "date" =>
                DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
                    ? dt
                    : text,
            _ => text,
        };
    }

    private static XElement WriteValue(object value) => value switch
    {
        string s => new XElement(Ns.Vt + "lpwstr", s),
        bool b => new XElement(Ns.Vt + "bool", b ? "true" : "false"),
        int i => new XElement(Ns.Vt + "i4", i.ToString(CultureInfo.InvariantCulture)),
        short sh => new XElement(Ns.Vt + "i2", sh.ToString(CultureInfo.InvariantCulture)),
        long l => new XElement(Ns.Vt + "i8", l.ToString(CultureInfo.InvariantCulture)),
        uint ui => new XElement(Ns.Vt + "ui4", ui.ToString(CultureInfo.InvariantCulture)),
        float f => new XElement(Ns.Vt + "r4", f.ToString("R", CultureInfo.InvariantCulture)),
        double d => new XElement(Ns.Vt + "r8", d.ToString("R", CultureInfo.InvariantCulture)),
        decimal m => new XElement(Ns.Vt + "decimal", m.ToString(CultureInfo.InvariantCulture)),
        DateTime dt => new XElement(Ns.Vt + "filetime",
            dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
        DateTimeOffset dto => new XElement(Ns.Vt + "filetime",
            dto.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
        _ => throw new ArgumentException(
            $"A custom document property cannot hold a {value.GetType().Name}. Supported types are " +
            "string, bool, the integer types, float, double, decimal and DateTime.", nameof(value)),
    };

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        foreach (var element in PropertyElements)
        {
            var name = element.Attribute("name")?.Value;
            if (name is not null)
            {
                yield return new KeyValuePair<string, object?>(name, ReadValue(element));
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
