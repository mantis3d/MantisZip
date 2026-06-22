using System.Collections.Generic;
using System.Windows;

namespace MantisZip.UI;

public partial class ElevationFailedDialog : Window
{
    public ElevationFailedDialog(IReadOnlyList<string> failedDirectories)
    {
        InitializeComponent();
        DirectoryList.ItemsSource = failedDirectories;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
