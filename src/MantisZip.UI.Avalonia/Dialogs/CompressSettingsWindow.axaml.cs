using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 压缩设置窗口。通过 ShowDialog 返回 bool 结果，
/// 调用方在结果为 true 后读取 ViewModel 的属性构造 CompressRequest。
/// </summary>
public partial class CompressSettingsWindow : Window
{
    /// <summary>
    /// ViewModel，公开属性供调用方关闭后读取。
    /// </summary>
    public CompressSettingsViewModel ViewModel { get; }

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="CompressSettingsWindow(IReadOnlyList{string})"/>。
    /// </summary>
    public CompressSettingsWindow()
    {
        InitializeComponent();
        ViewModel = new CompressSettingsViewModel(Array.Empty<string>());
        DataContext = ViewModel;
    }

    public CompressSettingsWindow(IReadOnlyList<string> sourcePaths)
    {
        InitializeComponent();

        ViewModel = new CompressSettingsViewModel(sourcePaths);

        // 设置文件保存浏览回调
        ViewModel.BrowseOutput = async () =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save archive",
                DefaultExtension = ".zip",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("ZIP archives") { Patterns = new[] { "*.zip" } },
                    new FilePickerFileType("TAR.GZ archives") { Patterns = new[] { "*.tar.gz" } },
                }
            });
            return file?.Path?.LocalPath;
        };

        // 设置关闭回调
        ViewModel.CloseAction = async (result) =>
        {
            Close(result);
            await Task.CompletedTask;
        };

        DataContext = ViewModel;
    }

    /// <summary>
    /// 切换密码显示/隐藏。
    /// </summary>
    private void TogglePasswordReveal(object? sender, RoutedEventArgs e)
    {
        if (PasswordTextBox.PasswordChar == '●')
        {
            PasswordTextBox.PasswordChar = default;
            RevealButton.Content = "Hide";
        }
        else
        {
            PasswordTextBox.PasswordChar = '●';
            RevealButton.Content = "Show";
        }
    }
}
