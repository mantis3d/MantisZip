using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MantisZip.Core.Utils;

/// <summary>
/// 路径共性计算（公共父目录 / 盘符根判定）。
///
/// 为什么单独成类：这套算法原先只以 <c>App.FindCommonParent</c> 的形式存在于 UI 层
/// （src/MantisZip.UI.Avalonia/App.CliCompress.cs），而 Core 的
/// <see cref="CompressPathPlanner.ResolveAutoName"/> 与
/// <see cref="FileNameTemplate"/> 的 <c>{parent}</c> 都需要它。
/// 下沉到 Core 后，UI 层改为委派，避免出现第 4 份拷贝
/// （ShellExt 里 ContextMenuHandler 的副本跨程序集，只能保持镜像并注释指向这里）。
/// </summary>
public static class PathCommonality
{
    /// <summary>
    /// 求一组路径的公共父目录。<paramref name="paths"/> 为空、任一元素取不到目录、
    /// 或公共前缀退化到根（如 <c>C:\</c>）时返回 null。
    /// </summary>
    public static string? FindCommonParent(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return null;

        var parents = paths.Select(p =>
        {
            var trimmed = p.TrimEnd('\\', '/');
            // 文件与目录都取父目录：目录源也要退一层才能与文件源比较
            return Path.GetDirectoryName(trimmed) ?? "";
        }).ToList();

        if (parents.Any(string.IsNullOrEmpty)) return null;

        var common = parents[0];
        for (int i = 1; i < parents.Count; i++)
        {
            while (!IsWithin(common, parents[i]))
            {
                var parent = Path.GetDirectoryName(common);
                if (parent == null) return null;
                common = parent;
            }
        }
        return common;
    }

    /// <summary>
    /// 判断 <paramref name="candidate"/> 是否落在 <paramref name="ancestor"/> 之内（含两者相等）。
    /// 前缀命中后还要求下一字符是目录分隔符 —— 否则 <c>D:\a</c> 会被当成 <c>D:\ab</c> 的祖先
    /// （<c>"ab"</c> 恰以 <c>"a"</c> 开头），公共父目录会退化成 <c>D:\a</c> 而不是 <c>D:\</c>。
    /// 盘符根（<c>D:\</c>）去掉尾分隔符后长度相等即同层，无需再看下一字符。
    /// </summary>
    private static bool IsWithin(string ancestor, string candidate)
    {
        var lhs = ancestor.TrimEnd('\\', '/');
        var rhs = candidate.TrimEnd('\\', '/');

        if (rhs.Equals(lhs, StringComparison.OrdinalIgnoreCase)) return true;
        if (rhs.Length <= lhs.Length) return false;
        if (!rhs.StartsWith(lhs, StringComparison.OrdinalIgnoreCase)) return false;

        var next = rhs[lhs.Length];
        return next == '\\' || next == '/';
    }

    /// <summary>
    /// 判断是否为盘符根（<c>C:</c> / <c>C:\</c>）。尾分隔符会先被去掉再判断。
    /// </summary>
    public static bool IsDriveRoot(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var trimmed = path.TrimEnd('\\', '/');
        return trimmed.Length == 2 && trimmed[1] == ':';
    }
}
