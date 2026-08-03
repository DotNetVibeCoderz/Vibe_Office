using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AutoWork.Desktop.Views;

public partial class WorkView : UserControl
{
    public WorkView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
