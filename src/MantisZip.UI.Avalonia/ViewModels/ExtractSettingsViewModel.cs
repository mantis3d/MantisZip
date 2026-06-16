using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// 解压设置对话框的 ViewModel。
/// 用户选择目标路径、冲突策略和是否打开文件夹，通过 CloseAction 回调返回结果。
/// </summary>
public partial class ExtractSettingsViewModel : ObservableObject
{
    /// <summary>初始传入的压缩包路径列表（只读）。</summary>
    public IReadOnlyList<string> ArchivePaths { get; }

    /// <summary>最终保留的文件路径列表（可修改后回读）。</summary>
    public List<string> SelectedPaths { get; }

    /// <summary>本地化字符串字典，供 XAML 绑定。</summary>
    public Dictionary<string, string> LocalizedStrings { get; }

    /// <summary>由 View 设置的文件夹选择回调。返回选择的路径，取消返回 null。</summary>
    public Func<Task<string?>>? BrowseFolder { get; set; }

    /// <summary>由 View 设置的关闭回调。参数 true=确认解压，false=取消。</summary>
    public Func<bool, Task>? CloseAction { get; set; }

    [ObservableProperty]
    private string _destinationPath = string.Empty;

    [ObservableProperty]
    private string _conflictAction = "ask";

    [ObservableProperty]
    private bool _openFolderAfterExtract;

    public ExtractSettingsViewModel(IReadOnlyList<string> archivePaths)
    {
        ArchivePaths = archivePaths;
        SelectedPaths = archivePaths.ToList();

        // 默认目标路径：第一个压缩包所在目录/压缩包名
        if (archivePaths.Count > 0)
        {
            var dir = Path.GetDirectoryName(archivePaths[0]) ?? "";
            var name = Path.GetFileNameWithoutExtension(archivePaths[0]);
            DestinationPath = Path.Combine(dir, name);
        }

        // 初始化本地化字符串
        var keys = new[]
        {
            "Extract_Title",
            "Extract_SourceArchives",
            "Extract_Destination",
            "Extract_DestinationPlaceholder",
            "Extract_Browse",
            "Extract_WhenFileExists",
            "Extract_ConflictAction",
            "Extract_Conflict_Ask",
            "Extract_Conflict_Overwrite",
            "Extract_Conflict_Rename",
            "Extract_Conflict_Skip",
            "Extract_OpenFolder",
            "Extract_Start",
            "Extract_Cancel"
        };
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            dict[key] = LocalizationManager.T(key);
        }
        LocalizedStrings = dict;
    }

    [RelayCommand]
    private async Task BrowseDestination()
    {
        if (BrowseFolder == null) return;
        var path = await BrowseFolder();
        if (!string.IsNullOrEmpty(path))
        {
            DestinationPath = path;
        }
    }

    [RelayCommand]
    private async Task Extract()
    {
        if (string.IsNullOrWhiteSpace(DestinationPath)) return;

        if (CloseAction != null)
            await CloseAction(true);
    }

    [RelayCommand]
    private async Task Cancel()
    {
        if (CloseAction != null)
            await CloseAction(false);
    }
}
