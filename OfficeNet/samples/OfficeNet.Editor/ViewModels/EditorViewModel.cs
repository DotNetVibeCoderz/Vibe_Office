// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExcelNet;
using OfficeNet.Core;
using OfficeNet.Rendering;
using WordNet;

namespace OfficeNet.Editor.ViewModels;

/// <summary>One editable row of the open document.</summary>
/// <remarks>
/// Word and Excel are edited through the same list on purpose: a paragraph and a cell are both
/// "a piece of addressable text", and modelling them separately would double the UI for no gain at
/// this scale. <see cref="Address"/> is what tells them apart on screen.
/// </remarks>
internal sealed partial class EditableLine : ObservableObject
{
    private readonly Action<string> _apply;

    public EditableLine(string address, string text, string? style, Action<string> apply)
    {
        Address = address;
        Style = style;
        _apply = apply;
        _text = text;
    }

    public string Address { get; }

    public string? Style { get; }

    private string _text;

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
            {
                _apply(value);
            }
        }
    }
}

/// <summary>A light editor: open a .docx or .xlsx, change the text, see the page, save.</summary>
internal sealed partial class EditorViewModel : ObservableObject
{
    private WordDocument? _word;
    private Workbook? _workbook;

    [ObservableProperty]
    public partial string DocumentName { get; set; } = "No document open";

    [ObservableProperty]
    public partial string Kind { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; set; } = "Open a .docx or .xlsx, or start a new one.";

    [ObservableProperty]
    public partial bool HasDocument { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<Bitmap> Pages { get; set; } = [];

    public ObservableCollection<EditableLine> Lines { get; } = [];

    /// <summary>Where Open and Save dialogs are answered from; set by the view.</summary>
    public Func<bool, Task<string?>>? AskForPath { get; set; }

    // ---- Opening -------------------------------------------------------------------------------

    [RelayCommand]
    private void NewWord()
    {
        Close();

        var document = WordDocument.Create();
        document.AddHeading("Judul Dokumen", 1);
        document.AddParagraph("Ketik di panel kiri, lalu tekan Refresh untuk melihat halamannya.");

        _word = document;
        DocumentName = "Untitled.docx";
        Kind = "Word";
        HasDocument = true;

        Reload();
        Status = "New document.";
    }

    [RelayCommand]
    private void NewExcel()
    {
        Close();

        var workbook = Workbook.Create("Sheet1");
        var sheet = workbook["Sheet1"];

        sheet.WriteHeader("A1", ["Item", "Jumlah"]);
        sheet["A2"].Set("Contoh");
        sheet["B2"].Set(1);

        _workbook = workbook;
        DocumentName = "Untitled.xlsx";
        Kind = "Excel";
        HasDocument = true;

        Reload();
        Status = "New workbook.";
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (AskForPath is null)
        {
            return;
        }

        var path = await AskForPath(false);

        if (path is null)
        {
            return;
        }

        try
        {
            Close();

            switch (Office.DetectFormat(path))
            {
                case OfficeFormat.Word:
                    _word = WordDocument.Open(path);
                    Kind = "Word";
                    break;

                case OfficeFormat.Excel:
                    _workbook = Workbook.Open(path);
                    Kind = "Excel";
                    break;

                default:
                    Status = "This editor opens .docx and .xlsx. Use the dashboard sample to " +
                             "preview PowerPoint and PDF.";
                    return;
            }

            DocumentName = Path.GetFileName(path);
            HasDocument = true;

            Reload();
            Status = $"Opened {DocumentName}.";
        }
        catch (OfficeNetException ex)
        {
            Status = ex.Message;
        }
    }

    // ---- Editing -------------------------------------------------------------------------------

    /// <summary>Rebuilds the editable lines and the rendered preview from the open document.</summary>
    private void Reload()
    {
        Lines.Clear();

        if (_word is { } document)
        {
            foreach (var paragraph in document.Paragraphs)
            {
                var target = paragraph;

                Lines.Add(new EditableLine(
                    $"¶{Lines.Count + 1}",
                    paragraph.Text,
                    paragraph.StyleId,
                    text =>
                    {
                        target.Text = text;
                        IsDirty = true;
                    }));
            }
        }
        else if (_workbook is { } workbook)
        {
            var sheet = workbook.ActiveSheet;

            foreach (var cell in sheet.UsedCells.OrderBy(c => c.Reference.Row)
                         .ThenBy(c => c.Reference.Column))
            {
                var target = cell;

                Lines.Add(new EditableLine(
                    cell.Address,
                    cell.Formula is { Length: > 0 } formula ? "=" + formula : cell.Text,
                    cell.Formula is { Length: > 0 } ? "formula" : null,
                    text =>
                    {
                        // A leading "=" means a formula; anything else is a value. That is the
                        // convention every spreadsheet uses, so it needs no explaining.
                        if (text.StartsWith('='))
                        {
                            target.SetFormula(text[1..]);
                        }
                        else
                        {
                            target.Set(text);
                        }

                        IsDirty = true;
                    }));
            }
        }

        Render();
    }

    [RelayCommand]
    private void Refresh()
    {
        // Excel formulas are recalculated here rather than on every keystroke: the engine walks the
        // dependency graph, and doing that per character would make typing feel heavy.
        _workbook?.Recalculate();

        Render();
        Status = "Preview refreshed.";
    }

    private void Render()
    {
        try
        {
            var options = new RenderOptions { Dpi = 96, MaxPixels = 900, DrawPageBorder = false };

            var pages = _word is { } document
                ? DocumentRenderer.RenderWord(document, options)
                : _workbook is { } workbook
                    ? DocumentRenderer.RenderExcel(workbook, options)
                    : [];

            Pages = [.. pages.Select(png =>
            {
                using var stream = new MemoryStream(png, writable: false);
                return new Bitmap(stream);
            })];
        }
        catch (OfficeNetException ex)
        {
            Status = $"Preview failed: {ex.Message}";
            Pages = [];
        }
    }

    // ---- Saving --------------------------------------------------------------------------------

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (AskForPath is null || !HasDocument)
        {
            return;
        }

        var path = await AskForPath(true);

        if (path is null)
        {
            return;
        }

        try
        {
            if (_word is { } document)
            {
                document.Save(path);
            }
            else if (_workbook is { } workbook)
            {
                workbook.Recalculate();
                workbook.Save(path);
            }

            DocumentName = Path.GetFileName(path);
            IsDirty = false;
            Status = $"Saved to {path}.";
        }
        catch (IOException ex)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private void ExportPdf()
    {
        if (!HasDocument)
        {
            return;
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OfficeNet Editor");

        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, Path.ChangeExtension(DocumentName, ".pdf"));

        try
        {
            if (_word is { } document)
            {
                document.SaveAsPdf(path);
            }
            else if (_workbook is { } workbook)
            {
                workbook.Recalculate();
                workbook.SaveAsPdf(path);
            }

            Status = $"Exported to {path}.";
        }
        catch (OfficeNetException ex)
        {
            Status = ex.Message;
        }
    }

    private void Close()
    {
        _word?.Dispose();
        _workbook?.Dispose();
        _word = null;
        _workbook = null;

        Lines.Clear();
        Pages = [];
        IsDirty = false;
        HasDocument = false;
    }
}
