using MantisZip.Core.Abstractions;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;

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
        CancellationToken cancellationToken)
    {
        // Build entryKeys and pathOverrides (with ExtractPreserveFullPath logic)
        var entryKeys = new List<string>();
        var pathOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in entries)
        {
            var key = item.FullPath ?? item.Name;
            entryKeys.Add(key);

            // 路径裁剪：preserveFullPath=false 时裁剪当前浏览文件夹前缀
            var outputEntryPath = key;
            if (!preserveFullPath && !string.IsNullOrEmpty(currentFolder))
            {
                var cf = currentFolder.TrimEnd('/') + "/";
                if (outputEntryPath.StartsWith(cf, StringComparison.OrdinalIgnoreCase))
                    outputEntryPath = outputEntryPath.Substring(cf.Length);
            }

            var safeEntryPath = FileConflictHelper.SanitizeEntryPath(outputEntryPath);
            pathOverrides[key] = FileConflictHelper.GetSafePath(destinationPath, safeEntryPath);
        }

        var options = CreateExtractOptions(conflictAction, conflictDialog);

        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        if (engine == null) throw new NotSupportedException(LocalizationManager.T("Error_UnsupportedArchiveFormat"));

        // 所有格式统一走按条目提取（TarGzEngine 已实现，不再降级全量解压）
        await engine.ExtractEntriesAsync(
            archivePath, entryKeys, destinationPath,
            password, progress, cancellationToken, options, pathOverrides);
    }

    /// <summary>
    /// 将 ExtractSettingsViewModel 的冲突策略字符串映射到 <see cref="FileConflictAction"/>。
    /// 支持设置中的全部 6 种值（含带连字符的 "overwrite-if-older" / "overwrite-if-smaller"）。
    /// </summary>
    private static FileConflictAction MapConflictActionString(string value)
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
    /// 集中创建解压选项，统一处理冲突回调 + ApplyToAll 记忆。
    /// </summary>
    /// <param name="conflictAction">冲突策略字符串值。</param>
    /// <param name="conflictDialog">Ask 冲突弹窗回调（null 时 Ask 降级为直接弹引擎默认）。</param>
    /// <returns>ArchiveOptions，Overwrite 且无 resolver 时返回 null。</returns>
    private static ArchiveOptions? CreateExtractOptions(
        string conflictAction,
        Func<FileConflictInfo, Task<(FileConflictAction Action, bool ApplyToAll)>>? conflictDialog)
    {
        var action = MapConflictActionString(conflictAction);
        if (action == FileConflictAction.Overwrite)
            return null; // 默认行为无需传 options

        if (action != FileConflictAction.Ask || conflictDialog == null)
            return new ArchiveOptions { ConflictAction = action };

        // Ask 模式：使用异步回调弹窗 + ApplyToAll 记忆
        bool applyToAll = false;
        FileConflictAction? chosenAction = null;

        return new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = async info =>
            {
                if (applyToAll && chosenAction.HasValue)
                    return chosenAction.Value;

                var (resultAction, applyAll) = await conflictDialog(info);

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
