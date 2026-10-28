using System;
using System.IO;
using System.Threading.Tasks;
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

/// <summary>
/// 压缩冲突对话框的确认载荷：一次关闭即交出完整决策（按钮 + 自定义名 + 应用到全部 + 暂停/取消整轮），
/// 调用方只读 <see cref="CompressConflictDialog.Completion"/>，不再逐个回读窗口属性。
/// </summary>
public readonly record struct CompressConflictDialogOutcome(
    CompressConflictAction Action,
    string? CustomName,
    bool ApplyToAll,
    bool IsPaused,
    bool IsCancelled);

public partial class CompressConflictDialog : Window
{
    /// <summary>未点按钮关闭（X / Alt+F4）时的载荷：取消本次压缩（旧 ResultAction 默认值为 Cancel）。</summary>
    private static readonly CompressConflictDialogOutcome DefaultOutcome =
        new(CompressConflictAction.Cancel, null, false, IsPaused: false, IsCancelled: false);

    private readonly TaskCompletionSource<CompressConflictDialogOutcome> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CompressConflictDialogOutcome? _outcome;

    /// <summary>关窗即完成的确认载荷（模态 ShowDialog 与非模态 Show 两条路径共用）。</summary>
    public Task<CompressConflictDialogOutcome> Completion => _completion.Task;

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

        // 诊断：窗口真正显示后的可见性快照（是否激活 / 位置 / owner / 置顶 / 任务栏），
        // 与解压侧 ConflictDialog 对称，写入 conflict.log。
        this.Opened += (_, _) =>
        {
            CoreLog.ConflictTrace("compress-dialog-shown active={0} bounds={1} owner={2} topmost={3} taskbar={4} file='{5}'",
                IsActive, Bounds, Owner?.GetType().Name, Topmost, ShowInTaskbar, Path.GetFileName(filePath));
        };

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

    protected override void OnClosed(EventArgs e)
    {
        // 兜底：非按钮关闭也给出完整载荷，绝不让调用方读到 null
        _completion.TrySetResult(_outcome ?? DefaultOutcome);
        base.OnClosed(e);
    }

    private void CaptureResult(CompressConflictAction action, string? customName)
    {
        CoreLog.ConflictTrace("compress-dialog-result action={0} applyAll={1} custom='{2}'",
            action, ApplyAllCheck.IsChecked == true, customName);
        _outcome = new CompressConflictDialogOutcome(action, customName, ApplyAllCheck.IsChecked == true,
            IsPaused: false, IsCancelled: false);
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
        _outcome = new CompressConflictDialogOutcome(CompressConflictAction.Cancel, null, false,
            IsPaused: true, IsCancelled: false);
        Close(false);
    }

    private void CancelOperation_Click(object? sender, RoutedEventArgs e)
    {
        _outcome = new CompressConflictDialogOutcome(CompressConflictAction.Cancel, null, false,
            IsPaused: false, IsCancelled: true);
        Close(false);
    }
}
