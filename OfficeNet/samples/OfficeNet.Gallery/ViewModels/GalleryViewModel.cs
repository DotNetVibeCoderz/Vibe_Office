// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OfficeNet.Core;
using OfficeNet.Gallery.Demos;

namespace OfficeNet.Gallery.ViewModels;

/// <summary>A demo as the catalog list shows it.</summary>
internal sealed partial class DemoItem(Demo demo) : ObservableObject
{
    public Demo Demo { get; } = demo;

    public string Title => Demo.Title;

    public string Library => Demo.Library;

    public string Summary => Demo.Summary;

    /// <summary>The library's identity colour, used for the spine on the list row.</summary>
    public string Accent => Demo.Library switch
    {
        "WordNet" => "#2B579A",
        "ExcelNet" => "#217346",
        "PowerPointNet" => "#D24726",
        _ => "#B30B00",
    };

    /// <summary>A four-letter tag, so the spine reads at a glance.</summary>
    public string Tag => Demo.Library switch
    {
        "WordNet" => "DOCX",
        "ExcelNet" => "XLSX",
        "PowerPointNet" => "PPTX",
        _ => "PDF",
    };
}

/// <summary>The demo browser: pick a demo, run it, see the output and the code that made it.</summary>
internal sealed partial class GalleryViewModel : ObservableObject
{
    private byte[] _lastBytes = [];
    private string _lastFileName = string.Empty;

    public GalleryViewModel()
    {
        Demos = [.. DemoCatalog.All.Select(d => new DemoItem(d))];
        Selected = Demos.FirstOrDefault();
    }

    public ObservableCollection<DemoItem> Demos { get; }

    [ObservableProperty]
    public partial DemoItem? Selected { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<Bitmap> Pages { get; set; } = [];

    [ObservableProperty]
    public partial string Source { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Log { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    partial void OnSelectedChanged(DemoItem? value)
    {
        Source = value?.Demo.Source ?? string.Empty;
        Pages = [];
        Log = string.Empty;
        Status = string.Empty;

        if (value is not null)
        {
            _ = RunAsync();
        }
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (Selected is not { } item || IsRunning)
        {
            return;
        }

        IsRunning = true;
        Status = "Running…";

        try
        {
            // Off the UI thread: building and rendering a document is CPU work measured in tens of
            // milliseconds, and a gallery that freezes while it demonstrates a library makes a poor
            // argument for the library.
            var result = await Task.Run(item.Demo.Run);

            Pages = [.. result.Pages.Select(png =>
            {
                using var stream = new MemoryStream(png, writable: false);
                return new Bitmap(stream);
            })];

            Log = result.Log;
            _lastBytes = result.Bytes;
            _lastFileName = result.FileName;

            Status = $"{result.Pages.Count} page{(result.Pages.Count == 1 ? "" : "s")} · " +
                     $"{result.Bytes.Length / 1024.0:0.#} KB";
        }
        catch (OfficeNetException ex)
        {
            Log = ex.Message;
            Status = "Failed";
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Writes the document the demo produced next to the executable.</summary>
    [RelayCommand]
    private void Save()
    {
        if (_lastBytes.Length == 0)
        {
            return;
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OfficeNet Gallery");

        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, _lastFileName);
        File.WriteAllBytes(path, _lastBytes);

        Status = $"Saved to {path}";
    }
}
