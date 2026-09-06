using Avalonia.Controls;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 选中条目解压的执行结果。
/// </summary>
public enum SelectedItemsExtractStatus
{
    Success,
    Failed,
    Cancelled
}

/// <summary>
/// 选中条目解压的结果载荷。
/// </summary>
public sealed record SelectedItemsExtractResult(SelectedItemsExtractStatus Status, string? ErrorMessage);

/// <summary>
/// 解压流程公共逻辑（主窗口与 CLI 右键菜单共用，消除两套实现漂移）。
///
/// 主窗口（MainWindowViewModel.ExtractArchive）与 CLI（--extract 弹窗批处理）在
/// 「拿到目标路径/冲突策略/过滤条件后如何执行解压」上曾经各写一份：CLI 侧曾出现
/// 漏接冲突策略、路径计算不一致等与主窗口行为漂移的问题。本方法作为统一执行入口：
/// - 冲突策略映射 + Ask 弹窗回调（可选）→ ArchiveOptions，复用 SelectedItemsExtractService
/// - 有过滤条件时仅解压匹配条目（ExtractEntriesAsync），否则全量（ExtractService）
/// 密码解析、进度呈现、退出策略由调用方负责（主窗口会话密码/内嵌进度，CLI 独立进度窗口）。
/// </summary>
public static class ExtractFlow
{
    /// <summary>
    /// 选中条目解压统一执行入口（拖拽解压与右键「解压选中项到」在拿到目标路径后共用的同一流程）。
    ///
    /// 两个入口的唯一差异是目标路径获取方式（拖拽 = DropTargetDetector + 选择器回退，
    /// 右键 = CustomFilePickerDialog 预览选择器）；拿到目标路径后必须完全走同一段代码，
    /// 防止进度窗口行为（批处理列表内容、状态驱动、失败弹窗）再次漂移。
    ///
    /// 本方法统一：
    /// - 创建进度窗口，批处理列表 = 压缩包一行（对齐右键路径语义；预留未来扩展为
    ///   「压缩包列表 + 包内文件列表」两个列表）
    /// - 状态驱动：SetCurrentBatchItem(0) + 成功 Completed / 失败 Failed（修复拖拽路径
    ///   从不驱动状态导致列表项全部停留在 ⏳ Pending 的问题）
    /// - 冲突策略映射 + Ask 弹窗回调（可选）、取消处理
    /// - 失败统一弹窗（拖拽与右键选中解压均无确认环节，必须弹窗提示）
    /// - 成功时 SetComplete + AutoCloseOrWaitAsync（尊重 KeepOpenOnComplete 图钉）
    ///
    /// 调用方负责：设置状态栏消息（拖拽/右键文案不同）与 OpenFolderAfterExtract 打开目标目录。
    /// </summary>
    /// <param name="archivePath">压缩包路径（同时作为批处理列表的唯一行）。</param>
    /// <param name="password">密码（可为 null）。</param>
    /// <param name="entries">待解压条目（已由 DragDropItemExpander / 右键选择展开为文件集）。</param>
    /// <param name="destinationPath">目标目录。</param>
    /// <param name="currentFolder">当前浏览层（用于裁剪路径前缀，与预览一致）。</param>
    /// <param name="preserveFullPath">是否保留完整路径。</param>
    /// <param name="conflictAction">冲突策略字符串（AppSettings.FileConflictAction 值）。</param>
    /// <param name="conflictDialog">Ask 冲突弹窗回调（null 时 Ask 降级为引擎默认处理）。</param>
    /// <param name="progressTitle">进度窗口标题（拖拽 = Status_DragExtractingTo，右键 = Status_Extracting）。</param>
    public static async Task<SelectedItemsExtractResult> RunSelectedItemsExtractionAsync(
        string archivePath,
        string? password,
        IReadOnlyList<ArchiveItem> entries,
        string destinationPath,
        string currentFolder,
        bool preserveFullPath,
        string conflictAction,
        Func<FileConflictInfo, Task<(FileConflictAction Action, bool ApplyToAll)>>? conflictDialog,
        string progressTitle)
    {
        var pw = new ProgressWindow(progressTitle);
        pw.InitCancellation();

        var status = SelectedItemsExtractStatus.Success;
        string? errorMessage = null;
        try
        {
            pw.Show();
            // 批处理列表 = 压缩包一行（对齐右键路径语义，非展开文件列表；
            // 未来扩展为「压缩包列表 + 包内文件列表」两个列表时在此调整数据源）
            pw.InitBatchMode(new[] { archivePath });
            pw.SetCurrentBatchItem(0);

            var progress = pw.CreatePauseAwareProgress(
                ProgressViewModel.CreateBackgroundProgress(pw, p => pw.SetProgress(p)));

            await new SelectedItemsExtractService().ExtractEntriesAsync(
                archivePath, password, entries, destinationPath,
                conflictAction, currentFolder, preserveFullPath,
                conflictDialog, progress, pw.CancellationToken);

            // 成功：标记批处理行完成 + 尊重 KeepOpenOnComplete 图钉（对齐 RunWithProgress 语义）
            pw.UpdateBatchItemStatus(0, BatchItemStatus.Completed);
            pw.SetComplete(LocalizationManager.T("Cli_StatusDone"));
            // 成功后把目标目录写入路径历史（拖拽解压 / 右键解压选中项共用本入口；取消/失败不记录）
            PathHistoryManager.Record(destinationPath);
            await pw.AutoCloseOrWaitAsync(0, () => pw.Close());
        }
        catch (OperationCanceledException)
        {
            status = SelectedItemsExtractStatus.Cancelled;
        }
        catch (Exception ex)
        {
            pw.UpdateBatchItemStatus(0, BatchItemStatus.Failed, ex.Message);
            status = SelectedItemsExtractStatus.Failed;
            errorMessage = ex.Message;
        }
        finally
        {
            pw.Close();
        }

        // 失败统一弹窗：拖拽与右键选中解压均无确认环节，用户容易忽略状态栏小字
        if (status == SelectedItemsExtractStatus.Failed)
        {
            try
            {
                await AppMessageBox.Show(
                    LocalizationManager.T("Main_Status_ExtractFailed", errorMessage),
                    LocalizationManager.T("App_ErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception dlgEx)
            {
                App.DebugLog($"[ExtractFlow] Failed to show error dialog: {dlgEx.Message}");
            }
        }

        return new SelectedItemsExtractResult(status, errorMessage);
    }
    /// <summary>
    /// 执行一次解压（单压缩包）。目标目录、冲突策略、过滤条件、密码均已解析完毕。
    /// </summary>
    /// <param name="archivePath">压缩包路径。</param>
    /// <param name="dest">目标目录。</param>
    /// <param name="conflictAction">冲突策略字符串（AppSettings.FileConflictAction 值）。</param>
    /// <param name="filteredKeys">过滤后需实际解压的条目 key 列表；null = 全量解压（未开过滤）。
    /// 注意：非 null 即走 ExtractEntriesAsync，空列表 = 有意零匹配（什么都不解压），
    /// 绝不回退全量 —— 这是「预览 = 实际」的边界保证（Bug 1 修复）。</param>
    /// <param name="password">密码（可为 null）。</param>
    /// <param name="conflictDialog">Ask 冲突弹窗回调（null 时 Ask 降级为引擎默认处理）。</param>
    /// <param name="progress">进度回调。</param>
    /// <param name="ct">取消令牌。</param>
    /// <exception cref="NotSupportedException">不支持的压缩格式。</exception>
    public static async Task ExtractAsync(
        string archivePath,
        string dest,
        string conflictAction,
        List<string>? filteredKeys,
        string? password,
        Func<FileConflictInfo, Task<(FileConflictAction Action, bool ApplyToAll)>>? conflictDialog,
        IProgress<ArchiveProgress> progress,
        CancellationToken ct)
    {
        var options = SelectedItemsExtractService.CreateExtractOptions(conflictAction, conflictDialog);

        // 有过滤条件：仅解压匹配条目（统一入口；无 pathOverrides = 保留完整路径）。
        // 注意用 `!= null` 而非 `is { Count: > 0 }`：过滤激活但零匹配（空列表）也必须走
        // ExtractEntriesAsync —— 空列表 = 什么都不解压，若误走 else 全量解压会泄露全部文件。
        if (filteredKeys != null)
        {
            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            if (engine == null)
                throw new NotSupportedException(LocalizationManager.T("Error_UnsupportedArchiveFormat"));

            await engine.ExtractEntriesAsync(
                archivePath, filteredKeys, dest, password, progress, ct, options);
        }
        else
        {
            await new ExtractService().ExtractAsync(
                archivePath, dest, password, progress, ct, options);
        }

        // 成功后把目标目录写入路径历史（主窗口对话框解压 / CLI 弹窗批处理共用本入口；
        // 取消/异常向上抛出时不会执行到此处）
        PathHistoryManager.Record(dest);
    }

    /// <summary>
    /// 弹 ConflictDialog 处理单个文件冲突（Ask 策略），主窗口与 CLI 共用。
    /// resolver 由 Core 在后台线程调用，本方法内部通过 Dispatcher 封送回 UI 线程弹窗。
    /// 循环重入：用户点击"暂停"时收起冲突对话框并进入进度窗口暂停态，
    /// 恢复后重新弹窗处理同一个冲突（对齐 WPF App.xaml.cs ConflictResolver 的实现）。
    /// 用户选择"取消整个操作"时抛 <see cref="OperationCanceledException"/> 终止解压
    /// （与拖拽/主窗口原有语义一致）。Rename 时把用户自定义名写回 <paramref name="info"/>。
    /// </summary>
    public static async Task<(FileConflictAction Action, bool ApplyToAll)>
        ShowConflictDialogAsync(Window owner, FileConflictInfo info, string titleKey = "Conflict_Title")
    {
        // 循环重入：暂停后恢复时重新弹窗（对齐 WPF App.xaml.cs ConflictResolver）
        while (true)
        {
            var result = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlg = new ConflictDialog(info, titleKey);
                await dlg.ShowDialog(owner);

                // 暂停：收起对话框，返回暂停标志由外层处理
                if (dlg.IsPaused)
                {
                    return (Action: FileConflictAction.Overwrite, IsPaused: true, IsCancelled: false, ApplyAll: false);
                }

                // 取消整个操作
                if (dlg.CancelOperation)
                {
                    return (Action: FileConflictAction.Overwrite, IsPaused: false, IsCancelled: true, ApplyAll: false);
                }

                if (dlg.ResultAction == FileConflictAction.Rename && !string.IsNullOrEmpty(dlg.CustomName))
                    info.CustomName = dlg.CustomName;

                return (Action: dlg.ResultAction, IsPaused: false, IsCancelled: false, ApplyAll: dlg.ApplyToAll);
            });

            if (result.IsCancelled)
                throw new OperationCanceledException("用户取消整个解压操作");

            if (result.IsPaused)
            {
                // 从 owner（CLI 直接传 ProgressWindow）或当前打开的进度窗口中找到目标，
                // 在 UI 线程调用 PauseFromConflict 进入暂停态，然后在后台线程等待暂停事件
                // （不阻塞 UI 线程，用户可在进度窗口点击"继续"恢复）。
                var pw = owner as ProgressWindow ?? ProgressWindow.CurrentVisible;
                if (pw != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => pw.PauseFromConflict());
                    pw.PauseEvent.Wait(pw.CancellationToken);
                }
                continue; // 恢复后重新弹窗
            }

            return (result.Action, result.ApplyAll);
        }
    }
}
