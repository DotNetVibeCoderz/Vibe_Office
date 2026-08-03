using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AutoWork.Desktop.Views;

public partial class IntegrationsView : UserControl
{
    public IntegrationsView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
