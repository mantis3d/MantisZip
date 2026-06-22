using System.Collections.Generic;
using System.Windows;

namespace MantisZip.UI;

public partial class ElevationInfoDialog : Window
{
    public ElevationInfoDialog(IReadOnlyList<string> unwritableDirs)
    {
        InitializeComponent();
        DirectoryList.ItemsSource = unwritableDirs;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
