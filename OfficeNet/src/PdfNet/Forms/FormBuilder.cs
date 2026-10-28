// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text;
using PdfNet.Content;
using PdfNet.Document;
using PdfNet.Objects;

namespace PdfNet.Forms;

/// <summary>
/// Creating fields: text, checkbox, radio group, combo box, list box and push button.
/// </summary>
/// <remarks>
/// <para>
/// A field is two things at once here — the field (its name, type and value) and the widget
/// annotation that puts it on a page — merged into one dictionary, except for a radio group, whose
/// buttons are separate widgets under one field. The widget must be listed in the page's
/// <c>/Annots</c> <em>and</em> the field in the form's <c>/Fields</c>; a field in only one of them
/// is either invisible or not a field.
/// </para>
/// <para>
/// <b>Every field is given its appearance.</b> <c>/NeedAppearances</c> asks the viewer to draw the
/// fields itself and is widely ignored — by browser viewers, mobile readers and print pipelines — so
/// a field without an appearance stream is an empty rectangle there. Checkboxes and radio buttons
/// get both states, drawn as paths rather than ZapfDingbats glyphs so no font is needed to show a tick.
/// </para>
/// </remarks>
public sealed partial class AcroForm
{
    private const int FlagRequired = 1 << 1;
    private const int FlagMultiline = 1 << 12;
    private const int FlagPassword = 1 << 13;
    private const int FlagNoToggleToOff = 1 << 14;
    private const int FlagRadio = 1 << 15;
    private const int FlagPushButton = 1 << 16;
    private const int FlagCombo = 1 << 17;
    private const int FlagEdit = 1 << 18;

    /// <summary>Adds a text field.</summary>
    /// <param name="page">The page it sits on.</param>
    /// <param name="name">A name unique in the form, without dots (a dot separates a group name).</param>
    /// <param name="rect">Its box, in points from the page's bottom-left.</param>
    /// <param name="value">An initial value.</param>
    /// <param name="multiline">Several lines, wrapped at line breaks.</param>
    /// <param name="maxLength">The most characters it accepts; 0 for no limit.</param>
    /// <param name="password">Shown as dots while typing.</param>
    /// <param name="fontSize">0 sizes the text to the box.</param>
    /// <param name="required">Must be filled before the form is submitted.</param>
    public PdfFormField AddTextField(PdfPage page, string name, PdfRectangle rect, string? value = null,
        bool multiline = false, int maxLength = 0, bool password = false, double fontSize = 0, bool required = false)
    {
        var flags = (multiline ? FlagMultiline : 0) | (password ? FlagPassword : 0) | (required ? FlagRequired : 0);
        var field = NewWidget(page, name, rect, "Tx", flags, fontSize);

        if (maxLength > 0)
        {
            field[PdfName.Get("MaxLen")] = new PdfNumber(maxLength);
        }

        return Register(page, field, name, value ?? string.Empty);
    }

    /// <summary>Adds a checkbox whose on-state is <c>Yes</c>.</summary>
    public PdfFormField AddCheckBox(PdfPage page, string name, PdfRectangle rect, bool isChecked = false)
    {
        var field = NewWidget(page, name, rect, "Btn", 0, 0);
        field[PdfName.Get("AP")] = ButtonAppearances(rect, "Yes", Tick);
        field[PdfName.Get("V")] = PdfName.Get(isChecked ? "Yes" : "Off");
        field[PdfName.Get("AS")] = PdfName.Get(isChecked ? "Yes" : "Off");

        return Register(page, field, name, value: null);
    }

    /// <summary>
    /// Adds a radio group: one field, one button per option, of which at most one is on.
    /// </summary>
    /// <param name="name">The group's name.</param>
    /// <param name="options">Each button's page, box and the value it stands for.</param>
    /// <param name="selected">The value initially chosen, or <c>null</c> for none.</param>
    public PdfFormField AddRadioGroup(string name, IReadOnlyList<(PdfPage Page, PdfRectangle Rect, string Value)> options,
        string? selected = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateName(name);

        if (options.Count < 2)
        {
            throw new ArgumentException("A radio group needs at least two options.", nameof(options));
        }

        if (options.Select(o => o.Value).Distinct(StringComparer.Ordinal).Count() != options.Count)
        {
            throw new ArgumentException("Each radio option needs a different value.", nameof(options));
        }

        if (selected is not null && options.All(o => o.Value != selected))
        {
            throw new ArgumentException($"'{selected}' is not one of the options.", nameof(selected));
        }

        var parent = new PdfDictionary();
        parent.SetName(PdfName.Get("FT"), "Btn");
        parent[PdfName.Get("T")] = new PdfString(name);
        parent[PdfName.Get("Ff")] = new PdfNumber(FlagRadio | FlagNoToggleToOff);
        parent[PdfName.Get("V")] = PdfName.Get(selected ?? "Off");

        var parentReference = Document.AddObject(parent);
        var kids = new PdfArray();

        foreach (var (page, rect, value) in options)
        {
            var widget = new PdfDictionary();
            widget[PdfName.Type] = PdfName.Get("Annot");
            widget.SetName(PdfName.Subtype, "Widget");
            widget[PdfName.Get("Parent")] = parentReference;
            widget[PdfName.Get("Rect")] = rect.ToArray();
            widget[PdfName.Get("F")] = new PdfNumber(4);
            widget[PdfName.Get("P")] = Document.ReferenceTo(page.Dictionary);
            widget[PdfName.Get("MK")] = Characteristics();
            widget[PdfName.Get("AP")] = ButtonAppearances(rect, value, Dot);
            widget[PdfName.Get("AS")] = PdfName.Get(value == selected ? value : "Off");

            var reference = Document.AddObject(widget);
            kids.Add(reference);
            page.Annotations.Add(reference);
        }

        parent[PdfName.Get("Kids")] = kids;
        Fields_().Add(parentReference);

        var field = new PdfFormField(this, parent, name);
        _fields.Add(field);
        return field;
    }

    /// <summary>Adds a drop-down list.</summary>
    /// <remarks><c>editable</c> lets the user type a value that is not in the list.</remarks>
    public PdfFormField AddComboBox(PdfPage page, string name, PdfRectangle rect, IReadOnlyList<string> options,
        string? value = null, bool editable = false)
    {
        var field = NewChoice(page, name, rect, options, FlagCombo | (editable ? FlagEdit : 0));
        return Register(page, field, name, value ?? string.Empty);
    }

    /// <summary>Adds a scrolling list box.</summary>
    public PdfFormField AddListBox(PdfPage page, string name, PdfRectangle rect, IReadOnlyList<string> options,
        string? value = null)
    {
        var field = NewChoice(page, name, rect, options, 0);
        return Register(page, field, name, value ?? string.Empty);
    }

    /// <summary>Adds a push button, optionally opening a link when pressed.</summary>
    public PdfFormField AddPushButton(PdfPage page, string name, PdfRectangle rect, string caption, string? url = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caption);

        var field = NewWidget(page, name, rect, "Btn", FlagPushButton, 0);
        var characteristics = Characteristics(background: 0.85);
        characteristics[PdfName.Get("CA")] = new PdfString(caption);
        field[PdfName.Get("MK")] = characteristics;

        var size = Math.Max(6, Math.Min(12, rect.Height * 0.55));
        var width = Font.Measure(caption, size);
        var x = Math.Max(2, (rect.Width - width) / 2);
        var baseline = (rect.Height - size) / 2 + size * 0.22;

        var content = new StringBuilder();
        Box(content, rect, background: 0.85);
        content.Append(CultureInfo.InvariantCulture,
            $"BT\n0 g\n/{FontResource} {size:0.##} Tf\n1 0 0 1 {x:0.##} {baseline:0.##} Tm\n{ShowText(caption)} Tj\nET\n");

        // A button's normal appearance is a dictionary of states, even with only one: PDF/A
        // (ISO 19005-2, 6.3.3) refuses a bare stream on any Btn widget, and every viewer reads both.
        var states = new PdfDictionary();
        states[PdfName.Get("On")] = Document.AddObject(AppearanceStream(rect, content.ToString()));

        var appearances = new PdfDictionary();
        appearances[PdfName.Get("N")] = states;
        field[PdfName.Get("AP")] = appearances;
        field[PdfName.Get("AS")] = PdfName.Get("On");

        if (url is not null)
        {
            var action = new PdfDictionary();
            action.SetName(PdfName.Get("S"), "URI");
            action[PdfName.Get("URI")] = new PdfString(url);
            field[PdfName.Get("A")] = action;
        }

        return Register(page, field, name, value: null);
    }

    // ---- Building blocks -----------------------------------------------------------------------

    private PdfDictionary NewWidget(PdfPage page, string name, PdfRectangle rect, string type, int flags, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(page);
        ValidateName(name);

        var field = new PdfDictionary();
        field[PdfName.Type] = PdfName.Get("Annot");
        field.SetName(PdfName.Subtype, "Widget");
        field.SetName(PdfName.Get("FT"), type);
        field[PdfName.Get("T")] = new PdfString(name);
        field[PdfName.Get("Rect")] = rect.ToArray();

        // Print flag: without it the field is on screen and missing from every printout.
        field[PdfName.Get("F")] = new PdfNumber(4);
        field[PdfName.Get("P")] = Document.ReferenceTo(page.Dictionary);
        field[PdfName.Get("MK")] = Characteristics();
        field.Set(PdfName.Get("DA"), $"/{FontResource} {fontSize.ToString("0.##", CultureInfo.InvariantCulture)} Tf 0 g");

        if (flags != 0)
        {
            field[PdfName.Get("Ff")] = new PdfNumber(flags);
        }

        return field;
    }

    private PdfDictionary NewChoice(PdfPage page, string name, PdfRectangle rect, IReadOnlyList<string> options, int flags)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Count == 0)
        {
            throw new ArgumentException("A choice field needs at least one option.", nameof(options));
        }

        var field = NewWidget(page, name, rect, "Ch", flags, 0);
        field[PdfName.Get("Opt")] = new PdfArray([.. options.Select(o => (PdfObject)new PdfString(o))]);
        return field;
    }

    /// <summary>Adds the widget to the page and the field to the form, then draws its value.</summary>
    private PdfFormField Register(PdfPage page, PdfDictionary field, string name, string? value)
    {
        var reference = Document.AddObject(field);
        page.Annotations.Add(reference);
        Fields_().Add(reference);

        var created = new PdfFormField(this, field, name);
        _fields.Add(created);

        // SetValue draws the appearance, border included; an empty value still needs the box.
        if (value is not null)
        {
            created.SetValue(value);
        }

        return created;
    }

    private PdfArray Fields_()
    {
        if (Dictionary.Get(PdfName.Get("Fields")) is PdfArray fields)
        {
            return fields;
        }

        var created = new PdfArray();
        Dictionary[PdfName.Get("Fields")] = created;
        return created;
    }

    private void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Contains('.'))
        {
            throw new ArgumentException(
                $"'{name}' contains a dot, which separates a group from a field in a full name.", nameof(name));
        }

        if (_fields.Any(f => f.FullName == name))
        {
            throw new ArgumentException($"The form already has a field named '{name}'.", nameof(name));
        }
    }

    /// <summary>Border and background a viewer uses when it redraws the field itself.</summary>
    private static PdfDictionary Characteristics(double background = 1)
    {
        var characteristics = new PdfDictionary();
        characteristics[PdfName.Get("BC")] = new PdfArray(0.5, 0.5, 0.5);
        characteristics[PdfName.Get("BG")] = new PdfArray(background, background, background);
        return characteristics;
    }

    /// <summary>The on and off appearances of a checkbox or radio button.</summary>
    private PdfDictionary ButtonAppearances(PdfRectangle rect, string onState, Action<StringBuilder, PdfRectangle> mark)
    {
        var on = new StringBuilder();
        Box(on, rect);
        mark(on, rect);

        var off = new StringBuilder();
        Box(off, rect);

        var normal = new PdfDictionary();
        normal[PdfName.Get(onState)] = Document.AddObject(AppearanceStream(rect, on.ToString()));
        normal[PdfName.Get("Off")] = Document.AddObject(AppearanceStream(rect, off.ToString()));

        var appearances = new PdfDictionary();
        appearances[PdfName.Get("N")] = normal;
        return appearances;
    }

    private static void Box(StringBuilder content, PdfRectangle rect, double background = 1)
    {
        content.Append(CultureInfo.InvariantCulture,
            $"q\n{background:0.##} g\n0 0 {rect.Width:0.##} {rect.Height:0.##} re\nf\n" +
            $"0.5 G\n1 w\n0.5 0.5 {rect.Width - 1:0.##} {rect.Height - 1:0.##} re\nS\nQ\n");
    }

    /// <summary>A tick, drawn as a stroked path.</summary>
    private static void Tick(StringBuilder content, PdfRectangle rect)
    {
        var w = rect.Width;
        var h = rect.Height;
        var line = Math.Max(1, Math.Min(w, h) * 0.12);

        content.Append(CultureInfo.InvariantCulture,
            $"q\n0 G\n{line:0.##} w\n1 J\n1 j\n{w * 0.22:0.##} {h * 0.52:0.##} m\n{w * 0.42:0.##} {h * 0.28:0.##} l\n" +
            $"{w * 0.78:0.##} {h * 0.76:0.##} l\nS\nQ\n");
    }

    /// <summary>A filled dot, approximated with four Bézier arcs.</summary>
    private static void Dot(StringBuilder content, PdfRectangle rect)
    {
        var cx = rect.Width / 2;
        var cy = rect.Height / 2;
        var r = Math.Min(rect.Width, rect.Height) * 0.25;
        var k = r * 0.5523;

        content.Append(CultureInfo.InvariantCulture,
            $"q\n0 g\n{cx + r:0.###} {cy:0.###} m\n" +
            $"{cx + r:0.###} {cy + k:0.###} {cx + k:0.###} {cy + r:0.###} {cx:0.###} {cy + r:0.###} c\n" +
            $"{cx - k:0.###} {cy + r:0.###} {cx - r:0.###} {cy + k:0.###} {cx - r:0.###} {cy:0.###} c\n" +
            $"{cx - r:0.###} {cy - k:0.###} {cx - k:0.###} {cy - r:0.###} {cx:0.###} {cy - r:0.###} c\n" +
            $"{cx + k:0.###} {cy - r:0.###} {cx + r:0.###} {cy - k:0.###} {cx + r:0.###} {cy:0.###} c\nf\nQ\n");
    }

    private PdfStream AppearanceStream(PdfRectangle rect, string content)
    {
        var stream = new PdfStream();
        stream[PdfName.Type] = PdfName.XObject;
        stream.SetName(PdfName.Subtype, "Form");
        stream[PdfName.Get("BBox")] = new PdfArray(0, 0, rect.Width, rect.Height);
        stream[PdfName.Resources] = AppearanceResources();
        stream.SetText(content);
        return stream;
    }
}
