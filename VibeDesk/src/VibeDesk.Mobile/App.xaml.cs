namespace VibeDesk.Mobile;

// Fully qualified: on the Android target `Application` also resolves to the Android.App namespace,
// which shadows the MAUI type and fails to compile.
public partial class App : Microsoft.Maui.Controls.Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Microsoft.Maui.Controls.Window CreateWindow(IActivationState? activationState) =>
        new(new MainPage()) { Title = "VibeDesk" };
}
