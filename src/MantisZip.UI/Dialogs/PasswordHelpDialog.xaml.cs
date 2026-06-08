using System.Windows;

namespace MantisZip.UI;

public partial class PasswordHelpDialog : Window
{
    public PasswordHelpDialog()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
