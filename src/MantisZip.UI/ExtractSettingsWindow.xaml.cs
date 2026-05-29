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

        UpdateFileCount();

        // 默认选中"解压到压缩包名"（最安全，天然隔离）
        ToNameRadio.IsChecked = true;
        OutputMode = ExtractOutputMode.ToName;

        // 初始化手动路径 TextBox 占位提示
        ManualPathTextBox.Text = L.T(L.ExtractSettings_ManualPathPlaceholder);

        RefreshOutputPathState();
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
    }

    /// <summary>
    /// 根据当前 OutputMode 更新路径区域的 UI 状态。
    /// </summary>
    private void RefreshOutputPathState()
    {
        if (ManualPathTextBox == null) return; // InitializeComponent 期间

        switch (OutputMode)
        {
            case ExtractOutputMode.Manual:
                ManualPathTextBox.IsReadOnly = false;
                BrowseButton.Visibility = Visibility.Visible;
                ManualPathRow.Visibility = Visibility.Visible;
                ModePreviewText.Visibility = Visibility.Collapsed;
                break;

            case ExtractOutputMode.Here:
                ManualPathTextBox.IsReadOnly = true;
                BrowseButton.Visibility = Visibility.Collapsed;
                ManualPathRow.Visibility = Visibility.Collapsed;
                ModePreviewText.Text = L.T(L.ExtractSettings_Mode_Here);
                ModePreviewText.Visibility = Visibility.Visible;
                break;

            case ExtractOutputMode.Smart:
                ManualPathTextBox.IsReadOnly = true;
                BrowseButton.Visibility = Visibility.Collapsed;
                ManualPathRow.Visibility = Visibility.Collapsed;
                ModePreviewText.Text = L.T(L.ExtractSettings_Mode_Smart);
                ModePreviewText.Visibility = Visibility.Visible;
                break;

            case ExtractOutputMode.ToName:
                ManualPathTextBox.IsReadOnly = true;
                BrowseButton.Visibility = Visibility.Collapsed;
                ManualPathRow.Visibility = Visibility.Collapsed;
                ModePreviewText.Text = L.T(L.ExtractSettings_Mode_ToName);
                ModePreviewText.Visibility = Visibility.Visible;
                break;
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
                    L.T(L.App_FileNotFound), // 重用"请选择有效路径"的语义，实际内容接近
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
    }

    private void UpdateFileCount()
    {
        FileCountText.Text = L.TF(L.ExtractSettings_FileCount, _files.Count);
    }
}
