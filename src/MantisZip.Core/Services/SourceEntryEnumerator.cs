using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MantisZip.Core.Utils;

namespace MantisZip.Core.Services;

/// <summary>
/// 压缩侧「列表」模式播种用的源条目枚举器。
/// <para>
/// 复用引擎内部的 <see cref="FileScanner.CollectFiles"/>（Key 语义与引擎上报的
/// <c>ArchiveProgress.EntryKey</c> 完全同源 —— 目录源带顶层目录名前缀、文件源取叶名），
/// 因此播种行的 Key 能与引擎终态事件（Completed/Failed/Skipped）精确对上。
/// </para>
/// <para>
/// 这是一次**额外的**目录扫描（引擎内部还会再扫一次）；仅在进度窗口「列表」模式需要
/// 全量 Pending 行时使用，且应由调用方放到后台线程执行。
/// </para>
/// </summary>
public static class SourceEntryEnumerator
{
    /// <summary>
    /// 枚举待打包条目。
    /// </summary>
    /// <param name="sourcePaths">源路径（文件或目录）。</param>
    /// <param name="whitelist">文件白名单（绝对路径，大小写不敏感）；null = 收集全部。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>(Key=条目相对路径, Name=文件名, Size=字节数) 列表。</returns>
    public static IReadOnlyList<(string Key, string Name, long Size)> Enumerate(
        IReadOnlyList<string> sourcePaths,
        IReadOnlyCollection<string>? whitelist = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        IReadOnlySet<string>? set = whitelist is null
            ? null
            : new HashSet<string>(whitelist, StringComparer.OrdinalIgnoreCase);

        // progress 传 null：播种只是枚举，不需要扫描进度上报
        var (files, _) = FileScanner.CollectFiles(sourcePaths.ToArray(), null, cancellationToken, set);

        var result = new List<(string Key, string Name, long Size)>(files.Count);
        foreach (var (fullPath, relativePath) in files)
        {
            long size = 0;
            try { size = new FileInfo(fullPath).Length; } catch { /* 读不到大小按 0 处理 */ }
            result.Add((relativePath, Path.GetFileName(relativePath), size));
        }
        return result;
    }
}
