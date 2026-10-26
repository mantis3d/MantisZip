using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 压缩流程公共逻辑（主窗口与 CLI 右键菜单共用，消除两套实现漂移）。
///
/// 历史背景：WPF 时代主窗口（code-behind）与 CLI（App）各自实现了一套压缩流程，
/// Avalonia 移植时继承了这个分裂，导致 CLI 侧曾出现「硬编码 Manual 模式」「不处理文件冲突」
/// 等与主窗口行为不一致的 bug。本类将可共享部分抽取为单一事实来源：
/// - <see cref="CreateErrorResolver"/>：源文件读取错误 resolver（同步契约 + UI 线程封送）
/// - <see cref="CreateResolver"/>：冲突处理 resolver（弹窗回调注入 + ApplyToAll 记忆）
/// - <see cref="ShowConflictDialogAsync"/>：CompressConflictDialog 弹窗 + 结果映射（无状态）
/// </summary>
public static class CompressFlow
{

    /// <summary>
    /// 创建源文件读取错误 resolver（文件被占用等）。
    /// 引擎在后台线程调用（重试耗尽后），本方法将其封送回 UI 线程弹 <see cref="ErrorDialog"/>
    /// 让用户选择 重试/跳过/中止，并统一「ApplyToAll 记忆」逻辑（对齐 WPF App.CreateCompressOptions）。
    /// 返回 Abort 时引擎重抛异常使整个压缩失败；Skip 跳过该文件继续；Retry 重新尝试读取。
    /// </summary>
    public static Func<FileErrorInfo, FileErrorAction> CreateErrorResolver()
    {
        bool applyToAll = false;
        FileErrorAction? chosenAction = null;

        return info =>
        {
            // 已勾选"应用到全部" → 直接返回记忆的选择
            if (applyToAll && chosenAction.HasValue)
                return chosenAction.Value;

            // 同步 API 边界：Core 的 ErrorResolver 契约是同步委托
            // （ArchiveOptions.ErrorResolver = Func<FileErrorInfo, FileErrorAction>，见
            // ArchiveEngine.cs:78），引擎在读文件的后台线程上同步调用，这里无法 await。
            // 因此把「弹窗 + 等用户选择」整段封送回 UI 线程，且只在**调用方后台线程**上同步等待；
            // 弹窗内部已走 await 链（ShowDialog → Completion），UI 线程内不再 sync-over-async。
            var result = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlg = new ErrorDialog(info);
                var owner = ResolveOwnerWindow();
                if (owner == null)
                {
                    // 极端边缘情况：无可用父窗口（非桌面模式），无法弹窗，返回默认中止
                    System.Diagnostics.Debug.WriteLine("ErrorDialog skipped: no owner window available");
                    return new ErrorDialogOutcome(FileErrorAction.Abort, ApplyToAll: false);
                }

                await dlg.ShowDialog(owner);
                return await dlg.Completion;
            }).GetAwaiter().GetResult();

            if (result.ApplyToAll)
            {
                applyToAll = true;
                chosenAction = result.Action;
            }

            return result.Action;
        };
    }

    /// <summary>
    /// 解析错误/冲突对话框的 Owner 窗口：优先当前活跃窗口，否则回退到主窗口。
    /// 避免弹窗出现在主窗口之后。
    /// </summary>
    private static Window? ResolveOwnerWindow()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.Windows
                    .OfType<Window>()
                    .FirstOrDefault(w => w.IsActive)
                    ?? desktop.MainWindow;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 创建压缩冲突处理 resolver。
    /// 弹窗由 <paramref name="showDialog"/> 回调完成（主窗口走 VM 回调、CLI 直接弹窗），
    /// 本方法统一「ApplyToAll 记忆 + 结果包装」逻辑。
    /// </summary>
    /// <param name="showDialog">
    /// 弹窗回调：传入冲突信息，返回用户选择（Action、重命名名、是否应用到全部）。
    /// 返回 Cancel 且取消整个操作时抛 OperationCanceledException 以终止压缩。
    /// </param>
    /// <param name="defaultAction">
    /// 默认冲突策略（来自设置/压缩对话框，仅本次压缩生效）："overwrite" / "add" / "rename" / "skip"。
    /// 非 "ask" 时直接映射不再弹窗；"add" 在格式不支持追加（!info.CanAdd）时回退弹窗。
    /// </param>
    public static CompressConflictResolver CreateResolver(
        Func<CompressConflictInfo, Task<(MantisZip.Core.Abstractions.CompressConflictAction Action, string? CustomName, bool ApplyToAll)>> showDialog,
        string? defaultAction = null)
    {
        bool applyToAll = false;
        MantisZip.Core.Abstractions.CompressConflictAction? chosenAction = null;

        return async info =>
        {
            // 已勾选"应用到全部" → 直接返回记忆的选择
            if (applyToAll && chosenAction.HasValue)
                return new CompressConflictResolution(chosenAction.Value, null);

            // 默认冲突策略非 ask → 直接映射免弹窗（映射不可用时回退弹窗）
            if (!string.IsNullOrEmpty(defaultAction) && defaultAction != "ask")
            {
                var mapped = MapDefaultConflictAction(defaultAction, info);
                if (mapped.HasValue)
                    return new CompressConflictResolution(mapped.Value, null);
            }

            var (action, customName, applyAll) = await showDialog(info);
            if (applyAll)
            {
                applyToAll = true;
                chosenAction = action;
            }

            return action switch
            {
                MantisZip.Core.Abstractions.CompressConflictAction.Cancel
                    => new CompressConflictResolution(MantisZip.Core.Abstractions.CompressConflictAction.Cancel, null),
                _ => new CompressConflictResolution(action, customName),
            };
        };
    }

    /// <summary>
    /// 将默认冲突策略字符串映射为 Core 冲突动作。
    /// </summary>
    /// <param name="defaultAction">"overwrite" / "add" / "rename" / "skip"（"ask" 由调用方过滤）。</param>
    /// <param name="info">当前冲突信息（用于 "add" 的 CanAdd 判定）。</param>
    /// <returns>
    /// 映射结果；null 表示该策略在当前冲突下不可用（如格式不支持追加），
    /// 调用方应回退弹窗询问用户。
    /// </returns>
    private static MantisZip.Core.Abstractions.CompressConflictAction? MapDefaultConflictAction(
        string defaultAction, CompressConflictInfo info)
    {
        switch (defaultAction)
        {
            case "overwrite":
                return MantisZip.Core.Abstractions.CompressConflictAction.Overwrite;
            case "rename":
                // CustomName 传 null → Core ComputeRenamedPath 自动生成唯一名
                return MantisZip.Core.Abstractions.CompressConflictAction.Rename;
            case "skip":
                // Core 无 Skip 动作；Dialog Skip → Cancel（resolver 单项跳过，非终止压缩）
                return MantisZip.Core.Abstractions.CompressConflictAction.Cancel;
            case "add":
                // 格式不支持追加（如 tar.gz）→ 返回 null 回退弹窗
                return info.CanAdd ? MantisZip.Core.Abstractions.CompressConflictAction.Add : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// 弹 CompressConflictDialog 并映射结果（供主窗口/CLI 的弹窗回调复用）。
    /// resolver 由 Core 在后台线程调用，本方法内部通过 Dispatcher 封送回 UI 线程弹窗。
    /// 循环重入：用户点击"暂停"时收起冲突对话框并进入进度窗口暂停态，
    /// 恢复后重新弹窗处理同一个冲突（对齐 WPF AppPartials/App.Compress.cs 的实现）。
    /// </summary>
    public static async Task<(MantisZip.Core.Abstractions.CompressConflictAction Action, string? CustomName, bool ApplyToAll)>
        ShowConflictDialogAsync(Window owner, CompressConflictInfo info)
    {
        // 循环重入：暂停后恢复时重新弹窗（对齐 WPF AppPartials/App.Compress.cs CompressConflictResolver）
        while (true)
        {
            // 冲突弹窗是模态等待：期间已用时间冻结（每次重入都配对 Begin/End，防计数泄漏）
            var pw = owner as ProgressWindow ?? ProgressWindow.CurrentVisible;
            pw?.BeginModalFreeze();

            // 确认即载荷：对话框关闭时一次性交出完整决策（按钮 + 自定义名 + 应用到全部 + 暂停/取消整轮），
            // 这里只做「载荷 → Core 动作」映射，不再逐字段回读窗口属性（漏读一项就静默失效）。
            var outcome = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlg = new CompressConflictDialog(info.OutputPath, info.SuggestedName, info.CanAdd);
                // 优先使用当前可见的进度窗口作为 owner（主窗口压缩时居中于进度窗口），
                // 回退到传入的 owner（CLI 场景显式传入 ProgressWindow）
                var dialogOwner = ProgressWindow.CurrentVisible ?? owner;
                await dlg.ShowDialog(dialogOwner);
                return await dlg.Completion;
            });

            // "取消操作"按钮：终止整个压缩（对齐解压侧 Ask 弹窗"取消整个操作"抛
            // OperationCanceledException 的语义；Core 收到后取消剩余任务）
            if (outcome.IsCancelled)
            {
                pw?.EndModalFreeze();
                throw new OperationCanceledException("压缩被用户取消");
            }

            // 暂停：收起对话框，进入进度窗口暂停态，恢复后重新弹窗处理同一个冲突
            if (outcome.IsPaused)
            {
                if (pw != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => pw.PauseFromConflict());
                    // IsPaused 已为 true → 解除模态冻结后由暂停态无缝接续冻结
                    pw.EndModalFreeze();
                    await ProgressPauseGate.WaitForResumeAsync(pw);
                }
                continue; // 恢复后重新弹窗
            }

            pw?.EndModalFreeze();
            return (MapConflictAction(outcome.Action),
                outcome.Action == Dialogs.CompressConflictAction.Rename ? outcome.CustomName : null,
                outcome.ApplyToAll);
        }
    }

    /// <summary>对话框侧的压缩冲突按钮 → Core 冲突动作（Skip/Cancel 均落为 Cancel，与旧 switch 一致）。</summary>
    private static MantisZip.Core.Abstractions.CompressConflictAction MapConflictAction(Dialogs.CompressConflictAction action)
        => action switch
        {
            Dialogs.CompressConflictAction.Overwrite => MantisZip.Core.Abstractions.CompressConflictAction.Overwrite,
            Dialogs.CompressConflictAction.Add => MantisZip.Core.Abstractions.CompressConflictAction.Add,
            Dialogs.CompressConflictAction.Rename => MantisZip.Core.Abstractions.CompressConflictAction.Rename,
            _ => MantisZip.Core.Abstractions.CompressConflictAction.Cancel,
        };

    /// <summary>
    /// 压缩成功后把输出目录写入路径历史（<see cref="PathHistoryManager"/>，
    /// QuickPathPicker 地址栏 / CustomFilePickerDialog 历史建议的数据源）。
    /// 目录取自 GetOutputPaths 各输出文件的父目录并去重（Manual/Combined = 输出文件所在目录；
    /// Separate = 各源条目所在目录）。失败仅记日志，不影响压缩结果。
    /// 由调用方在压缩确认成功后调用（主窗口 ExecuteCompressFromSettings / CLI CompressWithProgress）。
    /// </summary>
    public static void RecordOutputHistory(CompressRequest request)
    {
        try
        {
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var output in AvaloniaCompressService.GetOutputPaths(request))
            {
                var dir = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(dir))
                    dirs.Add(dir);
            }
            foreach (var dir in dirs)
                PathHistoryManager.Record(dir);
        }
        catch (Exception ex)
        {
            App.DebugLog($"[CompressFlow] RecordOutputHistory failed: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  压缩侧「列表」模式播种（对齐解压侧 ExtractFlow 的做法）
    // ════════════════════════════════════════════════════════════════

    /// <summary>「列表」模式播种阈值：条目数超过则一条行都不建（对齐解压侧 ExtractFlow.MaxSeedEntryCount）。</summary>
    private const int MaxSeedEntryCount = 5000;

    /// <summary>
    /// 压缩侧「列表」模式播种（后台）：枚举待打包条目后 <see cref="ProgressWindow.SeedEntries"/>，
    /// 让「列表」显示**全部条目**（Pending）并随终态事件更新，而不是只冒已完成行。
    /// <para>
    /// 本方法立即返回、不 await —— 压缩绝不等待播种；枚举失败/取消/超阈值一律放弃播种，
    /// 由引擎逐条目终态事件的 upsert 兜底（渐进模式）。引擎若已上报出条目行，
    /// <see cref="ProgressWindow.SeedEntries"/> 会自行跳过，避免清掉已上报的终态行。
    /// </para>
    /// </summary>
    public static void TrySeedEntryItemsInBackground(ProgressWindow? pw, CompressRequest request, CancellationToken ct)
    {
        if (pw is null || request.SourcePaths is not { Count: > 0 })
            return;

        // 白名单与 CompressService 同源：Plan 中非空 IncludedFiles 的并集；全为 null = 未启用过滤
        IReadOnlyCollection<string>? whitelist = null;
        var planItems = request.Plan?.Items;
        if (planItems is { Count: > 0 } && planItems.Any(i => i.IncludedFiles != null))
        {
            whitelist = planItems
                .Where(i => i.IncludedFiles != null)
                .SelectMany(i => i.IncludedFiles!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        _ = SeedEntryItemsAsync(pw, request.SourcePaths, whitelist, ct);
    }

    /// <summary>后台枚举 + 播种（所有异常与取消都吞掉，只影响列表预览，不影响压缩本身）。</summary>
    private static async Task SeedEntryItemsAsync(
        ProgressWindow pw, IReadOnlyList<string> sources,
        IReadOnlyCollection<string>? whitelist, CancellationToken ct)
    {
        try
        {
            var items = await Task.Run(
                () => SourceEntryEnumerator.Enumerate(sources, whitelist, ct), ct).ConfigureAwait(false);

            if (items.Count == 0 || items.Count > MaxSeedEntryCount)
                return;

            pw.SeedEntries(items);
        }
        catch (Exception ex)
        {
            App.DebugLog($"[CompressFlow] Entry seeding skipped: {ex.Message}");
        }
    }
}
