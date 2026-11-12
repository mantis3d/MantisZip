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
    /// <param name="rootMode">
    /// 「包内根目录」四态；必须与真正执行压缩时的取值**完全一致**，否则进度窗「列表」模式
    /// 里播种的 Pending 行和引擎实际上报的 Key 会对不上（两者都从
    /// <see cref="FileScanner.CollectFiles"/> 出，这里是唯一对接口）。
    /// </param>
    /// <param name="wrapRoot">加一层根目录时使用的根名；空 = 由引擎回落到压缩包文件名。</param>
    /// <param name="archivePath">目标压缩包路径：<paramref name="wrapRoot"/> 为空时用它去扩展名当根名。</param>
    /// <returns>(Key=条目相对路径, Name=文件名, Size=字节数) 列表。</returns>
    public static IReadOnlyList<(string Key, string Name, long Size)> Enumerate(
        IReadOnlyList<string> sourcePaths,
        IReadOnlyCollection<string>? whitelist = null,
        CancellationToken cancellationToken = default,
        RootDirectoryMode rootMode = RootDirectoryMode.Original,
        string? wrapRoot = null,
        string? archivePath = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        IReadOnlySet<string>? set = whitelist is null
            ? null
            : new HashSet<string>(whitelist, StringComparer.OrdinalIgnoreCase);

        // progress 传 null：播种只是枚举，不需要扫描进度上报
        var (files, _) = FileScanner.CollectFiles(sourcePaths.ToArray(), null, cancellationToken, set, rootMode, wrapRoot, archivePath);

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
