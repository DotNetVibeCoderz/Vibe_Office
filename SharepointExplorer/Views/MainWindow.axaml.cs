using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using SharepointExplorer.Models;
using SharepointExplorer.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SharepointExplorer.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is MainWindowViewModel vm)
            {
                vm.RequestInput = async (prompt) =>
                {
                    var inputWin = new InputWindow(prompt);
                    await inputWin.ShowDialog(this);
                    return inputWin.IsCancelled ? null : inputWin.Result;
                };

                vm.RequestSaveFile = async (fileName) =>
                {
                    var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "Save File",
                        SuggestedFileName = fileName
                    });

                    if (file != null)
                    {
                        return await file.OpenWriteAsync();
                    }
                    return null;
                };
            }
        }

        private void DataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && sender is DataGrid grid)
            {
                vm.SelectedGridItems.Clear();
                foreach (var item in grid.SelectedItems)
                {
                    if (item is SPNode node)
                        vm.SelectedGridItems.Add(node);
                }
            }
        }

        private void DataGrid_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && sender is DataGrid grid)
            {
                var item = grid.SelectedItem as SPNode;
                if (item != null)
                {
                    vm.EnterFolderCommand.Execute(item).Subscribe();
                }
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void About_Click(object sender, RoutedEventArgs e)
        {
            var win = new Window
            {
                Width = 300, Height = 150,
                Title = "About",
                Content = new TextBlock { Text = "SharePoint Explorer\nVersion 1.0\nCreated by Jacky", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            await win.ShowDialog(this);
        }
    }
}