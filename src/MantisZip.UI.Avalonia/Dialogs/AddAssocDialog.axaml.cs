using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs
{
    public partial class AddAssocDialog : Window
    {
        public string Extension { get; private set; } = "";

        public AddAssocDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void Ok_Click(object? sender, RoutedEventArgs e)
        {
            var raw = InputTextBox.Text ?? "";
            var norm = raw.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(norm))
            {
                _ = AppMessageBox.Show(LocalizationManager.T("Settings_Assoc_CustomInvalid"), "", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            if (!norm.StartsWith(".")) norm = "." + norm;
            if (norm.Length > 10)
            {
                _ = AppMessageBox.Show(LocalizationManager.T("Settings_Assoc_CustomInvalid"), "", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            Extension = norm;
            Close(true);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
