using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 解压设置窗口。通过 ShowDialog 返回 bool 结果，
/// 调用方在结果为 true 后读取 ViewModel 的 DestinationPath / ConflictAction / OpenFolderAfterExtract 属性。
/// </summary>
public partial class ExtractSettingsWindow : Window
{
    /// <summary>
    /// ViewModel，公开属性供调用方关闭后读取。
    /// </summary>
    public ExtractSettingsViewModel ViewModel { get; }

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="ExtractSettingsWindow(IReadOnlyList{string})"/>。
    /// </summary>
    public ExtractSettingsWindow()
    {
        InitializeComponent();
        ViewModel = new ExtractSettingsViewModel(Array.Empty<string>());
        DataContext = ViewModel;
    }

    public ExtractSettingsWindow(IReadOnlyList<string> archivePaths)
    {
        InitializeComponent();

        ViewModel = new ExtractSettingsViewModel(archivePaths);

        // 设置文件夹浏览回调
        ViewModel.BrowseFolder = async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select destination folder",
                AllowMultiple = false
            });
            return folders.Count >= 1 ? folders[0].Path.LocalPath : null;
        };

        // 设置关闭回调
        ViewModel.CloseAction = async (result) =>
        {
            Close(result);
            await Task.CompletedTask;
        };

        DataContext = ViewModel;

        // 绑定文件列表
        FileListBox.ItemsSource = archivePaths;
    }
}
