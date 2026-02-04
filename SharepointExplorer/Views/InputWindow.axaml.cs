using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SharepointExplorer.Views
{
    public partial class InputWindow : Window
    {
        public string Result { get; private set; } = string.Empty;
        public bool IsCancelled { get; private set; } = true;

        public InputWindow(string prompt, string defaultValue = "")
        {
            InitializeComponent();
            PromptText.Text = prompt;
            InputBox.Text = defaultValue;
        }
        
        // Default constructor for preview
        public InputWindow() { InitializeComponent(); }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Result = InputBox.Text ?? "";
            IsCancelled = false;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}