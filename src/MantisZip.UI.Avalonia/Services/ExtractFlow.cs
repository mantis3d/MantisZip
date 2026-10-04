using Avalonia.Controls;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
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
            // T6: 并行度接线（与 SelectedItemsExtractService.ExtractEntriesAsync 内读取的同一设置；
            // 引擎不支持并行或设置为串行 → 传 1，HasParallelDegree=false 自动隐藏并行统计，Rule 6）
            pw.SetParallelDegree(ResolveDisplayParallelDegree(archivePath));
            // T9/D7: 列表模式播种——调用方已持有确切的待解压条目集，直接播种
            // （无需 ListEntriesAsync：既不给 TAR/GZ 加全流扫描，也不会把未选中条目
            // 播成永远等不到上报的 Pending 行）。数据在内存中，同步建列表即可，
            // SeedEntries 内部负责封送到 UI 线程，不阻塞后续解压。
            TrySeedSelectedEntries(pw, archivePath, entries);

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
        catch (PasswordRetryCancelledException)
        {
            // 用户在密码弹窗点取消：行标记「已取消 - 需要密码」（区别于普通失败）
            var msg = LocalizationManager.T("Status_PasswordCancelled");
            pw.UpdateBatchItemStatus(0, BatchItemStatus.Failed, msg);
            status = SelectedItemsExtractStatus.Failed;
            errorMessage = msg;
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
        // 传递并行解压线程数（引擎 SupportsParallelExtract 时生效）
        if (options != null)
            options.ParallelExtractDegree = AppSettings.Load()?.ParallelExtractDegree ?? 0;

        // T6: 同一代码路径上已持有 ProgressWindow（MainWindow RunWithProgress 闭包 / CLI 批处理
        // 先 Show() 再进入本方法，OnOpened 已置位 CurrentVisible），把实际并行度写给进度窗口统计。
        // 串行路径（引擎不支持并行或 degree ≤ 1）传 1 → HasParallelDegree=false 自动隐藏（Rule 6）。
        ProgressWindow.CurrentVisible?.SetParallelDegree(ResolveDisplayParallelDegree(archivePath));
        // T9/D7: 列表模式播种（后台列目录 → SeedEntries；不 await，解压不等播种）。
        // TAR/GZ 与非 zip/7z 在 CanSeedEntries 内拦下 → 零 ListEntriesAsync 调用；
        // >5000 条跳过播种转渐进模式。播种结果由 UpdateEntryStatus 的 upsert 兜底。
        TrySeedEntryItemsInBackground(
            ProgressWindow.CurrentVisible, archivePath, password, filteredKeys, ct);

        // 有过滤条件：仅解压匹配条目（统一入口；无 pathOverrides = 保留完整路径）。
        // 注意用 `!= null` 而非 `is { Count: > 0 }`：过滤激活但零匹配（空列表）也必须走
        // ExtractEntriesAsync —— 空列表 = 什么都不解压，若误走 else 全量解压会泄露全部文件。
        if (filteredKeys != null)
        {
            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            if (engine == null)
                throw new NotSupportedException(LocalizationManager.T("Error_UnsupportedArchiveFormat"));

            // 叶子 2/5：过滤分支直连引擎，绕过 ExtractService，故在此单独接密码弹窗兜底。
            // 非过滤分支经 ExtractService（叶子 1）已覆盖，此处不重复包装。
            var (outcome, _) = await PasswordRetryLoop.RunAsync(
                archivePath, password, engine,
                owner: null,
                setStatus: _ => { },
                attempt: async (pwd, token) =>
                    await engine.ExtractEntriesAsync(
                        archivePath, filteredKeys, dest, pwd, progress, token, options),
                ct: ct);

            if (outcome == PasswordRetryOutcome.Cancelled)
                throw new PasswordRetryCancelledException(archivePath);
            // 损坏/无效：弹窗循环已判定继续重弹无意义 → 抛错走既有失败收尾，
            // 禁止落成功路径（否则下方 PathHistoryManager.Record 会记录损坏包的目录）
            if (outcome == PasswordRetryOutcome.CorruptedOrInvalid)
                throw new InvalidDataException(LocalizationManager.T("Status_ArchiveCorrupted"));
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
                // 优先使用当前可见的进度窗口作为 owner（主窗口解压时居中于进度窗口），
                // 回退到传入的 owner（CLI 场景显式传入 ProgressWindow）
                var dialogOwner = ProgressWindow.CurrentVisible ?? owner;
                await dlg.ShowDialog(dialogOwner);

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

    /// <summary>
    /// 计算用于进度窗口统计显示的并行度（T6）：
    /// 引擎不支持并行（7z/TAR/GZ 等 SupportsParallelExtract=false）→ 1（串行，隐藏并行统计）；
    /// 设置值 0/负数 = 引擎自动 → <c>Environment.ProcessorCount</c>（与 ZipEngine 的 0 值语义一致）。
    /// </summary>
    /// <remarks>
    /// CLI 批处理直解路径（<c>App.RunCliDirectExtractBatchAsync</c>）绕过 <see cref="ExtractAsync"/>，
    /// 需自行调用本方法把并行度写给进度窗口，故为 internal 而非 private。
    /// </remarks>
    internal static int ResolveDisplayParallelDegree(string archivePath)
    {
        if (ArchiveEngineFactory.GetEngineByExtension(archivePath)?.SupportsParallelExtract != true)
            return 1;
        int degree = AppSettings.Load()?.ParallelExtractDegree ?? 0;
        return degree > 0 ? degree : Environment.ProcessorCount;
    }

    // ════════════════════════════════════════════
    //  T9/D7: 列表模式条目行播种
    // ════════════════════════════════════════════

    /// <summary>列表模式播种阈值（D7）：条目数超过该值不播种，转渐进模式（避免 10 万行内存 + UI 长停顿）。</summary>
    private const int MaxSeedEntryCount = 5000;

    /// <summary>
    /// 播种资格判定（D7）：<b>TAR/GZ 一律不播</b>（其 <c>ListEntriesAsync</c> 是全流扫描，
    /// 成本≈解压一次，仅为填充预览列表不值得）；其余仅 zip/7z。
    /// 引擎类型兜一道的原因：魔数兜底路径下 <see cref="ArchiveEngineFactory.GetFormatByExtension"/>
    /// 会把未知扩展名判成 Zip，仅按格式判断可能把 TAR/GZ 误放进来。
    /// </summary>
    private static bool CanSeedEntries(string archivePath, IArchiveEngine? engine)
    {
        if (engine == null || engine is TarGzEngine)
            return false;
        if (engine.SupportsParallelExtract)
            return true;
        var format = ArchiveEngineFactory.GetFormatByExtension(archivePath);
        return format is ArchiveFormat.Zip or ArchiveFormat.SevenZip;
    }

    /// <summary>
    /// 选中条目解压的列表模式播种：直接用调用方已持有的条目集（等价于压缩路径「用已知
    /// 源列表播种」的思路），因此不需要 <c>ListEntriesAsync</c>，也不受 TAR/GZ 扫描成本影响
    /// （<see cref="CanSeedEntries"/> 仍按格式把 TAR/GZ 拦下，保持「TAR/GZ 不播种」的统一语义）。
    /// 条目数在内存中已知，超阈值直接放弃，无需先列目录再判数。
    /// </summary>
    private static void TrySeedSelectedEntries(
        ProgressWindow pw, string archivePath, IReadOnlyList<ArchiveItem> entries)
    {
        if (entries.Count == 0 || entries.Count > MaxSeedEntryCount)
            return;
        if (!CanSeedEntries(archivePath, ArchiveEngineFactory.GetEngineByExtension(archivePath)))
            return;

        var seeds = BuildSeedRows(entries, null);
        if (seeds.Count > 0)
            pw.SeedEntries(seeds);
    }

    /// <summary>
    /// 全量/过滤解压的列表模式播种（后台）：zip/7z 在后台线程 <c>ListEntriesAsync</c> 列目录后播种。
    /// 本方法立即返回、不 await —— 解压绝不等待播种；列目录失败/取消/超阈值一律放弃播种，
    /// 由 <see cref="ProgressViewModel.UpdateEntryStatus"/> 的 upsert 兜底（渐进模式）。
    /// </summary>
    /// <remarks>
    /// CLI 批处理直解路径绕过 <see cref="ExtractAsync"/>，需自行调用本方法，故为 internal 而非 private。
    /// </remarks>
    internal static void TrySeedEntryItemsInBackground(
        ProgressWindow? pw, string archivePath, string? password,
        List<string>? filteredKeys, CancellationToken ct)
    {
        if (pw == null)
            return;

        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        if (engine == null)
            return;
        if (!CanSeedEntries(archivePath, engine))
            return; // TAR/GZ / 非 zip/7z：不产生任何 ListEntriesAsync 调用

        _ = SeedEntryItemsAsync(pw, engine, archivePath, password, filteredKeys, ct);
    }

    /// <summary>后台列目录 + 播种（所有异常与取消都在此吞掉，只影响预览列表，不影响解压）。</summary>
    private static async Task SeedEntryItemsAsync(
        ProgressWindow pw, IArchiveEngine engine, string archivePath,
        string? password, List<string>? filteredKeys, CancellationToken ct)
    {
        try
        {
            var items = await engine.ListEntriesAsync(archivePath, password, ct).ConfigureAwait(false);

            // 阈值按原始条目数判定：先学会总数再决定是否播种（>5000 → 一条行都不建）
            if (items.Count == 0 || items.Count > MaxSeedEntryCount)
                return;

            // 过滤激活但零匹配（空列表）= 什么都不解压 → 自然也不播种
            if (filteredKeys is { Count: 0 })
                return;

            var seeds = BuildSeedRows(items, filteredKeys);
            if (seeds.Count > 0)
                pw.SeedEntries(seeds);
        }
        catch (Exception ex)
        {
            // 播种只影响列表预览；失败/取消不得影响解压本身（渐进模式兜底）
            App.DebugLog($"[ExtractFlow] Entry seeding skipped: {ex.Message}");
        }
    }

    /// <summary>
    /// 把条目列表转成播种元组：<c>Key</c> 用 <see cref="ArchiveItem.Name"/>（三引擎均存归一化条目键，
    /// 与引擎上报的 <c>ArchiveProgress.EntryKey</c> 同源）；目录条目被跳过——引擎建目录后不上报终态，
    /// 播出来会是永远等不到上报的 Pending 行。过滤键非 null 时只保留将被实际解压的条目。
    /// </summary>
    private static List<(string Key, string Name, long Size)> BuildSeedRows(
        IReadOnlyList<ArchiveItem> items, List<string>? filteredKeys)
    {
        HashSet<string>? filter = filteredKeys == null
            ? null
            : new HashSet<string>(filteredKeys, StringComparer.Ordinal);

        var seeds = new List<(string Key, string Name, long Size)>(items.Count);
        foreach (var item in items)
        {
            if (item.IsDirectory)
                continue;
            if (filter != null && !filter.Contains(item.Name))
                continue;
            seeds.Add((item.Name, Path.GetFileName(item.Name), item.Size));
        }
        return seeds;
    }
}
