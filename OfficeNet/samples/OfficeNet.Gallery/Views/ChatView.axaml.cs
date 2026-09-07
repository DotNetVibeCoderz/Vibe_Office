// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia.Controls;
using Avalonia.Input;
using OfficeNet.Gallery.ViewModels;

namespace OfficeNet.Gallery.Views;

public partial class ChatView : UserControl
{
    public ChatView() => InitializeComponent();

    /// <summary>Enter sends; Shift+Enter inserts a newline.</summary>
    /// <remarks>
    /// The convention every chat interface uses. Without it the box needs a mouse trip to the Send
    /// button for every message, which is the difference between usable and merely present.
    /// </remarks>
    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }

        e.Handled = true;

        if (DataContext is ChatViewModel viewModel && viewModel.SendCommand.CanExecute(null))
        {
            viewModel.SendCommand.Execute(null);
        }
    }
}
