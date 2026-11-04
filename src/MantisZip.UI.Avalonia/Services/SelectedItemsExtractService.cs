using MantisZip.Core.Abstractions;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 选中条目解压服务：统一「拿到输出路径后」的解压流程。
/// 右键「解压选中项到…」与拖拽解压都调用本服务，差异仅剩获取输出路径的方式。
/// 进度窗口、错误呈现由调用方负责（本服务不创建窗口）。
/// </summary>
public sealed class SelectedItemsExtractService
{
    /// <summary>
    /// 解压选中的条目到指定目录。
    /// </summary>
    /// <param name="archivePath">压缩包路径。</param>
    /// <param name="password">密码（可为 null）。</param>
    /// <param name="entries">已展开的条目列表（目录已展开为内部文件）。</param>
    /// <param name="destinationPath">目标目录。</param>
    /// <param name="conflictAction">冲突策略字符串（AppSettings.FileConflictAction 值，6 策略全支持）。</param>
    /// <param name="currentFolder">当前浏览的压缩包内路径（路径裁剪锚点，ExtractPreserveFullPath=false 时裁剪其前缀）。</param>
    /// <param name="preserveFullPath">是否保留完整路径（ExtractPreserveFullPath 设置）。</param>
    /// <param name="conflictDialog">Ask 冲突弹窗回调。返回用户选择与是否应用到全部；用户取消整个操作时应抛 OperationCanceledException。</param>
    /// <param name="progress">进度回调（由调用方创建，模态/非模态窗口均可）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="nameEncoding">文件名代码页（null = 自动检测）。</param>
    /// <param name="livePolicy">
    /// 实时冲突策略源（进度窗口「冲突时:」下拉）；null = 用 <paramref name="conflictAction"/> 静态策略。
    /// </param>
    /// <exception cref="OperationCanceledException">用户取消整个操作（Ask 冲突弹窗）。</exception>
    public async Task ExtractEntriesAsync(
        string archivePath,
        string? password,
        IReadOnlyList<ArchiveItem> entries,
        string destinationPath,
        string conflictAction,
        string currentFolder,
        bool preserveFullPath,
        Func<FileConflictInfo, Task<(FileConflictAction Action, bool ApplyToAll)>>? conflictDialog,
        IProgress<ArchiveProgress> progress,
        CancellationToken cancellationToken,
        string? nameEncoding = null,
        Func<FileConflictAction>? livePolicy = null)
    {
        // Build entryKeys and pathOverrides via 统一路径计算（单一事实来源，预览树共用）
        var entryKeys = new List<string>();
        var pathOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in entries)
        {
            var key = item.FullPath ?? item.Name;
            entryKeys.Add(key);

            // 净化后的目标相对路径：ExtractPathResolver = TrimCurrentFolderPrefix + SanitizeEntryPath
            var safeEntryPath = ExtractPathResolver.ResolveRelativePath(key, currentFolder, preserveFullPath);
            pathOverrides[key] = FileConflictHelper.GetSafePath(destinationPath, safeEntryPath);
        }

        var options = CreateExtractOptions(conflictAction, conflictDialog, nameEncoding, livePolicy);

        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        if (engine == null) throw new NotSupportedException(LocalizationManager.T("Error_UnsupportedArchiveFormat"));

        // 智能并行解压：选中条目解压无对话框覆盖（callerDegree = null），经 resolver 5 步决策链解析
        var settings = AppSettings.Load();
        var resolvedDegree = await ParallelDegreeResolver.ResolveAsync(
            callerDegree: null,
            settings: new ParallelExtractSettings
            {
                EnableParallel = settings?.EnableParallelExtract ?? true,
                SmartParallel = settings?.SmartParallelDegree ?? true,
                SmartSsdDegree = settings?.SmartSsdDegree ?? 12,
                SmartHddDegree = settings?.SmartHddDegree ?? 8,
                LargeArchivePreTest = settings?.LargeArchivePreTest ?? true,
                ManualDegree = settings?.ParallelExtractDegree ?? 0
            },
            archivePath: archivePath,
            destinationPath: destinationPath,
            password: password,
            engineSupportsParallel: engine.SupportsParallelExtract,
            // 预测试阶段 resolver 上报的状态占位 key 在此替换为本地化文案
            progress: new PreTestStatusLocalizingProgress(progress),
            ct: cancellationToken);

        if (options != null)
            options.ParallelExtractDegree = resolvedDegree;

        // 所有格式统一走按条目提取（TarGzEngine 已实现，不再降级全量解压）
        // 叶子 3/5：选中条目解压直连引擎 → 密码弹窗兜底在此（拖拽解压经本方法自动覆盖）
        var (outcome, _) = await PasswordRetryLoop.RunAsync(
            archivePath, password, engine,
            owner: null,
            setStatus: _ => { },
            attempt: async (pwd, token) =>
                await engine.ExtractEntriesAsync(
                    archivePath, entryKeys, destinationPath,
                    pwd, progress, token, options, pathOverrides),
            ct: cancellationToken);

        if (outcome == PasswordRetryOutcome.Cancelled)
            throw new PasswordRetryCancelledException(archivePath);
        // 损坏/无效：弹窗循环已判定继续重弹无意义 → 抛错走既有失败收尾，
        // 禁止落成功路径（否则调用方记录路径历史且 CLI 直解批处理会误删损坏源包）
        if (outcome == PasswordRetryOutcome.CorruptedOrInvalid)
            throw new InvalidDataException(LocalizationManager.T("Status_ArchiveCorrupted"));
    }

    /// <summary>
    /// 将 ExtractSettingsViewModel 的冲突策略字符串映射到 <see cref="FileConflictAction"/>。
    /// 支持设置中的全部 6 种值（含带连字符的 "overwrite-if-older" / "overwrite-if-smaller"）。
    /// </summary>
    internal static FileConflictAction MapConflictActionString(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "ask" => FileConflictAction.Ask,
            "rename" => FileConflictAction.Rename,
            "skip" => FileConflictAction.Skip,
            "overwriteifolder" or "overwrite_if_older" or "overwrite-if-older" => FileConflictAction.OverwriteIfOlder,
            "overwriteifsmaller" or "overwrite_if_smaller" or "overwrite-if-smaller" => FileConflictAction.OverwriteIfSmaller,
            _ => FileConflictAction.Overwrite,
        };
    }

    /// <summary>
    /// 把冲突动作反向映射成策略码（进度窗口下拉用）。
    /// 用于冲突弹窗里勾了「应用到全部」后把下拉同步成实际生效的动作 —— 唯一写入口，
    /// 让「当前策略」始终可见可改。返回 <see langword="null"/> 表示无需同步（Ask）。
    /// </summary>
    internal static string? ToConflictActionCode(FileConflictAction action) => action switch
    {
        FileConflictAction.Overwrite => "overwrite",
        FileConflictAction.OverwriteIfOlder => "overwrite-if-older",
        FileConflictAction.OverwriteIfSmaller => "overwrite-if-smaller",
        FileConflictAction.Rename => "rename",
        FileConflictAction.Skip => "skip",
        _ => null,
    };

    /// <summary>
    /// 集中创建解压选项，统一处理冲突回调 + ApplyToAll 记忆。
    /// </summary>
    /// <param name="conflictAction">冲突策略字符串值。</param>
    /// <param name="conflictDialog">Ask 冲突弹窗回调（null 时 Ask 降级为直接弹引擎默认）。</param>
    /// <param name="nameEncoding">文件名代码页（null = 自动检测）。</param>
    /// <param name="livePolicy">
    /// 实时冲突策略源（进度窗口「冲突时:」下拉）。传入后改为每次冲突都问它，
    /// 因而「暂停 → 改下拉 → 继续」立即生效；<see langword="null"/> 时退回静态 <paramref name="conflictAction"/>，
    /// 与引入前的行为逐字节一致。
    /// </param>
    /// <returns>ArchiveOptions（Overwrite 也返回显式 options，调用方可直接设置 ParallelExtractDegree）。</returns>
    internal static ArchiveOptions CreateExtractOptions(
        string conflictAction,
        Func<FileConflictInfo, Task<(FileConflictAction Action, bool ApplyToAll)>>? conflictDialog,
        string? nameEncoding = null,
        Func<FileConflictAction>? livePolicy = null)
    {
        // 实时策略模式：策略本身由进度窗口持有，可能在解压途中被改写，
        // 所以静态 ConflictAction 只作兜底，引擎按 provider 逐个冲突取值。
        if (livePolicy is not null)
        {
            bool liveApplyToAll = false;
            FileConflictAction liveChosen = FileConflictAction.Ask;

            return new ArchiveOptions
            {
                ConflictAction = FileConflictAction.Ask,
                ConflictActionProvider = livePolicy,
                ReadEncoding = nameEncoding,
                ConflictResolverAsync = conflictDialog == null
                    ? null
                    : async info =>
                    {
                        if (liveApplyToAll)
                        {
                            CoreLog.ConflictTrace("resolver-live-memo action={0} file='{1}'", liveChosen, info.FilePath);
                            return liveChosen;
                        }

                        // provider 已判断为 Ask 才会进 resolver；若此刻下拉已被改成自动策略
                        // （典型：暂停时改的），直接按新策略走，不再弹窗 —— 否则会出现
                        // 「下拉显示跳过、弹窗还在问覆盖」的矛盾。
                        var current = livePolicy();
                        if (current != FileConflictAction.Ask)
                        {
                            CoreLog.ConflictTrace("resolver-live-policy action={0} file='{1}'", current, info.FilePath);
                            return current;
                        }

                        CoreLog.ConflictTrace("resolver-live-enter file='{0}'", info.FilePath);
                        var (resultAction, applyAll) = await conflictDialog(info);
                        CoreLog.ConflictTrace("resolver-live-returned action={0} applyAll={1} file='{2}'",
                            resultAction, applyAll, info.FilePath);

                        if (applyAll && resultAction != FileConflictAction.Ask)
                        {
                            liveApplyToAll = true;
                            liveChosen = resultAction;
                        }

                        return resultAction;
                    },
            };
        }

        var action = MapConflictActionString(conflictAction);
        if (action == FileConflictAction.Overwrite)
            return new ArchiveOptions { ConflictAction = FileConflictAction.Overwrite, ReadEncoding = nameEncoding }; // 默认行为即覆盖

        if (action != FileConflictAction.Ask || conflictDialog == null)
            return new ArchiveOptions { ConflictAction = action, ReadEncoding = nameEncoding };

        // Ask 模式：使用异步回调弹窗 + ApplyToAll 记忆
        bool applyToAll = false;
        FileConflictAction? chosenAction = null;

        return new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ReadEncoding = nameEncoding,
            ConflictResolverAsync = async info =>
            {
                if (applyToAll && chosenAction.HasValue)
                {
                    // 诊断（conflict.log）：命中「应用到全部」记忆，本次不弹窗
                    CoreLog.ConflictTrace("resolver-memo action={0} file='{1}'", chosenAction.Value, info.FilePath);
                    return chosenAction.Value;
                }

                CoreLog.ConflictTrace("resolver-enter file='{0}'", info.FilePath);
                var (resultAction, applyAll) = await conflictDialog(info);
                CoreLog.ConflictTrace("resolver-returned action={0} applyAll={1} file='{2}'",
                    resultAction, applyAll, info.FilePath);

                if (applyAll)
                {
                    applyToAll = true;
                    chosenAction = resultAction;
                }

                return resultAction;
            },
        };
    }
}
