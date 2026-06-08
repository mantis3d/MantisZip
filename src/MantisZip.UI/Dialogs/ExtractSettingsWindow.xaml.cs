using MantisZip.Core.Models;
using MantisZip.UI.Localization;
using Ookii.Dialogs.Wpf;
using System.Collections.ObjectModel;
using System.IO;
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

    // ── Internal State ──

    private readonly ObservableCollection<string> _files;
    private readonly string _firstArchiveDir = "";
    private readonly string _firstArchiveNameOnly = "";

    /// <summary>
    /// 创建解压设置窗口。
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

        // 初始化手动路径 TextBox 占位提示
        ManualPathTextBox.Text = L.T(L.ExtractSettings_ManualPathPlaceholder);

        // 从 AppSettings 加载默认值
        LoadDefaultsFromSettings();

        RefreshOutputPathState();
        UpdateExtractButton();
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
            var text = ManualPathTextBox.Text?.Trim();
            var hasValidPath = !string.IsNullOrEmpty(text)
                && text != L.T(L.ExtractSettings_ManualPathPlaceholder);
            ExtractButton.IsEnabled = hasValidPath;
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
        if (ManualPathTextBox == null) return; // InitializeComponent 期间

        if (OutputMode == ExtractOutputMode.Manual)
        {
            // 手动模式：启用路径编辑
            ManualPathTextBox.IsReadOnly = false;
            ManualPathTextBox.IsEnabled = true;
            BrowseButton.IsEnabled = true;

            // 恢复之前用户输入的路径，或占位文本
            if (!string.IsNullOrEmpty(CustomDestination))
                ManualPathTextBox.Text = CustomDestination;
            else
                ManualPathTextBox.Text = L.T(L.ExtractSettings_ManualPathPlaceholder);
        }
        else
        {
            // 非手动模式：禁用路径编辑，显示计算好的路径预览
            ManualPathTextBox.IsReadOnly = true;
            ManualPathTextBox.IsEnabled = false;
            BrowseButton.IsEnabled = false;

            ManualPathTextBox.Text = OutputMode switch
            {
                ExtractOutputMode.Here => _firstArchiveDir,
                ExtractOutputMode.Smart => L.T(L.ExtractSettings_Mode_Smart),
                ExtractOutputMode.ToName => Path.Combine(_firstArchiveDir, _firstArchiveNameOnly),
                _ => _firstArchiveDir
            };
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new VistaFolderBrowserDialog
        {
            Description = L.T(L.ExtractSettings_Title),
            SelectedPath = !string.IsNullOrEmpty(CustomDestination)
                ? CustomDestination
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        if (dialog.ShowDialog() == true)
        {
            CustomDestination = dialog.SelectedPath;
            ManualPathTextBox.Text = CustomDestination;
        }
    }

    private void ExtractButton_Click(object sender, RoutedEventArgs e)
    {
        // 手动模式下必须指定有效目录
        if (OutputMode == ExtractOutputMode.Manual)
        {
            if (string.IsNullOrWhiteSpace(CustomDestination))
            {
                // 用户在 TextBox 直接输入了内容但 CustomDestination 未同步
                var text = ManualPathTextBox.Text?.Trim();
                if (!string.IsNullOrEmpty(text) && text != L.T(L.ExtractSettings_ManualPathPlaceholder))
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
                catch
                {
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

    private void ManualPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (OutputMode == ExtractOutputMode.Manual)
        {
            var text = ManualPathTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(text) && text != L.T(L.ExtractSettings_ManualPathPlaceholder))
            {
                CustomDestination = text;
            }
        }
        UpdateExtractButton();
    }

    private void UpdateFileCount()
    {
        FileCountText.Text = L.TF(L.ExtractSettings_FileCount, _files.Count);
    }
}
