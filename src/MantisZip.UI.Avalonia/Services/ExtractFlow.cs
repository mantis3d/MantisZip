using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;

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
    /// <param name="filteredKeys">过滤后需实际解压的条目 key 列表；null/空 = 全量解压。</param>
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

        // 有过滤条件：仅解压匹配条目（统一入口；无 pathOverrides = 保留完整路径）
        if (filteredKeys is { Count: > 0 })
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
}
