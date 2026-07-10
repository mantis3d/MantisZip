using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using MantisZip.UI.Localization;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MantisZip.UI;

/// <summary>
/// 解压设置窗口 — 统一替换 --extract 的文件夹选择对话框。
/// 支持单文件/多文件的输出模式选择（手动输入/解压到此处/智能解压/解压到压缩包名）。
/// 布局风格与 CompressSettingsWindow 保持一致（TabControl + GroupBox + 2-column Grid）。
/// </summary>
public partial class ExtractSettingsWindow : Window
{
    // ── Public Properties (caller reads these after DialogResult = true) ──

    /// <summary>最终保留的文件路径列表</summary>
    public List<string> SelectedPaths { get; private set; }

    /// <summary>选择的输出模式</summary>
    public ExtractOutputMode OutputMode { get; private set; }

    /// <summary>手动模式下用户选择的目录</summary>
    public string? CustomDestination { get; private set; }

    // ── Filter Support ──

    /// <summary>提取模式使用的条目列表（用于过滤统计）。</summary>
    private IReadOnlyList<MantisZip.Core.Abstractions.ArchiveItem>? _entries;

    /// <summary>获取当前过滤条件。仅当启用过滤且 filter.IsActive 时有效。</summary>
    public FileFilterCriteria? GetFilter()
    {
        if (FileFilterControl == null) return null;
        if (!FileFilterControl.IsFilterEnabled) return null;
        var filter = FileFilterControl.GetFilter();
        return filter.IsActive ? filter : null;
    }

    /// <summary>
    /// 对 _entries 应用过滤条件，返回匹配条目的 key 列表。
    /// </summary>
    public List<string>? GetFilteredEntryKeys()
    {
        var filter = GetFilter();
        if (filter == null || _entries == null) return null;

        return _entries
            .Where(e => FileFilterMatcher.IsMatch(filter, e))
            .Select(e => e.FullPath)
            .ToList();
    }

    // ── Internal State ──

    private readonly ObservableCollection<string> _files;
    private readonly string _firstArchiveDir = "";
    private readonly string _firstArchiveNameOnly = "";

    /// <summary>
    /// 创建解压设置窗口（无条目列表，不启用过滤）。
    /// </summary>
    /// <param name="archivePaths">初始压缩包路径列表</param>
    public ExtractSettingsWindow(IReadOnlyList<string> archivePaths)
    {
        InitializeComponent();

        _files = new ObservableCollection<string>(archivePaths);
        SelectedPaths = archivePaths.ToList();
        FileListBox.ItemsSource = _files;

        // 预计算第一个压缩包的路径信息，用于非 Manual 模式下的路径预览
        if (archivePaths.Count > 0)
        {
            var first = archivePaths[0];
            _firstArchiveDir = Path.GetDirectoryName(first) ?? "";
            _firstArchiveNameOnly = Path.GetFileNameWithoutExtension(first);
        }

        UpdateFileCount();

        // 默认选中"解压到压缩包名"（最安全，天然隔离）
        ToNameRadio.IsChecked = true;
        OutputMode = ExtractOutputMode.ToName;

        // 从 AppSettings 加载默认值
        LoadDefaultsFromSettings();

        RefreshOutputPathState();
        UpdateExtractButton();
    }

    /// <summary>
    /// 创建解压设置窗口（带压缩包条目列表，支持过滤）。
    /// </summary>
    /// <param name="archivePaths">初始压缩包路径列表</param>
    /// <param name="entries">压缩包内条目列表（用于过滤统计和提取）</param>
    public ExtractSettingsWindow(IReadOnlyList<string> archivePaths, IReadOnlyList<MantisZip.Core.Abstractions.ArchiveItem> entries)
        : this(archivePaths)
    {
        _entries = entries;

        // 加载预设
        if (FileFilterControl != null)
        {
            FileFilterControl.LoadPresets(AppSettings.Instance.FilterPresets);
            FileFilterControl.SavePresetRequested += OnSavePreset;
            FileFilterControl.DeletePresetRequested += OnDeletePreset;
        }
    }

    private void OnSavePreset(string name)
    {
        var filter = FileFilterControl.GetFilter();
        var preset = new FileFilterPreset(name, filter, isBuiltIn: false);
        try
        {
            AppSettings.Instance.AddPreset(preset);
            AppSettings.Instance.Save();
            FileFilterControl.LoadPresets(AppSettings.Instance.FilterPresets);
        }
        catch (InvalidOperationException ex)
        {
            AppMessageBox.Show(ex.Message, L.T(L.FileFilter_SavePresetTitle),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDeletePreset(FileFilterPreset preset)
    {
        AppSettings.Instance.FilterPresets.Remove(preset);
        AppSettings.Instance.Save();
        FileFilterControl.LoadPresets(AppSettings.Instance.FilterPresets);
    }

    private void LoadDefaultsFromSettings()
    {
        var s = AppSettings.Instance;

        // 文件冲突默认
        switch (s.FileConflictAction)
        {
            case "overwrite": ConflictOverwriteRadio.IsChecked = true; break;
            case "rename": ConflictRenameRadio.IsChecked = true; break;
            case "skip": ConflictSkipRadio.IsChecked = true; break;
            default: ConflictAskRadio.IsChecked = true; break;
        }

        // 打开文件夹
        OpenFolderCheck.IsChecked = s.OpenFolderAfterExtract;
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 保留供将来扩展
    }

    private void OutputMode_Changed(object sender, RoutedEventArgs e)
    {
        if (ManualRadio.IsChecked == true)
            OutputMode = ExtractOutputMode.Manual;
        else if (HereRadio.IsChecked == true)
            OutputMode = ExtractOutputMode.Here;
        else if (SmartRadio.IsChecked == true)
            OutputMode = ExtractOutputMode.Smart;
        else if (ToNameRadio.IsChecked == true)
            OutputMode = ExtractOutputMode.ToName;

        RefreshOutputPathState();
        UpdateExtractButton();
    }

    private void UpdateExtractButton()
    {
        if (ExtractButton == null) return;

        if (OutputMode == ExtractOutputMode.Manual)
        {
            var text = OutputPathControl.PathText?.Trim();
            ExtractButton.IsEnabled = !string.IsNullOrEmpty(text);
        }
        else
        {
            ExtractButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 根据当前 OutputMode 更新路径区域的 UI 状态。
    /// 输出路径始终可见，仅切换启用/禁用状态，避免界面跳动。
    /// </summary>
    private void RefreshOutputPathState()
    {
        if (OutputPathControl == null) return; // InitializeComponent 期间

        if (OutputMode == ExtractOutputMode.Manual)
        {
            // 手动模式：启用路径编辑
            OutputPathControl.IsReadOnly = false;

            // 恢复之前用户输入的路径
            if (!string.IsNullOrEmpty(CustomDestination))
                OutputPathControl.PathText = CustomDestination;
        }
        else
        {
            // 非手动模式：禁用路径编辑，显示计算好的路径预览
            OutputPathControl.IsReadOnly = true;

            OutputPathControl.PathText = OutputMode switch
            {
                ExtractOutputMode.Here => _firstArchiveDir,
                ExtractOutputMode.Smart => L.T(L.ExtractSettings_Mode_Smart),
                ExtractOutputMode.ToName => Path.Combine(_firstArchiveDir, _firstArchiveNameOnly),
                _ => _firstArchiveDir
            };
        }
    }

    private void ExtractButton_Click(object sender, RoutedEventArgs e)
    {
        // 手动模式下必须指定有效目录
        if (OutputMode == ExtractOutputMode.Manual)
        {
            if (string.IsNullOrWhiteSpace(CustomDestination))
            {
                // 同步 OutputPathControl 输入的路径
                var text = OutputPathControl.PathText?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    CustomDestination = text;
                }
            }

            if (string.IsNullOrWhiteSpace(CustomDestination))
            {
                AppMessageBox.Show(
                    L.T(L.App_FileNotFound),
                    L.T(L.ExtractSettings_Title),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 确保目录存在（用户可能在 TextBox 中输入了不存在路径）
            if (!Directory.Exists(CustomDestination))
            {
                try
                {
                    Directory.CreateDirectory(CustomDestination);
                }
                catch (Exception ex)
                {
                    CoreLog.Trace("ExtractSettingsWindow: failed: {0}", ex.Message);
                    AppMessageBox.Show(
                        L.T(L.App_ExtractFailed),
                        L.T(L.ExtractSettings_Title),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }
        }

        // 将冲突策略和打开文件夹设置写入 AppSettings（HandleExtractBatchCore 会读取）
        var settings = AppSettings.Instance;
        if (ConflictAskRadio.IsChecked == true)
            settings.FileConflictAction = "ask";
        else if (ConflictOverwriteRadio.IsChecked == true)
            settings.FileConflictAction = "overwrite";
        else if (ConflictRenameRadio.IsChecked == true)
            settings.FileConflictAction = "rename";
        else if (ConflictSkipRadio.IsChecked == true)
            settings.FileConflictAction = "skip";
        settings.OpenFolderAfterExtract = OpenFolderCheck.IsChecked == true;

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }


    private void UpdateFileCount()
    {
        FileCountText.Text = L.TF(L.ExtractSettings_FileCount, _files.Count);
    }

    private void OnFilterChanged()
    {
        if (_entries == null || FileFilterControl == null) return;

        var filter = FileFilterControl.GetFilter();
        if (FileFilterControl.IsFilterEnabled && filter.IsActive)
        {
            var matched = _entries.Count(e => FileFilterMatcher.IsMatch(filter, e));
            FileFilterControl.SetFilterStats(
                L.TF(L.ExtractFilter_CountLabel, _entries.Count, matched));
            FileFilterControl.ShowFilterStats(true);
        }
        else
        {
            FileFilterControl.ShowFilterStats(false);
        }
    }
}
