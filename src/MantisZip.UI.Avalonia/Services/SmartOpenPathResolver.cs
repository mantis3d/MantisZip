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
    /// 解压后该打开哪个文件夹 —— **已知根策略时的纯函数版本**（智能解压两档走它）。
    /// <para>
    /// 智能解压的落点由根策略决定，而策略是解压前就定下来的，没必要解完再列一遍包去猜
    /// （解压后源包可能已被「解压完成后删除压缩包」删掉，再列会失败）。
    /// </para>
    /// <list type="bullet">
    /// <item>加了根 → 内容都在 <c>dest\&lt;加的那层&gt;</c>，打开它，否则用户看到 dest 里只有一个文件夹。</item>
    /// <item>剥了根 → 内容被摊平进 dest，直接打开 dest。</item>
    /// <item>两者都没动（智能加根遇单根包 / 智能去根遇散装包）→ 回到「按公共根目录猜」的老逻辑。</item>
    /// </list>
    /// </summary>
    /// <param name="dest">实际解压目标目录（智能解压恒为压缩包所在目录）。</param>
    /// <param name="stripRoot">被剥掉的顶层根段；null = 没剥。</param>
    /// <param name="addRoot">被添加的顶层根段；null = 没加。</param>
    /// <param name="commonRoot">包内非目录条目的公共根目录（未动时可据此再进一层），可为 null。</param>
    public static string ResolveOpenPath(string dest, string? stripRoot, string? addRoot, string? commonRoot)
    {
        if (!string.IsNullOrEmpty(addRoot))
            return Path.Combine(dest, addRoot);

        if (!string.IsNullOrEmpty(stripRoot))
            return dest;

        // 结构没动：包自带的唯一根目录仍在 dest 下面一层
        if (!string.IsNullOrEmpty(commonRoot))
            return Path.Combine(dest, commonRoot);

        return dest;
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

    /// <summary>
    /// 已知根策略时的智能打开路径：不列包，直接按 <paramref name="stripRoot"/> /
    /// <paramref name="addRoot"/> 判定；都没动才退回列包猜公共根目录。
    /// 列包失败（含加密无密码）一律兜底 <paramref name="dest"/>，绝不让「打开文件夹」这一步挡在解压成功之后。
    /// </summary>
    public static async Task<string> ResolveSmartOpenPathAsync(
        string archivePath,
        string dest,
        IArchiveEngine? engine,
        string? password,
        string? stripRoot,
        string? addRoot)
    {
        // 已加根：答案确定，不需要列包
        if (!string.IsNullOrEmpty(addRoot))
            return Path.Combine(dest, addRoot);

        // 已剥根：内容摊平进 dest
        if (!string.IsNullOrEmpty(stripRoot))
            return dest;

        // 结构没动 → 老逻辑（按公共根目录再进一层）；引擎缺失就只开 dest
        if (engine == null)
            return dest;

        return await ResolveSmartOpenPathAsync(archivePath, dest, engine, password).ConfigureAwait(false);
    }
}