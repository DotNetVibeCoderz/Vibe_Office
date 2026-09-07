// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections;
using System.Globalization;
using System.Text;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using PdfNet.Content;
using PdfNet.Document;
using PdfNet.Objects;

namespace PdfNet.Forms;

/// <summary>The kinds of interactive field a PDF form can contain.</summary>
public enum PdfFieldType
{
    /// <summary>A single- or multi-line text box.</summary>
    Text,

    /// <summary>A checkbox.</summary>
    Checkbox,

    /// <summary>One button of a radio group.</summary>
    RadioButton,

    /// <summary>A push button, which holds no value.</summary>
    PushButton,

    /// <summary>A drop-down list.</summary>
    ComboBox,

    /// <summary>A scrolling list box.</summary>
    ListBox,

    /// <summary>A signature field.</summary>
    Signature,

    /// <summary>A field whose type could not be determined.</summary>
    Unknown,
}

/// <summary>
/// One field of an interactive form.
/// </summary>
/// <remarks>
/// A field and its on-page widget are usually the same dictionary but need not be: a field that
/// appears on several pages has one field dictionary and several <c>/Kids</c> widgets. Both shapes
/// are handled, because the merged form is what a simple document produces and the split form is
/// what a real one does.
/// </remarks>
public sealed class PdfFormField
{
    private readonly AcroForm _form;

    internal PdfFormField(AcroForm form, PdfDictionary dictionary, string fullName)
    {
        _form = form;
        Dictionary = dictionary;
        FullName = fullName;
    }

    /// <summary>The underlying field dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>
    /// The field's fully qualified name, with ancestors joined by periods — which is the name
    /// <c>/T</c> lookups and JavaScript both use.
    /// </summary>
    public string FullName { get; }

    /// <summary>The field's own partial name.</summary>
    public string? PartialName => Dictionary.GetText(PdfName.Get("T"));

    /// <summary>The tooltip text.</summary>
    public string? Tooltip => Dictionary.GetText(PdfName.Get("TU"));

    /// <summary>The field type.</summary>
    public PdfFieldType FieldType
    {
        get
        {
            // /FT is inheritable, so a field inside a group may not declare it.
            var type = InheritedName("FT");
            var flags = InheritedInt("Ff");

            return type switch
            {
                "Tx" => PdfFieldType.Text,
                // The button family is distinguished only by flag bits: 16 is a push button,
                // 32768 is a radio button, and neither set means a checkbox.
                "Btn" when (flags & 1 << 16) != 0 => PdfFieldType.PushButton,
                "Btn" when (flags & 1 << 15) != 0 => PdfFieldType.RadioButton,
                "Btn" => PdfFieldType.Checkbox,
                "Ch" when (flags & 1 << 17) != 0 => PdfFieldType.ComboBox,
                "Ch" => PdfFieldType.ListBox,
                "Sig" => PdfFieldType.Signature,
                _ => PdfFieldType.Unknown,
            };
        }
    }

    /// <summary>True when the field must not be edited.</summary>
    public bool IsReadOnly => (InheritedInt("Ff") & 1) != 0;

    /// <summary>True when the field must be filled before submission.</summary>
    public bool IsRequired => (InheritedInt("Ff") & 2) != 0;

    /// <summary>True when a text field accepts line breaks.</summary>
    public bool IsMultiline => FieldType == PdfFieldType.Text && (InheritedInt("Ff") & 1 << 12) != 0;

    /// <summary>True when a text field masks its content.</summary>
    public bool IsPassword => FieldType == PdfFieldType.Text && (InheritedInt("Ff") & 1 << 13) != 0;

    /// <summary>The maximum length of a text field's value; 0 when unlimited.</summary>
    public int MaxLength => Dictionary.GetInt(PdfName.Get("MaxLen"));

    /// <summary>The options a choice field offers.</summary>
    public IReadOnlyList<string> Options
    {
        get
        {
            var opt = Dictionary.Get(PdfName.Get("Opt")) as PdfArray;
            if (opt is null)
            {
                return [];
            }

            var result = new List<string>(opt.Count);

            foreach (var item in opt)
            {
                // An option is either a display string or a two-element [export, display] array.
                switch (_form.Document.Follow(item))
                {
                    case PdfString text:
                        result.Add(text.AsText());
                        break;

                    case PdfArray pair when pair.Count >= 2:
                        result.Add((_form.Document.Follow(pair[1]) as PdfString)?.AsText() ?? string.Empty);
                        break;

                    case PdfArray pair when pair.Count == 1:
                        result.Add((_form.Document.Follow(pair[0]) as PdfString)?.AsText() ?? string.Empty);
                        break;
                }
            }

            return result;
        }
    }

    /// <summary>
    /// The states a checkbox or radio button can be set to, other than <c>Off</c>.
    /// </summary>
    /// <remarks>
    /// A checkbox's "on" state is not always <c>/Yes</c>. Producers use the field's own name, a
    /// number, or anything else; the only reliable source is the appearance dictionary's key set,
    /// which is what this reads. Writing <c>/Yes</c> into a box whose on-state is <c>/1</c> ticks
    /// nothing and shows nothing.
    /// </remarks>
    public IReadOnlyList<string> OnStates
    {
        get
        {
            var result = new List<string>();

            foreach (var widget in Widgets())
            {
                if (_form.Document.Follow(widget[PdfName.Get("AP")]) is not PdfDictionary ap)
                {
                    continue;
                }

                // PdfStream derives from PdfDictionary. A text field's /N is a stream, and reading
                // its dictionary keys here would report /Type, /BBox and /Length as "on" states.
                var appearance = _form.Document.Follow(ap[PdfName.Get("N")]);

                if (appearance is PdfStream || appearance is not PdfDictionary normal)
                {
                    continue;
                }

                foreach (var key in normal.Keys)
                {
                    if (key.Value != "Off" && !result.Contains(key.Value))
                    {
                        result.Add(key.Value);
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// The field's value: text for a text or choice field, the state name for a button.
    /// </summary>
    public string? Value
    {
        get
        {
            var raw = InheritedValue("V");

            return raw switch
            {
                PdfString text => text.AsText(),
                PdfName name => name.Value,
                PdfNumber number => number.ToString(),
                PdfArray array when array.Count > 0 =>
                    (_form.Document.Follow(array[0]) as PdfString)?.AsText(),
                _ => null,
            };
        }
        set => SetValue(value);
    }

    /// <summary>True when a checkbox or radio button is ticked.</summary>
    public bool IsChecked
    {
        get
        {
            var value = Value;
            return value is not null && value != "Off";
        }
        set
        {
            var on = OnStates.FirstOrDefault() ?? "Yes";
            SetValue(value ? on : "Off");
        }
    }

    /// <summary>Sets the field's value and refreshes its appearance.</summary>
    public void SetValue(string? value)
    {
        var type = FieldType;

        if (type == PdfFieldType.PushButton)
        {
            throw new InvalidOperationException(
                $"Field '{FullName}' is a push button and holds no value.");
        }

        if (IsReadOnly)
        {
            throw new InvalidOperationException($"Field '{FullName}' is read-only.");
        }

        if (value is null)
        {
            Dictionary.Remove(PdfName.Get("V"));
            Dictionary.Remove(PdfName.Get("AS"));
            return;
        }

        if (MaxLength > 0 && type == PdfFieldType.Text && value.Length > MaxLength)
        {
            value = value[..MaxLength];
        }

        if (type is PdfFieldType.Checkbox or PdfFieldType.RadioButton)
        {
            var states = OnStates;
            var state = value == "Off" || value.Length == 0 ? "Off"
                : states.Contains(value) ? value
                : states.FirstOrDefault() ?? "Yes";

            Dictionary[PdfName.Get("V")] = PdfName.Get(state);

            // /AS on the widget is what actually draws the tick. Setting /V alone changes the value
            // and leaves the box looking empty until the reader regenerates appearances.
            foreach (var widget in Widgets())
            {
                var appearances = _form.Document.Follow(widget[PdfName.Get("AP")]) as PdfDictionary;
                var normal = appearances is null
                    ? null
                    : _form.Document.Follow(appearances[PdfName.Get("N")]) as PdfDictionary;

                var widgetState = state != "Off" && normal is not null && !normal.ContainsKey(PdfName.Get(state))
                    ? "Off"
                    : state;

                widget[PdfName.Get("AS")] = PdfName.Get(widgetState);
            }

            return;
        }

        Dictionary[PdfName.Get("V")] = new PdfString(value);

        if (type is PdfFieldType.ComboBox or PdfFieldType.ListBox)
        {
            Dictionary[PdfName.Get("I")] = new PdfArray();
        }

        GenerateTextAppearance(value);
    }

    /// <summary>
    /// Draws the field's value into its appearance stream.
    /// </summary>
    /// <remarks>
    /// Without this the filled value is invisible in every viewer that does not regenerate
    /// appearances itself — which includes most browser viewers, most mobile readers, and every
    /// print pipeline. Setting <c>/NeedAppearances</c> instead asks the reader to do it and is
    /// widely ignored, so both are done: the appearance is generated <em>and</em> the flag is set.
    /// </remarks>
    private void GenerateTextAppearance(string value)
    {
        if (FieldType is not (PdfFieldType.Text or PdfFieldType.ComboBox or PdfFieldType.ListBox))
        {
            return;
        }

        foreach (var widget in Widgets())
        {
            if (widget.Get(PdfName.Get("Rect")) is not PdfArray rectArray)
            {
                continue;
            }

            var rect = PdfRectangle.FromArray(rectArray);
            var (font, size, color) = ParseDefaultAppearance();

            // An auto-sized field (size 0) picks a size that fits the box height.
            if (size <= 0)
            {
                size = Math.Max(6, Math.Min(12, rect.Height * 0.65));
            }

            const double Padding = 2;
            var builder = new StringBuilder();
            builder.Append("/Tx BMC\nq\n");
            builder.Append(CultureInfo.InvariantCulture,
                $"{Padding:0.##} {Padding:0.##} {rect.Width - Padding * 2:0.##} {rect.Height - Padding * 2:0.##} re\nW\nn\n");
            builder.Append("BT\n");
            builder.Append(CultureInfo.InvariantCulture,
                $"{color.R / 255.0:0.###} {color.G / 255.0:0.###} {color.B / 255.0:0.###} rg\n");
            builder.Append(CultureInfo.InvariantCulture, $"/Helv {size:0.##} Tf\n");

            if (IsMultiline)
            {
                var lineHeight = size * 1.15;
                var y = rect.Height - Padding - size;

                foreach (var line in value.Replace("\r\n", "\n").Split('\n'))
                {
                    builder.Append(CultureInfo.InvariantCulture,
                        $"1 0 0 1 {Padding + 2:0.##} {y:0.##} Tm\n");
                    builder.Append(Escape(line)).Append(" Tj\n");
                    y -= lineHeight;

                    if (y < Padding)
                    {
                        break;
                    }
                }
            }
            else
            {
                // A single-line field's text is vertically centred on the box.
                var baseline = (rect.Height - size) / 2 + size * 0.22;
                builder.Append(CultureInfo.InvariantCulture,
                    $"1 0 0 1 {Padding + 2:0.##} {baseline:0.##} Tm\n");
                builder.Append(Escape(value)).Append(" Tj\n");
            }

            builder.Append("ET\nQ\nEMC\n");

            var appearance = new PdfStream();
            appearance[PdfName.Type] = PdfName.XObject;
            appearance.SetName(PdfName.Subtype, "Form");
            appearance[PdfName.Get("BBox")] = new PdfArray(0, 0, rect.Width, rect.Height);
            appearance[PdfName.Resources] = _form.AppearanceResources();
            appearance.SetText(builder.ToString());

            var appearanceDictionary = _form.Document.Follow(widget[PdfName.Get("AP")]) as PdfDictionary;
            if (appearanceDictionary is null)
            {
                appearanceDictionary = new PdfDictionary();
                widget[PdfName.Get("AP")] = appearanceDictionary;
            }

            appearanceDictionary[PdfName.Get("N")] = _form.Document.AddObject(appearance);
        }
    }

    private (StandardFont Font, double Size, OfficeColor Color) ParseDefaultAppearance()
    {
        // /DA is a fragment of content stream: "/Helv 9 Tf 0 g".
        var da = (InheritedValue("DA") as PdfString)?.AsText()
                 ?? _form.Dictionary.GetText(PdfName.Get("DA"))
                 ?? "/Helv 0 Tf 0 g";

        var tokens = da.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        double size = 0;
        var color = OfficeColor.Black;

        for (var i = 0; i < tokens.Length; i++)
        {
            switch (tokens[i])
            {
                case "Tf" when i >= 1 &&
                               double.TryParse(tokens[i - 1], NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out var parsed):
                    size = parsed;
                    break;

                case "g" when i >= 1 &&
                              double.TryParse(tokens[i - 1], NumberStyles.Float,
                                  CultureInfo.InvariantCulture, out var gray):
                    var level = (byte)Math.Clamp(gray * 255, 0, 255);
                    color = OfficeColor.FromRgb(level, level, level);
                    break;

                case "rg" when i >= 3:
                    if (double.TryParse(tokens[i - 3], NumberStyles.Float, CultureInfo.InvariantCulture, out var r) &&
                        double.TryParse(tokens[i - 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var g) &&
                        double.TryParse(tokens[i - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                    {
                        color = OfficeColor.FromRgb(
                            (byte)Math.Clamp(r * 255, 0, 255),
                            (byte)Math.Clamp(g * 255, 0, 255),
                            (byte)Math.Clamp(b * 255, 0, 255));
                    }

                    break;
            }
        }

        return (StandardFont.Helvetica, size, color);
    }

    private static string Escape(string text)
    {
        var bytes = StandardFonts.EncodeWinAnsi(text);
        var builder = new StringBuilder(bytes.Length + 2).Append('(');

        foreach (var b in bytes)
        {
            if (b is (byte)'(' or (byte)')' or (byte)'\\')
            {
                builder.Append('\\');
            }

            builder.Append((char)b);
        }

        return builder.Append(')').ToString();
    }

    /// <summary>The widget annotations that draw this field.</summary>
    public IEnumerable<PdfDictionary> Widgets()
    {
        // A field with no /Kids is its own widget; one with /Kids has them as separate annotations.
        var kids = Dictionary.Get(PdfName.Get("Kids")) as PdfArray;

        if (kids is null || kids.Count == 0)
        {
            yield return Dictionary;
            yield break;
        }

        foreach (var kid in kids)
        {
            if (_form.Document.Follow(kid) is PdfDictionary widget &&
                widget.Get(PdfName.Get("T")) is null)
            {
                yield return widget;
            }
        }
    }

    private PdfObject? InheritedValue(string key)
    {
        var node = Dictionary;

        for (var depth = 0; node is not null && depth < 32; depth++)
        {
            var value = node.Get(PdfName.Get(key));
            if (value is not null)
            {
                return value;
            }

            node = node.Get<PdfDictionary>(PdfName.Parent);
        }

        return null;
    }

    private string? InheritedName(string key) => (InheritedValue(key) as PdfName)?.Value;

    private int InheritedInt(string key) => (InheritedValue(key) as PdfNumber)?.IntValue ?? 0;

    public override string ToString() => $"{FullName} ({FieldType}) = {Value ?? "(empty)"}";
}

/// <summary>
/// A document's interactive form.
/// </summary>
public sealed class AcroForm : IReadOnlyCollection<PdfFormField>
{
    private readonly List<PdfFormField> _fields = [];

    private AcroForm(PdfDocument document, PdfDictionary dictionary)
    {
        Document = document;
        Dictionary = dictionary;
        Load();
    }

    /// <summary>The document this form belongs to.</summary>
    public PdfDocument Document { get; }

    /// <summary>The <c>/AcroForm</c> dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <inheritdoc />
    public int Count => _fields.Count;

    /// <summary>Every field, including those nested inside groups.</summary>
    public IReadOnlyList<PdfFormField> Fields => _fields;

    /// <summary>Opens a document's form, or <c>null</c> when it has none.</summary>
    public static AcroForm? Open(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Catalog.Get<PdfDictionary>(PdfName.Get("AcroForm")) is { } dictionary
            ? new AcroForm(document, dictionary)
            : null;
    }

    /// <summary>Opens a document's form, creating an empty one when it has none.</summary>
    public static AcroForm OpenOrCreate(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Catalog.Get<PdfDictionary>(PdfName.Get("AcroForm")) is { } existing)
        {
            return new AcroForm(document, existing);
        }

        var dictionary = new PdfDictionary();
        dictionary[PdfName.Get("Fields")] = new PdfArray();
        dictionary.Set(PdfName.Get("DA"), "/Helv 0 Tf 0 g");
        document.Catalog[PdfName.Get("AcroForm")] = document.AddObject(dictionary);
        return new AcroForm(document, dictionary);
    }

    private void Load()
    {
        var roots = Dictionary.Get(PdfName.Get("Fields")) as PdfArray;
        if (roots is null)
        {
            return;
        }

        foreach (var item in roots)
        {
            if (Document.Follow(item) is PdfDictionary field)
            {
                Walk(field, string.Empty, 0, []);
            }
        }
    }

    private void Walk(PdfDictionary node, string prefix, int depth, HashSet<PdfDictionary> visited)
    {
        if (depth > 32 || !visited.Add(node))
        {
            return;
        }

        var partial = node.GetText(PdfName.Get("T"));
        var name = partial is null ? prefix : prefix.Length == 0 ? partial : $"{prefix}.{partial}";

        var kids = node.Get(PdfName.Get("Kids")) as PdfArray;

        // A node with named kids is a group; one whose kids are unnamed widgets is a field with
        // several appearances and is itself the field.
        var hasNamedKids = kids is not null && kids.Any(k =>
            Document.Follow(k) is PdfDictionary kid && kid.Get(PdfName.Get("T")) is not null);

        if (hasNamedKids)
        {
            foreach (var kid in kids!)
            {
                if (Document.Follow(kid) is PdfDictionary child)
                {
                    Walk(child, name, depth + 1, visited);
                }
            }

            return;
        }

        if (partial is not null || node.Get(PdfName.Get("FT")) is not null)
        {
            _fields.Add(new PdfFormField(this, node, name));
        }
    }

    /// <summary>Finds a field by its fully qualified name.</summary>
    public PdfFormField? this[string name] =>
        _fields.FirstOrDefault(f => f.FullName == name)
        ?? _fields.FirstOrDefault(f => string.Equals(f.FullName, name, StringComparison.OrdinalIgnoreCase))
        ?? _fields.FirstOrDefault(f => f.PartialName == name);

    /// <summary>Every field's name and current value.</summary>
    public Dictionary<string, string?> ReadValues() =>
        _fields.ToDictionary(f => f.FullName, f => f.Value);

    /// <summary>
    /// Fills fields by name. Names that do not match a field are reported rather than ignored.
    /// </summary>
    /// <returns>The names that had no matching field.</returns>
    public IReadOnlyList<string> Fill(IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var missing = new List<string>();

        foreach (var (name, value) in values)
        {
            var field = this[name];

            if (field is null)
            {
                missing.Add(name);
                continue;
            }

            field.SetValue(value);
        }

        NeedAppearances = true;
        return missing;
    }

    /// <summary>
    /// Asks the reader to regenerate field appearances on open.
    /// </summary>
    /// <remarks>
    /// This is a belt-and-braces measure alongside the appearance streams this library generates.
    /// Acrobat honours it; most other viewers do not, which is why the appearances are generated
    /// too.
    /// </remarks>
    public bool NeedAppearances
    {
        get => Dictionary.GetBool(PdfName.Get("NeedAppearances"));
        set => Dictionary[PdfName.Get("NeedAppearances")] = PdfBoolean.Get(value);
    }

    /// <summary>
    /// Flattens the form: draws every field's appearance onto its page and removes the field.
    /// </summary>
    /// <remarks>
    /// A flattened form is what an archived or emailed document should be — the values become part
    /// of the page and cannot be changed back into an editable field.
    /// </remarks>
    public void Flatten()
    {
        foreach (var page in Document.Pages)
        {
            var annotations = page.Dictionary.Get(PdfName.Annots) as PdfArray;
            if (annotations is null)
            {
                continue;
            }

            var remaining = new PdfArray();

            foreach (var item in annotations)
            {
                if (Document.Follow(item) is not PdfDictionary annotation ||
                    annotation.GetName(PdfName.Subtype) != "Widget")
                {
                    remaining.Add(item);
                    continue;
                }

                StampWidget(page, annotation);
            }

            page.Dictionary[PdfName.Annots] = remaining;
        }

        Document.Catalog.Remove(PdfName.Get("AcroForm"));
        _fields.Clear();
    }

    private void StampWidget(PdfPage page, PdfDictionary widget)
    {
        if (widget.Get(PdfName.Get("Rect")) is not PdfArray rectArray)
        {
            return;
        }

        var appearances = Document.Follow(widget[PdfName.Get("AP")]) as PdfDictionary;
        var normal = appearances is null ? null : Document.Follow(appearances[PdfName.Get("N")]);

        // PdfStream derives from PdfDictionary, so the state-dictionary case must be tested
        // second: matching on PdfDictionary first swallows every text field, whose /N is a stream.
        if (normal is not PdfStream && normal is PdfDictionary states)
        {
            // For a checkbox or radio button, /N is a dictionary of states and /AS names which
            // one is currently showing.
            var state = widget.GetName(PdfName.Get("AS")) ?? "Off";
            normal = Document.Follow(states[PdfName.Get(state)]);
        }

        if (normal is not PdfStream appearance)
        {
            return;
        }

        var rect = PdfRectangle.FromArray(rectArray);
        var name = "OfnFlat" + Guid.NewGuid().ToString("N")[..8];

        var resources = page.Resources;
        if (resources.Get(PdfName.XObject) is not PdfDictionary xobjects)
        {
            xobjects = new PdfDictionary();
            resources[PdfName.XObject] = xobjects;
        }

        xobjects[PdfName.Get(name)] = Document.ReferenceTo(appearance);

        // The appearance's BBox is its own coordinate space; placing it means translating that
        // space to the widget's rectangle on the page.
        var bbox = appearance.Get(PdfName.Get("BBox")) is PdfArray box
            ? PdfRectangle.FromArray(box)
            : new PdfRectangle(0, 0, rect.Width, rect.Height);

        var scaleX = bbox.Width > 0 ? rect.Width / bbox.Width : 1;
        var scaleY = bbox.Height > 0 ? rect.Height / bbox.Height : 1;

        var content = string.Create(CultureInfo.InvariantCulture,
            $"q\n{scaleX:0.####} 0 0 {scaleY:0.####} {rect.Left:0.###} {rect.Bottom:0.###} cm\n/{name} Do\nQ\n");

        page.AppendContent(Encoding.Latin1.GetBytes(content));
    }

    internal PdfDictionary AppearanceResources()
    {
        // The generated appearances reference /Helv, so the resource must exist. Reusing the form's
        // own /DR when it has one keeps the filled text in the font the form designer chose.
        if (Document.Follow(Dictionary[PdfName.Get("DR")]) is PdfDictionary existing &&
            Document.Follow(existing[PdfName.Font]) is PdfDictionary fonts &&
            fonts.ContainsKey(PdfName.Get("Helv")))
        {
            return existing;
        }

        var fontDictionary = new PdfDictionary();
        fontDictionary[PdfName.Get("Helv")] =
            Document.AddObject(StandardFonts.CreateFontDictionary(StandardFont.Helvetica));

        var resources = new PdfDictionary();
        resources[PdfName.Font] = fontDictionary;

        Dictionary[PdfName.Get("DR")] = resources;
        return resources;
    }

    /// <inheritdoc />
    public IEnumerator<PdfFormField> GetEnumerator() => _fields.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
