using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainWindowViewModel();
        vm.GetOpenFilePath = OpenFileDialogAsync;
        DataContext = vm;
    }

    private async Task<string?> OpenFileDialogAsync()
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择压缩包",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("压缩包")
                {
                    Patterns = ["*.zip", "*.7z", "*.rar", "*.tar", "*.tgz", "*.gz", "*.tar.gz", "*.iso"]
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = ["*.*"]
                }
            ]
        });

        return result.Count >= 1 ? result[0].Path.LocalPath : null;
    }
}
