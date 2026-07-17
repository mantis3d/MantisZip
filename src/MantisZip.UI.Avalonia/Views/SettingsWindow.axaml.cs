using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = new SettingsWindowViewModel();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsWindowViewModel vm)
            vm.SaveCommand.Execute(null);

        // 刷新全局字体、紧凑度等需要立即生效的设置
        App.RefreshAppFontFamily();
        App.RefreshCompactness();

        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
