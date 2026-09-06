using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MantisZip.Core.Abstractions;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 解压后智能打开路径解析（移植自 WPF App.Extract.cs 的 GetCommonRootDirectory / ResolveSmartOpenPathAsync）。
/// 语义：基于压缩包全量条目计算严格公共根目录（非解压子集），打开 dest/公共根目录；失败兜底 dest。
/// </summary>
public static class SmartOpenPathResolver
{
    /// <summary>
    /// 获取压缩包内所有非目录条目的公共根目录。
    /// 例如压缩包内容为 my_project/a.txt、my_project/b.txt → 返回 "my_project"。
    /// 若条目不共享公共根目录（如 a.txt、b.txt 混在），返回 null。
    /// </summary>
    public static string? GetCommonRootDirectory(IReadOnlyList<ArchiveItem> entries)
    {
        string? commonRoot = null;
        foreach (var entry in entries)
        {
            if (entry.IsDirectory) continue;
            var path = entry.Name ?? entry.FullPath ?? "";
            var firstSlash = path.IndexOf('/');
            if (firstSlash < 0) return null;
            var root = path[..firstSlash];
            if (commonRoot == null)
                commonRoot = root;
            else if (!string.Equals(commonRoot, root, StringComparison.Ordinal))
                return null;
        }
        return commonRoot;
    }

    /// <summary>
    /// 解压后智能决定打开哪个路径。
    /// 如果压缩包内所有条目共享一个公共根目录，打开 dest/公共根目录，
    /// 否则直接打开 dest。例如压缩包只包含 my_project/... 目录树，
    /// 原生解压到 dest/，实际内容在 dest/my_project/，用户希望打开后者。
    /// 任何异常（含 ListEntriesAsync 失败/加密未提供密码）捕获后记录日志并返回 dest 兜底。
    /// </summary>
    public static async Task<string> ResolveSmartOpenPathAsync(
        string archivePath, string dest, IArchiveEngine engine, string? password)
    {
        try
        {
            var entries = await engine.ListEntriesAsync(archivePath, password);
            var commonRoot = GetCommonRootDirectory(entries);
            if (commonRoot != null)
                return Path.Combine(dest, commonRoot);
        }
        catch (Exception ex)
        {
            App.DebugLog($"SmartOpenPathResolver.ResolveSmartOpenPathAsync: failed for '{archivePath}': {ex.Message}");
        }
        return dest;
    }
}