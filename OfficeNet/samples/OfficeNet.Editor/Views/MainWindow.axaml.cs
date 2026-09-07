// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OfficeNet.Editor.ViewModels;

namespace OfficeNet.Editor.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The view owns the file dialogs because they need a window; the view model owns the
        // decision about when to ask. Handing it a callback keeps the storage API out of the model.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is EditorViewModel viewModel)
            {
                viewModel.AskForPath = AskForPathAsync;
            }
        };
    }

    private async Task<string?> AskForPathAsync(bool saving)
    {
        var word = new FilePickerFileType("Word document") { Patterns = ["*.docx"] };
        var excel = new FilePickerFileType("Excel workbook") { Patterns = ["*.xlsx"] };

        if (saving)
        {
            var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save document",
                FileTypeChoices = [word, excel],
                SuggestedFileName = (DataContext as EditorViewModel)?.DocumentName,
            });

            return target?.TryGetLocalPath();
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a document",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Word or Excel") { Patterns = ["*.docx", "*.xlsx"] },
                word,
                excel,
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
