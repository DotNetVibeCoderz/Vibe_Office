using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AutoWork.Desktop.Views;

public partial class KnowledgeView : UserControl
{
    public KnowledgeView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
