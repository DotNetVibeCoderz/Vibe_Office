using AutoWork.Desktop.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace AutoWork.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The folder picker needs a TopLevel, which only the view can reach — so the window
        // supplies it and the view model stays free of Avalonia types.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel model)
                model.SettingsPage.PickFolder = PickFolderAsync;
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Grant AutoWork access to a folder",
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
