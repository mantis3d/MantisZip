using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.UI.Avalonia.Models;
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

    /// <summary>过滤后需实际解压的条目 key 列表（由 View 从对话框回传；null = 未启用过滤，全量解压）。</summary>
    public List<string>? FilteredEntryKeys { get; set; }

    // ── Preview tree ──

    /// <summary>预览树的根节点。</summary>
    [ObservableProperty]
    private PreviewTreeNode? _previewRoot;

    /// <summary>预览面板是否启用精简模式。</summary>
    [ObservableProperty]
    private bool _previewCompactMode = true;

    /// <summary>是否显示过滤项。</summary>
    [ObservableProperty]
    private bool _showFilteredGhosts;

    /// <summary>预览树是否正在后台构建（构建超过阈值后置 true，驱动加载覆层显示）。</summary>
    [ObservableProperty]
    private bool _isPreviewBuilding;

    /// <summary>预览树构建进度（0–100，-1 表示不确定进度/不定进度条）。</summary>
    [ObservableProperty]
    private double _previewBuildProgress = -1;

    /// <summary>预览树构建版本号，用于丢弃过期异步结果。</summary>
    private int _previewBuildVersion;

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
            "Extract_Cancel",
            "Extract_TabFilter"
        };
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            dict[key] = LocalizationManager.T(key);
        }
        LocalizedStrings = dict;
    }

    /// <summary>
    /// 构建解压预览树。由窗口在加载完成后调用。
    /// 原始树构建在后台线程执行，快速操作时通过版本号丢弃过期结果。
    /// </summary>
    /// <param name="entries">压缩包内的条目列表。</param>
    /// <param name="filter">文件过滤条件，传递到服务层标记 IsFilteredOut。</param>
    /// <param name="checkExists">是否逐文件检查目标位置是否存在。</param>
    public void BuildExtractPreview(IEnumerable<ArchiveItem> entries, FileFilterCriteria? filter = null, bool checkExists = false)
        => _ = BuildExtractPreviewCoreAsync(entries, filter, checkExists);

    private async Task BuildExtractPreviewCoreAsync(IEnumerable<ArchiveItem> entries, FileFilterCriteria? filter, bool checkExists)
    {
        var version = ++_previewBuildVersion;
        PreviewBuildProgress = -1;

        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            PreviewRoot = null;
            IsPreviewBuilding = false;
            return;
        }

        // 快照输入（后台构建期间 DestinationPath 等可能被用户修改）
        var snapshot = entries.ToList();
        var destDir = DestinationPath;

        // Progress<T> 捕获构造时的 SynchronizationContext（UI 线程），自动封送回 UI；
        // 版本号守卫丢弃过期构建的进度回调
        var progress = new Progress<double>(v =>
        {
            if (version == _previewBuildVersion)
                PreviewBuildProgress = v;
        });

        try
        {
            var rootName = Path.GetFileName(destDir);

            var buildTask = Task.Run(() => ResultPreviewService.BuildExtractPreview(
                snapshot,
                destDir,
                rootName: rootName,
                checkExists: checkExists,
                filter: filter,
                progress: progress));

            // 快速构建（<250ms）不显示加载态，避免切换目标路径时预览树闪烁；
            // 慢构建显示确定性进度条（服务按条目数上报 0–100）
            var delayTask = Task.Delay(250);
            if (await Task.WhenAny(buildTask, delayTask) == delayTask)
            {
                if (version != _previewBuildVersion) return; // 已有更新的构建
                IsPreviewBuilding = true;
            }

            var root = await buildTask;
            if (version != _previewBuildVersion) return; // 过期结果丢弃

            PreviewRoot = root;
        }
        catch (Exception ex)
        {
            App.DebugLog($"BuildExtractPreview failed: {ex.Message}");
        }
        finally
        {
            if (version == _previewBuildVersion)
                IsPreviewBuilding = false;
        }
    }

    partial void OnDestinationPathChanged(string value)
    {
        // When destination path changes, update the preview tree if we have entries
        // The caller should call BuildExtractPreview again
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
