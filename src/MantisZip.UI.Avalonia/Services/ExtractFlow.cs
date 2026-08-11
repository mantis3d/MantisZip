using Avalonia.Controls;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.UI.Avalonia.Dialogs;

namespace MantisZip.UI.Avalonia.Services;

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
        ShowConflictDialogAsync(Window owner, FileConflictInfo info)
    {
        // 循环重入：暂停后恢复时重新弹窗（对齐 WPF App.xaml.cs ConflictResolver）
        while (true)
        {
            var result = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlg = new ConflictDialog(info);
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
