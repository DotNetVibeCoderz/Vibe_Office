using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using SharepointExplorer.Models;
using SharepointExplorer.Services;

namespace SharepointExplorer.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private ObservableCollection<SPNode> _navigationItems;
        public ObservableCollection<SPNode> NavigationItems
        {
            get => _navigationItems;
            set => this.RaiseAndSetIfChanged(ref _navigationItems, value);
        }

        private ObservableCollection<SPNode> _currentFolderItems;
        public ObservableCollection<SPNode> CurrentFolderItems
        {
            get => _currentFolderItems;
            set => this.RaiseAndSetIfChanged(ref _currentFolderItems, value);
        }

        private SPNode? _selectedNavigationItem;
        public SPNode? SelectedNavigationItem
        {
            get => _selectedNavigationItem;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedNavigationItem, value);
                if (value != null)
                {
                    _ = LoadFolderContents(value);
                }
            }
        }

        public ObservableCollection<SPNode> SelectedGridItems { get; } = new ObservableCollection<SPNode>();

        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
        public ReactiveCommand<string, Unit> CreateFolderCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
        public ReactiveCommand<Unit, Unit> DownloadCommand { get; }
        public ReactiveCommand<string, Unit> NewDocumentCommand { get; }
        public ReactiveCommand<SPNode, Unit> EnterFolderCommand { get; }
        
        public Func<string, Task<string?>>? RequestInput { get; set; }
        public Func<string, Task<Stream?>>? RequestSaveFile { get; set; }

        public MainWindowViewModel()
        {
            _navigationItems = new ObservableCollection<SPNode>();
            _currentFolderItems = new ObservableCollection<SPNode>();

            RefreshCommand = ReactiveCommand.CreateFromTask(LoadStructure);
            
            CreateFolderCommand = ReactiveCommand.CreateFromTask<string>(async (title) =>
            {
                if (SelectedNavigationItem == null) return;
                
                string folderName = "New Folder";
                if (RequestInput != null)
                {
                    var input = await RequestInput(title ?? "Folder Name");
                    if (string.IsNullOrWhiteSpace(input)) return;
                    folderName = input;
                }

                await AppServices.SharePoint.CreateFolderAsync(SelectedNavigationItem.ServerRelativeUrl, folderName);
                await LoadFolderContents(SelectedNavigationItem);
            });

            DeleteCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var items = SelectedGridItems.ToList(); // Copy
                foreach (var item in items)
                {
                    await AppServices.SharePoint.DeleteItemAsync(item.ServerRelativeUrl, item.Type);
                }
                if (SelectedNavigationItem != null) await LoadFolderContents(SelectedNavigationItem);
            });

            DownloadCommand = ReactiveCommand.CreateFromTask(PerformDownload);
            
            EnterFolderCommand = ReactiveCommand.CreateFromTask<SPNode>(async (node) =>
            {
                if (node.Type == SPItemType.Folder || node.Type == SPItemType.Library)
                {
                     // Navigate down. Ideally we update the Tree Selection if possible.
                     // For now, we just update the Grid view and set SelectedNavigationItem to this node
                     // This mimics drilling down.
                     SelectedNavigationItem = node;
                }
                else if (node.Type == SPItemType.File)
                {
                    // Add to selection and download
                    SelectedGridItems.Clear();
                    SelectedGridItems.Add(node);
                    await PerformDownload();
                }
            });
            
            NewDocumentCommand = ReactiveCommand.CreateFromTask<string>(async (type) =>
            {
                 if (SelectedNavigationItem == null) return;
                 string ext = type switch { "Word" => "docx", "Excel" => "xlsx", "PowerPoint" => "pptx", _ => "txt" };
                 string name = $"New {type}.{ext}";
                 
                 if (RequestInput != null)
                 {
                      var input = await RequestInput("Document Name");
                      if (!string.IsNullOrWhiteSpace(input)) name = input.EndsWith("."+ext) ? input : input + "." + ext;
                 }

                 using var ms = new MemoryStream();
                 if(type == "Text") { 
                    var writer = new StreamWriter(ms); writer.Write(" "); writer.Flush(); ms.Position=0;
                 }
                 await AppServices.SharePoint.UploadFileAsync(SelectedNavigationItem.ServerRelativeUrl, name, ms);
                 await LoadFolderContents(SelectedNavigationItem);
            });

            // Initial Load
            _ = LoadStructure();
        }

        private async Task LoadStructure()
        {
            var items = await AppServices.SharePoint.GetStructureAsync();
            NavigationItems = new ObservableCollection<SPNode>(items);
        }

        private async Task LoadFolderContents(SPNode node)
        {
            // Clear current items first?
            var items = await AppServices.SharePoint.GetFolderContentsAsync(node.ServerRelativeUrl);
            CurrentFolderItems = new ObservableCollection<SPNode>(items);
        }

        private async Task PerformDownload()
        {
            if (SelectedGridItems.Count == 0) return;
            if (RequestSaveFile == null) return;

            if (SelectedGridItems.Count == 1 && SelectedGridItems[0].Type == SPItemType.File)
            {
                var item = SelectedGridItems[0];
                using var stream = await RequestSaveFile(item.Name);
                if (stream != null)
                {
                    using var spStream = await AppServices.SharePoint.DownloadFileAsync(item.ServerRelativeUrl);
                    await spStream.CopyToAsync(stream);
                }
            }
            else
            {
                using var stream = await RequestSaveFile("Download.zip");
                if (stream != null)
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Create, true);
                    foreach (var item in SelectedGridItems)
                    {
                        if (item.Type == SPItemType.File)
                        {
                            var entry = archive.CreateEntry(item.Name);
                            using var entryStream = entry.Open();
                            using var spStream = await AppServices.SharePoint.DownloadFileAsync(item.ServerRelativeUrl);
                            await spStream.CopyToAsync(entryStream);
                        }
                    }
                }
            }
        }
    }
}