using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using System.ComponentModel;
using System.Linq;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 解压设置窗口。通过 ShowDialog 返回 bool 结果，
/// 调用方在结果为 true 后读取 ViewModel 的 DestinationPath / ConflictAction / OpenFolderAfterExtract 属性。
/// </summary>
public partial class ExtractSettingsWindow : Window
{
    private bool _loaded;

    /// <summary>后台逐包校验的取消源（窗口关闭时取消，避免无谓 IO）。</summary>
    private CancellationTokenSource? _validationCts;

    /// <summary>
    /// 对话框结果（true=确认解压，false=取消）。CLI 无 owner 场景下配合
    /// <see cref="Show"/> + Closed 事件使用（ShowDialog 必须传 owner，CLI 模式没有主窗口）。
    /// </summary>
    public bool? DialogResult { get; private set; }

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

        // 设置文件夹浏览回调（解压模式：弹窗内建 ResultTreeView 实时冲突检测）。
        // 冲突预览语义绑定首包条目（与提取一致），无首包条目时无法预览。
        ViewModel.BrowseFolder = async () =>
        {
            var first = ViewModel.FirstArchiveEntries;
            if (first == null)
                return null;
            return await CustomFilePickerDialog.ShowExtractFolderAsync(this, first, ViewModel.DestinationPath);
        };

        // 设置关闭回调
        ViewModel.CloseAction = async (result) =>
        {
            DialogResult = result;
            Close(result);
            await Task.CompletedTask;
        };

        DataContext = ViewModel;

        // 窗口关闭时取消后台逐包校验
        Closed += (_, _) => _validationCts?.Cancel();

        // 浏览回调：解压模式文件夹对话框（内建 ResultTreeView 实时冲突检测）。
        // QuickPathPicker 只收目录，此处返回目录即可。
        DestinationPicker.BrowseAction = (owner, current) =>
            ViewModel.FirstArchiveEntries == null
                ? Task.FromResult<string?>(null)
                : CustomFilePickerDialog.ShowExtractFolderAsync(
                    owner ?? this, ViewModel.FirstArchiveEntries, ViewModel.DestinationPath);

        Loaded += OnLoaded;
    }

    /// <summary>
    /// 注入首包（当前打开压缩包）的条目列表——MainWindow 单包路径使用：
    /// 条目已在内存，跳过对首包的重复读取；过滤统计与冲突预览立即就绪。
    /// </summary>
    public void SetEntries(IReadOnlyList<ArchiveItem> entries)
        => ViewModel.SetFirstArchiveEntries(entries);

    /// <summary>获取当前过滤条件。仅当启用过滤且 filter.IsActive 时有效。</summary>
    public FileFilterCriteria? GetFilter()
    {
        if (FileFilterControl == null) return null;
        if (!FileFilterControl.IsFilterEnabled) return null;
        var filter = FileFilterControl.GetFilter();
        return filter.IsActive ? filter : null;
    }

    /// <summary>
    /// 过滤后需实际解压的条目 key 列表。提取语义始终绑定首包
    /// （对齐 WPF HandleExtractBatchCore「过滤仅对 i==0 生效」），与预览展示的选中项解耦。
    /// </summary>
    public List<string>? GetFilteredEntryKeys()
        => ViewModel.ComputeFilteredEntryKeys(GetFilter());

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        InitFileFilter();

        // 注入过滤条件读取器（选中切换重建 / FilteredEntryKeys 计算共用）
        ViewModel.FilterProvider = () => GetFilter();

        // Subscribe to DestinationPath changes for preview rebuild
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // 首包条目已由外部注入（MainWindow 单包路径）时立即构建冲突预览；
        // 否则 ValidateAllAsync 完成首包校验后自动填充 ⏳→树
        if (ViewModel.FirstArchiveEntries != null && !string.IsNullOrWhiteSpace(ViewModel.DestinationPath))
            ViewModel.RebuildMergedPreview();

        // 逐包后台校验（损坏 / 需密码 → 行内徽标 + 预览占位；窗口关闭时经 CTS 取消）
        _validationCts = new CancellationTokenSource();
        _ = ViewModel.ValidateAllAsync(_validationCts.Token);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExtractSettingsViewModel.DestinationPath))
        {
            // 目标路径变化 → 当前选中项的冲突高亮需要重算
            ViewModel.RebuildMergedPreview();
        }
    }

    /// <summary>初始化文件过滤控件（预设 + 事件）。</summary>
    private void InitFileFilter()
    {
        var settings = AppSettings.Load();
        FileFilterControl.LoadPresets(settings.FilterPresets);

        FileFilterControl.SavePresetRequested += name =>
        {
            var filter = FileFilterControl.GetFilter();
            if (!filter.IsActive) return;
            settings.AddPreset(new FileFilterPreset(name, filter));
            settings.Save();
            FileFilterControl.LoadPresets(settings.FilterPresets);
        };

        FileFilterControl.DeletePresetRequested += preset =>
        {
            settings.FilterPresets.Remove(preset);
            settings.Save();
            FileFilterControl.LoadPresets(settings.FilterPresets);
        };

        FileFilterControl.RenamePresetRequested += (preset, newName) =>
        {
            var existing = settings.FilterPresets.FirstOrDefault(p => p.Name == newName);
            if (existing != null)
            {
                _ = AppMessageBox.Show(
                    LocalizationManager.T("FileFilter_PresetNameExists"),
                    LocalizationManager.T("FileFilter_RenamePresetTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning,
                    this);
                return;
            }
            preset.Name = newName;
            settings.Save();
            FileFilterControl.LoadPresets(settings.FilterPresets, newName);
        };

        // 过滤条件变更时更新统计并重建预览树
        UpdateFilterStats();
        FileFilterControl.FilterChanged += OnFileFilterChanged;
    }

    private void UpdateFilterStats()
    {
        var entries = ViewModel.FirstArchiveEntries;
        if (entries == null) return;

        var filter = GetFilter();
        if (filter != null)
        {
            var matched = entries.Count(e => !e.IsDirectory && FileFilterMatcher.IsMatch(filter, e));
            var total = entries.Count(e => !e.IsDirectory);
            FileFilterControl.SetFilterStats(LocalizationManager.T("Extract_FilterStatsFormat", matched, total));
        }
        else
        {
            FileFilterControl.SetFilterStats("");
        }
    }

    private void OnFileFilterChanged()
    {
        UpdateFilterStats();
        ViewModel.RebuildMergedPreview();
    }
}
