// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using CommunityToolkit.Mvvm.ComponentModel;

namespace OfficeNet.Gallery.ViewModels;

/// <summary>The window: a demo browser and a chatbot, side by side as tabs.</summary>
internal sealed partial class MainViewModel : ObservableObject
{
    public GalleryViewModel Gallery { get; } = new();

    public ChatViewModel Chat { get; } = new();
}
