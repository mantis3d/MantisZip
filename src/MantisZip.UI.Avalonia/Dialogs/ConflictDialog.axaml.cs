using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
/// <summary>
/// 解压冲突对话框的确认载荷：一次关闭即交出完整决策（按钮 + 自定义名 + 应用到全部 + 暂停/取消整轮），
/// 调用方只读 <see cref="ConflictDialog.Completion"/>，不再逐个回读窗口属性。
/// </summary>
public readonly record struct FileConflictDialogOutcome(
    FileConflictAction Action,
    bool ApplyToAll,
    string? CustomName,
    bool IsPaused,
    bool IsCancelled);

public partial class ConflictDialog : Window
{
    /// <summary>未点按钮关闭（X / Alt+F4）时的载荷：按覆盖继续（旧 ResultAction 默认值为 Overwrite）。</summary>
    private static readonly FileConflictDialogOutcome DefaultOutcome =
        new(FileConflictAction.Overwrite, false, null, IsPaused: false, IsCancelled: false);

    private readonly TaskCompletionSource<FileConflictDialogOutcome> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private FileConflictDialogOutcome? _outcome;
    private string _titleKey = "Conflict_Title";
    /// <summary>当前冲突文件名（仅文件名，诊断日志用；不含路径）。</summary>
    private string _fileName = string.Empty;

    /// <summary>关窗即完成的确认载荷（模态 ShowDialog 与非模态 Show 两条路径共用）。</summary>
    public Task<FileConflictDialogOutcome> Completion => _completion.Task;

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

        _fileName = Path.GetFileName(info.FilePath);

        InitializeComponent();
        DataContext = this;

        Services.LifetimeDiagnostics.Log($"ConflictDialog OPEN file='{Path.GetFileName(info.FilePath)}' existing={info.ExistingSize} entry={info.EntrySize}");
        CoreLog.ConflictTrace("dialog-created file='{0}' existing={1} entry={2}", _fileName, info.ExistingSize, info.EntrySize);

        // 诊断：窗口真正显示后的可见性快照（是否激活 / 位置 / owner / 置顶 / 任务栏）。
        // 用于排查「冲突框弹了但被挡住或未激活」——conflict.log 与 lifecycle.log 各留一份。
        this.Opened += (_, _) =>
        {
            Services.LifetimeDiagnostics.Log($"ConflictDialog SHOWN active={IsActive} bounds={Bounds} owner={Owner?.GetType().Name} topmost={Topmost} taskbar={ShowInTaskbar}");
            CoreLog.ConflictTrace("dialog-shown active={0} bounds={1} owner={2} topmost={3} taskbar={4} file='{5}'",
                IsActive, Bounds, Owner?.GetType().Name, Topmost, ShowInTaskbar, _fileName);
        };

        HeaderText.Text = string.Format(LocalizationManager.T("Conflict_Header"), $"\"{Path.GetFileName(info.FilePath)}\"");

        // 预填重命名的建议名
        RenameTextBox.Text = info.SuggestedName ?? "";

        // 勾选"应用到全部"时禁用重命名输入（后续文件不支持自定义名）
        ApplyAllCheck.IsCheckedChanged += (_, _) => RenameTextBox.IsEnabled = ApplyAllCheck.IsChecked != true;

        // 窗口关闭时如果未通过按钮关闭（例如 Alt+F4），捕获当前快照
        this.Closing += (_, _) =>
        {
            if (_outcome == null)
            {
                // 诊断：非按钮路径关闭（Alt+F4/系统/程序化 Close）——默认按覆盖捕获
                Services.LifetimeDiagnostics.Log($"ConflictDialog CLOSING without button (default Overwrite captured) file='{Path.GetFileName(info.FilePath)}' stack:\n{Environment.StackTrace}");
                CoreLog.ConflictTrace("dialog-closing-without-button (default Overwrite) file='{0}'", _fileName);
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

    protected override void OnClosed(EventArgs e)
    {
        // 兜底：非按钮关闭也给出完整载荷，绝不让调用方读到 null
        _completion.TrySetResult(_outcome ?? DefaultOutcome);
        base.OnClosed(e);
    }

    private void CaptureResult(FileConflictAction action, bool applyToAll, string? customName)
    {
        CoreLog.ConflictTrace("dialog-result action={0} applyAll={1} custom='{2}' file='{3}'",
            action, applyToAll, customName, _fileName);
        _outcome = new FileConflictDialogOutcome(action, applyToAll, customName, IsPaused: false, IsCancelled: false);
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
        _outcome = new FileConflictDialogOutcome(FileConflictAction.Overwrite, false, null,
            IsPaused: true, IsCancelled: false);
        Close(false);
    }

    private void CancelOperation_Click(object? sender, RoutedEventArgs e)
    {
        Services.LifetimeDiagnostics.Log("ConflictDialog btn=CancelOperation");
        _outcome = new FileConflictDialogOutcome(FileConflictAction.Overwrite, false, null,
            IsPaused: false, IsCancelled: true);
        Close(false);
    }
}
