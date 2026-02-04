using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SharepointExplorer.ViewModels;
using SharepointExplorer.Views;

namespace SharepointExplorer
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // We start with the Login Screen or Main Screen depending on logic.
                // For now, let's start with Login.
                var loginVm = new LoginViewModel();
                var loginWindow = new LoginWindow
                {
                    DataContext = loginVm
                };
                
                desktop.MainWindow = loginWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}