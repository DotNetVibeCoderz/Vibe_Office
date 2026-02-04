using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using SharepointExplorer.ViewModels;
using System;

namespace SharepointExplorer.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }
        
        protected override void OnDataContextChanged(EventArgs e)
        {
             base.OnDataContextChanged(e);
             if (DataContext is LoginViewModel vm)
             {
                 vm.LoginSuccess += () =>
                 {
                     // Open Main Window
                     var mainVm = new MainWindowViewModel();
                     var mainWindow = new MainWindow
                     {
                         DataContext = mainVm
                     };
                     mainWindow.Show();
                     this.Close();
                 };
             }
        }
    }
}