using System.Windows;

namespace MantisZip.UI;

public partial class LogPrivacyHelpDialog : Window
{
    public LogPrivacyHelpDialog()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
