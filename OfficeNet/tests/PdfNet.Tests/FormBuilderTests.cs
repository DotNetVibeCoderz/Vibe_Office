// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using PdfNet.Document;
using PdfNet.Forms;
using PdfNet.Objects;
using Xunit;

namespace PdfNet.Tests;

public class FormBuilderTests
{
    private static PdfRectangle Box(double x, double y, double w, double h) => PdfRectangle.FromSize(x, y, w, h);

    /// <summary>A one-page document with one field of every kind.</summary>
    private static byte[] Build()
    {
        using var pdf = PdfDocument.Create();
        var page = pdf.Pages.Add();
        var form = AcroForm.OpenOrCreate(pdf);

        form.AddTextField(page, "nama", Box(72, 700, 240, 22), "Kang Fadhil", required: true);
        form.AddTextField(page, "catatan", Box(72, 600, 240, 80), "baris satu\nbaris dua", multiline: true);
        form.AddTextField(page, "kode", Box(72, 560, 80, 22), maxLength: 6);
        form.AddCheckBox(page, "setuju", Box(72, 520, 16, 16), isChecked: true);
        form.AddRadioGroup("ukuran",
        [
            (page, Box(72, 480, 16, 16), "S"),
            (page, Box(110, 480, 16, 16), "M"),
            (page, Box(148, 480, 16, 16), "L"),
        ], selected: "M");
        form.AddComboBox(page, "kota", Box(72, 430, 160, 22), ["Jakarta", "Bandung", "Medan"], "Bandung");
        form.AddListBox(page, "warna", Box(260, 380, 120, 70), ["Merah", "Hijau", "Biru"], "Hijau");
        form.AddPushButton(page, "situs", Box(72, 380, 100, 26), "Buka situs", "https://example.org/");

        return pdf.ToArray();
    }

    [Fact]
    public void EveryFieldTypeIsCreatedAndReadBack()
    {
        using var pdf = PdfDocument.Open(Build());
        var form = AcroForm.Open(pdf)!;

        Assert.Equal(PdfFieldType.Text, form["nama"]!.FieldType);
        Assert.Equal("Kang Fadhil", form["nama"]!.Value);
        Assert.True(form["nama"]!.IsRequired);
        Assert.True(form["catatan"]!.IsMultiline);
        Assert.Equal(6, form["kode"]!.MaxLength);

        Assert.Equal(PdfFieldType.Checkbox, form["setuju"]!.FieldType);
        Assert.True(form["setuju"]!.IsChecked);

        Assert.Equal(PdfFieldType.RadioButton, form["ukuran"]!.FieldType);
        Assert.Equal("M", form["ukuran"]!.Value);
        Assert.Equal(["S", "M", "L"], form["ukuran"]!.OnStates);

        Assert.Equal(PdfFieldType.ComboBox, form["kota"]!.FieldType);
        Assert.Equal(["Jakarta", "Bandung", "Medan"], form["kota"]!.Options);
        Assert.Equal("Bandung", form["kota"]!.Value);
        Assert.Equal(PdfFieldType.ListBox, form["warna"]!.FieldType);
        Assert.Equal(PdfFieldType.PushButton, form["situs"]!.FieldType);
    }

    [Fact]
    public void EveryWidgetIsOnThePageAndHasAnAppearance()
    {
        // A widget missing from /Annots is invisible; one without /AP is an empty box in every
        // viewer that ignores /NeedAppearances.
        using var pdf = PdfDocument.Open(Build());
        var annotations = pdf.Pages[0].Annotations.Select(a => pdf.Follow(a)).OfType<PdfDictionary>().ToList();

        Assert.Equal(10, annotations.Count);   // 8 fields, the radio group being three widgets

        foreach (var widget in annotations)
        {
            Assert.Equal("Widget", (widget[PdfName.Subtype] as PdfName)?.Value);
            Assert.NotNull(pdf.Follow(widget[PdfName.Get("AP")]));
            Assert.Equal(4, (pdf.Follow(widget[PdfName.Get("F")]) as PdfNumber)?.LongValue);
        }
    }

    [Fact]
    public void ButtonsCarryBothStatesAndTheSelectedOneIsShown()
    {
        using var pdf = PdfDocument.Open(Build());
        var form = AcroForm.Open(pdf)!;

        var radios = form["ukuran"]!.Widgets().ToList();
        Assert.Equal(["Off", "M", "Off"], radios.Select(w => (w[PdfName.Get("AS")] as PdfName)!.Value));

        var normal = (PdfDictionary)pdf.Follow(((PdfDictionary)pdf.Follow(radios[0][PdfName.Get("AP")])!)[PdfName.Get("N")])!;
        Assert.True(normal.ContainsKey(PdfName.Get("S")));
        Assert.True(normal.ContainsKey(PdfName.Get("Off")));
    }

    [Fact]
    public void CreatedFieldsCanBeFilledAndFlattened()
    {
        using var pdf = PdfDocument.Open(Build());
        var form = AcroForm.Open(pdf)!;

        form.Fill(new Dictionary<string, string?>
        {
            ["nama"] = "Ani",
            ["setuju"] = "Off",
            ["ukuran"] = "L",
            ["kota"] = "Medan",
        });

        Assert.Equal("L", form["ukuran"]!.Value);
        Assert.False(form["setuju"]!.IsChecked);

        form.Flatten();
        var text = pdf.ExtractText();
        Assert.Contains("Ani", text);
        Assert.Contains("Medan", text);
    }

    [Fact]
    public void NamesAreValidated()
    {
        using var pdf = PdfDocument.Create();
        var page = pdf.Pages.Add();
        var form = AcroForm.OpenOrCreate(pdf);
        form.AddTextField(page, "nama", Box(0, 0, 10, 10));

        Assert.Throws<ArgumentException>(() => form.AddTextField(page, "nama", Box(0, 20, 10, 10)));
        Assert.Throws<ArgumentException>(() => form.AddTextField(page, "grup.nama", Box(0, 40, 10, 10)));
        Assert.Throws<ArgumentException>(() => form.AddRadioGroup("r", [(page, Box(0, 60, 10, 10), "a")]));
        Assert.Throws<ArgumentException>(() => form.AddRadioGroup("r",
            [(page, Box(0, 60, 10, 10), "a"), (page, Box(20, 60, 10, 10), "b")], selected: "c"));
    }

    [Fact]
    public void AnEmptyTextFieldStillDrawsItsBox()
    {
        using var pdf = PdfDocument.Open(Build());
        var form = AcroForm.Open(pdf)!;
        var widget = form["kode"]!.Widgets().Single();
        var appearance = (PdfStream)pdf.Follow(((PdfDictionary)pdf.Follow(widget[PdfName.Get("AP")])!)[PdfName.Get("N")])!;

        var content = appearance.DecodedText;
        Assert.Contains(" re\nf", content);   // background
        Assert.Contains(" re\nS", content);   // border
    }
}
