using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 文件冲突对话框：覆盖 / 重命名 / 跳过 / 取消，支持"应用到全部"。
/// 显示磁盘文件与压缩包文件的对比信息。
/// </summary>
public partial class ConflictDialog : Window
{
    private FileConflictAction _capturedAction;
    private bool _capturedApplyToAll;
    private string? _capturedCustomName;
    private bool _resultCaptured;
    private bool _isPaused;
    private bool _cancelOperation;
    private string _titleKey = "Conflict_Title";

    /// <summary>用户选择的处理方式</summary>
    public FileConflictAction ResultAction => _resultCaptured ? _capturedAction : FileConflictAction.Overwrite;
    /// <summary>用户是否勾选了"应用到全部"</summary>
    public bool ApplyToAll => _resultCaptured && _capturedApplyToAll;
    /// <summary>用户输入的自定义文件名</summary>
    public string? CustomName => _resultCaptured ? _capturedCustomName : RenameTextBox.Text;

    /// <summary>用户是否点击了"暂停"按钮</summary>
    public bool IsPaused => _isPaused;
    /// <summary>用户是否点击了"取消"（取消整个操作）按钮</summary>
    public bool CancelOperation => _cancelOperation;

    // ── Localized string properties ──
    public string WinTitle => LocalizationManager.T(_titleKey);
    public string DiskLabel => LocalizationManager.T("Conflict_DiskLabel");
    public string ArchiveLabel => LocalizationManager.T("Conflict_ArchiveLabel");
    public string SizeLabel => LocalizationManager.T("Conflict_SizeLabel");
    public string DateLabel => LocalizationManager.T("Conflict_DateLabel");
    public string OverwriteText => LocalizationManager.T("CompressConflict_Overwrite");
    public string RenameText => LocalizationManager.T("CompressConflict_Rename");
    public string SkipText => LocalizationManager.T("Conflict_Skip");
    public string ApplyAllText => LocalizationManager.T("Conflict_ApplyAll");
    public string NewNameLabel => LocalizationManager.T("Conflict_NewNameLabel");
    public string RenameHint => LocalizationManager.T("Conflict_RenameHint");
    public string OverwriteOlderText => LocalizationManager.T("Conflict_Btn_OverwriteOlder");
    public string OverwriteSmallerText => LocalizationManager.T("Conflict_Btn_OverwriteSmaller");
    public string PauseText => LocalizationManager.T("Conflict_Btn_Pause");
    public string CancelOpText => LocalizationManager.T("Conflict_Btn_CancelOperation");

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="ConflictDialog(FileConflictInfo)"/>。
    /// </summary>
    public ConflictDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public ConflictDialog(FileConflictInfo info, string? titleKey = null)
    {
        if (!string.IsNullOrEmpty(titleKey))
            _titleKey = titleKey;

        InitializeComponent();
        DataContext = this;

        Services.LifetimeDiagnostics.Log($"ConflictDialog OPEN file='{Path.GetFileName(info.FilePath)}' existing={info.ExistingSize} entry={info.EntrySize}");

        HeaderText.Text = string.Format(LocalizationManager.T("Conflict_Header"), $"\"{Path.GetFileName(info.FilePath)}\"");

        // 预填重命名的建议名
        RenameTextBox.Text = info.SuggestedName ?? "";

        // 勾选"应用到全部"时禁用重命名输入（后续文件不支持自定义名）
        ApplyAllCheck.IsCheckedChanged += (_, _) => RenameTextBox.IsEnabled = ApplyAllCheck.IsChecked != true;

        // 窗口关闭时如果未通过按钮关闭（例如 Alt+F4），捕获当前快照
        this.Closing += (_, _) =>
        {
            if (!_resultCaptured)
            {
                // 诊断：非按钮路径关闭（Alt+F4/系统/程序化 Close）——默认按覆盖捕获
                Services.LifetimeDiagnostics.Log($"ConflictDialog CLOSING without button (default Overwrite captured) file='{Path.GetFileName(info.FilePath)}' stack:\n{Environment.StackTrace}");
                CaptureResult(FileConflictAction.Overwrite, false, null);
            }
        };

        // 已有文件信息
        ExistingSizeText.Text = info.ExistingSize.HasValue ? FormatUtil.FormatSize(info.ExistingSize.Value) : "--";
        ExistingDateText.Text = info.ExistingModified?.ToString("yyyy-MM-dd HH:mm") ?? "--";

        // 压缩包条目信息
        EntrySizeText.Text = info.EntrySize.HasValue ? FormatUtil.FormatSize(info.EntrySize.Value) : "--";
        EntryDateText.Text = info.EntryModified?.ToString("yyyy-MM-dd HH:mm") ?? "--";

        // 对比结果
        var parts = new List<string>();
        if (info.ExistingSize.HasValue && info.EntrySize.HasValue)
        {
            if (info.EntrySize.Value > info.ExistingSize.Value)
                parts.Add(LocalizationManager.T("Conflict_Size_ArchiveLarger"));
            else if (info.EntrySize.Value < info.ExistingSize.Value)
                parts.Add(LocalizationManager.T("Conflict_Size_DiskLarger"));
            else
                parts.Add(LocalizationManager.T("Conflict_Size_Same"));
        }
        if (info.ExistingModified.HasValue && info.EntryModified.HasValue)
        {
            if (info.EntryModified.Value > info.ExistingModified.Value)
                parts.Add(LocalizationManager.T("Conflict_Date_ArchiveNewer"));
            else if (info.EntryModified.Value < info.ExistingModified.Value)
                parts.Add(LocalizationManager.T("Conflict_Date_DiskNewer"));
            else
                parts.Add(LocalizationManager.T("Conflict_Date_Same"));
        }
        CompareResultText.Text = string.Join("  |  ", parts);
    }

    private void CaptureResult(FileConflictAction action, bool applyToAll, string? customName)
    {
        _capturedAction = action;
        _capturedApplyToAll = applyToAll;
        _capturedCustomName = customName;
        _resultCaptured = true;
    }

    private void Overwrite_Click(object? sender, RoutedEventArgs e)
    {
        var b = sender as Button;
        Services.LifetimeDiagnostics.Log($"ConflictDialog btn=Overwrite ptrOver={b?.IsPointerOver} focused={b?.IsFocused}");
        CaptureResult(FileConflictAction.Overwrite, ApplyAllCheck.IsChecked == true, RenameTextBox.Text);
        Close(true);
    }

    private void Rename_Click(object? sender, RoutedEventArgs e)
    {
        Services.LifetimeDiagnostics.Log("ConflictDialog btn=Rename");
        CaptureResult(FileConflictAction.Rename, ApplyAllCheck.IsChecked == true, RenameTextBox.Text);
        Close(true);
    }

    private void Skip_Click(object? sender, RoutedEventArgs e)
    {
        Services.LifetimeDiagnostics.Log("ConflictDialog btn=Skip");
        CaptureResult(FileConflictAction.Skip, ApplyAllCheck.IsChecked == true, RenameTextBox.Text);
        Close(true);
    }

    private void OverwriteIfOlder_Click(object? sender, RoutedEventArgs e)
    {
        Services.LifetimeDiagnostics.Log("ConflictDialog btn=OverwriteIfOlder");
        CaptureResult(FileConflictAction.OverwriteIfOlder, ApplyAllCheck.IsChecked == true, RenameTextBox.Text);
        Close(true);
    }

    private void OverwriteIfSmaller_Click(object? sender, RoutedEventArgs e)
    {
        Services.LifetimeDiagnostics.Log("ConflictDialog btn=OverwriteIfSmaller");
        CaptureResult(FileConflictAction.OverwriteIfSmaller, ApplyAllCheck.IsChecked == true, RenameTextBox.Text);
        Close(true);
    }

    private void Pause_Click(object? sender, RoutedEventArgs e)
    {
        Services.LifetimeDiagnostics.Log("ConflictDialog btn=Pause");
        _isPaused = true;
        Close(false);
    }

    private void CancelOperation_Click(object? sender, RoutedEventArgs e)
    {
        Services.LifetimeDiagnostics.Log("ConflictDialog btn=CancelOperation");
        _cancelOperation = true;
        CaptureResult(FileConflictAction.Overwrite, false, null);
        Close(false);
    }
}
