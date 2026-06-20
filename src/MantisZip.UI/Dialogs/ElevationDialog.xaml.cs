using System.Collections.Generic;
using System.Windows;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

public partial class ElevationDialog : Window
{
    public ElevationDialog(IReadOnlyList<string> unwritableDirs)
    {
        InitializeComponent();

        if (unwritableDirs.Count == 1)
        {
            MessageText.Text = string.Format(L.T(L.ElevationDialog_Message), unwritableDirs[0]);
        }
        else
        {
            MessageText.Text = string.Format(L.T(L.ElevationDialog_MultiMessage), unwritableDirs.Count);
            DirectoryList.ItemsSource = unwritableDirs;
            DirectoryList.Visibility = Visibility.Visible;
        }
    }

    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
