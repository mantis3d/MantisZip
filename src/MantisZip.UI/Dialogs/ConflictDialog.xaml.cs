using System.IO;
using System.Windows;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// 文件冲突对话框：覆盖 / 覆盖较旧 / 重命名 / 跳过，支持"L.T(L.Settings_Menu_Btn_Apply)到全部"。
/// L.T(L.Pwd_ShowBtn)L.T(L.Conflict_DiskLabel)文件与L.T(L.Conflict_ArchiveLabel)文件的对比信息。
/// Topmost 以确保不被进度窗口挡住。
/// </summary>
public partial class ConflictDialog : Window
{
    // 在按钮点击时立即捕获对话框结果，避免 ShowDialog() 返回后 WPF 控件状态发生变化
    private FileConflictAction _capturedAction;
    private bool _capturedApplyToAll;
    private string? _capturedCustomName;
    private bool _resultCaptured;

    /// <summary>对话框关闭后读取此属性获取用户选择的处理方式</summary>
    public FileConflictAction ResultAction => _resultCaptured ? _capturedAction : FileConflictAction.Overwrite;
    /// <summary>用户是否勾选了"对后续所有文件使用相同操作"</summary>
    public bool ApplyToAll => _resultCaptured && _capturedApplyToAll;

    /// <summary>用户输入的自定义文件名（未修改时返回建议名）</summary>
    public string? CustomName => _resultCaptured ? _capturedCustomName : RenameTextBox.Text;

    public ConflictDialog(FileConflictInfo info)
    {
        InitializeComponent();

        HeaderText.Text = string.Format(L.T(L.Conflict_Header), $"“{Path.GetFileName(info.FilePath)}”");

        // 预填L.T(L.CompressConflict_Rename)的建议名（由 Core 层预计算）
        RenameTextBox.Text = info.SuggestedName ?? "";

        // 勾选"L.T(L.Settings_Menu_Btn_Apply)到全部"时禁用L.T(L.Conflict_Btn_Rename)输入（后续文件不支持自定义名）
        ApplyAllCheck.Checked += (_, _) => RenameTextBox.IsEnabled = false;
        ApplyAllCheck.Unchecked += (_, _) => RenameTextBox.IsEnabled = true;

        // 窗口关闭时如果未通过按钮关闭（例如 Alt+F4），捕获当前快照
        // 注意：只在 _resultCaptured == false 时生效，否则会覆盖按钮 Click 中的显式捕获值
        this.Closing += (_, _) =>
        {
            if (!_resultCaptured)
                CaptureResult(FileConflictAction.Overwrite, false, null);
        };

        // 已有文件信息
        ExistingSizeText.Text = info.ExistingSize.HasValue ? FormatSize(info.ExistingSize.Value) : "--";
        ExistingDateText.Text = info.ExistingModified?.ToString("yyyy-MM-dd HH:mm") ?? "--";

        // L.T(L.Conflict_ArchiveLabel)条目信息
        EntrySizeText.Text = info.EntrySize.HasValue ? FormatSize(info.EntrySize.Value) : "--";
        EntryDateText.Text = info.EntryModified?.ToString("yyyy-MM-dd HH:mm") ?? "--";

        // 对比结果
        var parts = new List<string>();
        if (info.ExistingSize.HasValue && info.EntrySize.HasValue)
        {
            if (info.EntrySize.Value > info.ExistingSize.Value)
                parts.Add(L.T(L.Conflict_Size_ArchiveLarger));
            else if (info.EntrySize.Value < info.ExistingSize.Value)
                parts.Add(L.T(L.Conflict_Size_DiskLarger));
            else
                parts.Add(L.T(L.Conflict_Size_Same));
        }
        else if (info.EntrySize.HasValue && !info.ExistingSize.HasValue)
        {
            // 磁盘文件可能被删除或读不到
        }
        if (info.ExistingModified.HasValue && info.EntryModified.HasValue)
        {
            if (info.EntryModified.Value > info.ExistingModified.Value)
                parts.Add(L.T(L.Conflict_Date_ArchiveNewer));
            else if (info.EntryModified.Value < info.ExistingModified.Value)
                parts.Add(L.T(L.Conflict_Date_DiskNewer));
            else
                parts.Add(L.T(L.Conflict_Date_Same));
        }
        CompareResultText.Text = string.Join("  |  ", parts);
    }

    /// <summary>
    /// 立即捕获对话框当前状态的快照，供 ShowDialog() 调用方通过属性读取。
    /// 在按钮 Click 事件中调用，确保结果不受关闭时序影响。
    /// </summary>
    private void CaptureResult(FileConflictAction action, bool applyToAll, string? customName)
    {
        _capturedAction = action;
        _capturedApplyToAll = applyToAll;
        _capturedCustomName = customName;
        _resultCaptured = true;
    }

    private void Overwrite_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("ConflictDialog: user chose Overwrite for '{0}', ApplyToAll={1}", HeaderText.Text, ApplyAllCheck.IsChecked);
        CaptureResult(FileConflictAction.Overwrite, ApplyAllCheck.IsChecked == true, RenameTextBox.Text);
        DialogResult = true;
    }

    private void OverwriteIfOlder_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("ConflictDialog: user chose OverwriteIfOlder for '{0}', ApplyToAll={1}", HeaderText.Text, ApplyAllCheck.IsChecked);
        CaptureResult(FileConflictAction.OverwriteIfOlder, ApplyAllCheck.IsChecked == true, RenameTextBox.Text);
        DialogResult = true;
    }

    private void OverwriteIfSmaller_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("ConflictDialog: user chose OverwriteIfSmaller for '{0}', ApplyToAll={1}", HeaderText.Text, ApplyAllCheck.IsChecked);
        CaptureResult(FileConflictAction.OverwriteIfSmaller, ApplyAllCheck.IsChecked == true, RenameTextBox.Text);
        DialogResult = true;
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("ConflictDialog: user chose Rename for '{0}', customName='{1}', ApplyToAll={2}",
            HeaderText.Text, RenameTextBox.Text, ApplyAllCheck.IsChecked);
        CaptureResult(FileConflictAction.Rename, ApplyAllCheck.IsChecked == true, RenameTextBox.Text);
        DialogResult = true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("ConflictDialog: user chose Skip for '{0}', ApplyToAll={1}", HeaderText.Text, ApplyAllCheck.IsChecked);
        CaptureResult(FileConflictAction.Skip, ApplyAllCheck.IsChecked == true, RenameTextBox.Text);
        DialogResult = true;
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
}
