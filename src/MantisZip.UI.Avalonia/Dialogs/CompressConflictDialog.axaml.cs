using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 压缩冲突对话框：覆盖 / 重命名 / 跳过 / 取消，支持"应用到全部"。
/// </summary>
public enum CompressConflictAction
{
    Overwrite,
    Add,
    Rename,
    Skip,
    Cancel
}

public partial class CompressConflictDialog : Window
{
    private CompressConflictAction _capturedAction;
    private bool _capturedApplyToAll;
    private string? _capturedCustomName;
    private bool _resultCaptured;
    private bool _isPaused;
    private bool _cancelOperation;

    /// <summary>用户选择的处理方式</summary>
    public CompressConflictAction ResultAction => _resultCaptured ? _capturedAction : CompressConflictAction.Cancel;
    /// <summary>用户输入的自定义文件名</summary>
    public string? CustomName => _resultCaptured ? _capturedCustomName : RenameTextBox.Text;
    /// <summary>用户是否勾选了"应用到全部"</summary>
    public bool ApplyToAll => _resultCaptured && _capturedApplyToAll;

    /// <summary>用户是否点击了"暂停"按钮</summary>
    public bool IsPaused => _isPaused;
    /// <summary>用户是否点击了"取消整个操作"按钮</summary>
    public bool CancelOperation => _cancelOperation;

    // ── Localized string properties (bound via DataContext=self) ──
    public string WinTitle => LocalizationManager.T("CompressConflict_Title");
    public string TargetLabel => LocalizationManager.T("CompressConflict_TargetLabel");
    public string SizeLabel => LocalizationManager.T("Conflict_SizeLabel");
    public string DateLabel => LocalizationManager.T("Conflict_DateLabel");
    public string OverwriteText => LocalizationManager.T("CompressConflict_Overwrite");
    public string RenameText => LocalizationManager.T("CompressConflict_Rename");
    public string SkipText => LocalizationManager.T("CompressConflict_Skip");

    public string ApplyAllText => LocalizationManager.T("Error_ApplyToAll");
    public string RenameHint => LocalizationManager.T("Conflict_RenameHint");
    public string AddText => LocalizationManager.T("CompressConflict_Add");
    public string PauseText => LocalizationManager.T("CompressConflict_Pause");
    public string CancelOpText => LocalizationManager.T("CompressConflict_CancelOperation");
    public string TooltipNoAddText => LocalizationManager.T("CompressConflict_Tooltip_NoAdd");

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="CompressConflictDialog(string, string?)"/>。
    /// </summary>
    public CompressConflictDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <param name="filePath">目标文件路径</param>
    /// <param name="suggestedName">重命名的建议名（不含路径）</param>
    /// <param name="canAdd">是否支持"添加到已有压缩包"（Tar 等格式不支持）</param>
    public CompressConflictDialog(string filePath, string? suggestedName = null, bool canAdd = true)
    {
        InitializeComponent();
        DataContext = this;

        HeaderText.Text = string.Format(LocalizationManager.T("CompressConflict_Header"), $"\"{Path.GetFileName(filePath)}\"");

        // Enable Add button based on engine capability
        AddBtn.IsEnabled = canAdd;
        if (!canAdd)
            ToolTip.SetTip(AddBtn, TooltipNoAddText);

        // 预填重命名建议名
        RenameTextBox.Text = suggestedName ?? Path.GetFileName(filePath);

        // 勾选"应用到全部"时禁用重命名输入框并切换按钮文本
        ApplyAllCheck.IsCheckedChanged += (_, _) =>
        {
            var isChecked = ApplyAllCheck.IsChecked == true;
            RenameTextBox.IsEnabled = !isChecked;
            RenameBtnLabel.Text = isChecked
                ? LocalizationManager.T("CompressConflict_AutoRename")
                : LocalizationManager.T("CompressConflict_Rename");
        };

        // 填充目标文件信息
        PopulateTargetInfo(filePath);
    }

    private void PopulateTargetInfo(string filePath)
    {
        try
        {
            var fi = new FileInfo(filePath);
            if (fi.Exists)
            {
                TargetSizeText.Text = FormatUtil.FormatSize(fi.Length);
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
        TargetPathBlock.Text = LocalizationManager.T("CompressConflict_PathLabel") + filePath;
    }

    private void CaptureResult(CompressConflictAction action, string? customName)
    {
        _capturedAction = action;
        _capturedApplyToAll = ApplyAllCheck.IsChecked == true;
        _capturedCustomName = customName;
        _resultCaptured = true;
    }

    private void Overwrite_Click(object? sender, RoutedEventArgs e)
    {
        CaptureResult(CompressConflictAction.Overwrite, RenameTextBox.Text);
        Close(true);
    }

    private void Rename_Click(object? sender, RoutedEventArgs e)
    {
        CaptureResult(CompressConflictAction.Rename, RenameTextBox.Text);
        Close(true);
    }

    private void Skip_Click(object? sender, RoutedEventArgs e)
    {
        CaptureResult(CompressConflictAction.Skip, RenameTextBox.Text);
        Close(true);
    }

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        CaptureResult(CompressConflictAction.Add, RenameTextBox.Text);
        Close(true);
    }

    private void Pause_Click(object? sender, RoutedEventArgs e)
    {
        _isPaused = true;
        Close(false);
    }

    private void CancelOperation_Click(object? sender, RoutedEventArgs e)
    {
        _cancelOperation = true;
        CaptureResult(CompressConflictAction.Cancel, RenameTextBox.Text);
        Close(false);
    }
}
