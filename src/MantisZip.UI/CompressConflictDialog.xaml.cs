using System.IO;
using System.Windows;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

public enum CompressConflictAction
{
    Overwrite,
    Add,
    Rename,
    Cancel
}

public partial class CompressConflictDialog : Window
{
    // 在按钮点击时立即捕获对话框结果，避免 ShowDialog() 返回后 WPF 控件状态发生变化
    private CompressConflictAction _capturedAction;
    private bool _capturedApplyToAll;
    private string? _capturedCustomName;
    private bool _resultCaptured;

    /// <summary>对话框关闭后读取此属性获取用户选择的处理方式</summary>
    public CompressConflictAction ResultAction => _resultCaptured ? _capturedAction : CompressConflictAction.Cancel;
    /// <summary>用户输入的自定义文件名（未修改时返回建议名）</summary>
    public string? CustomName => _resultCaptured ? _capturedCustomName : RenameTextBox.Text;
    /// <summary>用户是否勾选了"应用到全部"</summary>
    public bool ApplyToAll => _resultCaptured && _capturedApplyToAll;

    /// <param name="filePath">目标文件路径</param>
    /// <param name="canAdd">是否支持"添加到压缩包"（Tar 不支持）</param>
    /// <param name="suggestedName">重命名的建议名（不含路径），用于预填输入框</param>
    public CompressConflictDialog(string filePath, bool canAdd, string? suggestedName = null)
    {
        InitializeComponent();
        HeaderText.Text = string.Format(L.T(L.CompressConflict_Header), $"“{Path.GetFileName(filePath)}”");

        // 预填重命名的建议名（由调用方预计算）
        RenameTextBox.Text = suggestedName ?? Path.GetFileName(filePath);

        if (!canAdd)
        {
            AddBtn.IsEnabled = false;
            AddBtn.ToolTip = L.T(L.CompressConflict_Tooltip_NoAdd);
        }

        // 勾选"应用到全部"时切换为"自动重命名"并禁用输入框
        ApplyAllCheck.Checked += (_, _) =>
        {
            RenameBtn.Content = L.T(L.CompressConflict_AutoRename);
            RenameTextBox.IsEnabled = false;
        };
        ApplyAllCheck.Unchecked += (_, _) =>
        {
            RenameBtn.Content = L.T(L.CompressConflict_Rename);
            RenameTextBox.IsEnabled = true;
        };

        // 填充目标文件信息面板
        PopulateTargetInfo(filePath);
    }

    private void PopulateTargetInfo(string filePath)
    {
        try
        {
            var fi = new FileInfo(filePath);
            if (fi.Exists)
            {
                TargetSizeText.Text = FormatSize(fi.Length);
                TargetDateText.Text = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            }
            else
            {
                TargetSizeText.Text = "--";
                TargetDateText.Text = "--";
            }
        }
        catch
        {
            TargetSizeText.Text = "--";
            TargetDateText.Text = "--";
        }
        TargetPathBlock.Text = L.T(L.CompressConflict_PathLabel) + filePath;
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// 立即捕获对话框当前状态的快照，供 ShowDialog() 调用方通过属性读取。
    /// </summary>
    private void CaptureResult(CompressConflictAction action, string? customName)
    {
        _capturedAction = action;
        _capturedApplyToAll = ApplyAllCheck.IsChecked == true;
        _capturedCustomName = customName;
        _resultCaptured = true;
    }

    private void Overwrite_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("CompressConflictDialog: user chose Overwrite for '{0}', ApplyToAll={1}", HeaderText.Text, ApplyAllCheck.IsChecked);
        CaptureResult(CompressConflictAction.Overwrite, RenameTextBox.Text);
        DialogResult = true;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("CompressConflictDialog: user chose Add for '{0}', ApplyToAll={1}", HeaderText.Text, ApplyAllCheck.IsChecked);
        CaptureResult(CompressConflictAction.Add, RenameTextBox.Text);
        DialogResult = true;
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("CompressConflictDialog: user chose Rename for '{0}', customName='{1}', ApplyToAll={2}",
            HeaderText.Text, RenameTextBox.Text, ApplyAllCheck.IsChecked);
        CaptureResult(CompressConflictAction.Rename, RenameTextBox.Text);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("CompressConflictDialog: user cancelled for '{0}'", HeaderText.Text);
        CaptureResult(CompressConflictAction.Cancel, RenameTextBox.Text);
        DialogResult = false;
    }
}
