using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 格式检测辅助方法。
/// </summary>
public static class ArchiveFormatHelper
{
    /// <summary>
    /// 根据扩展名判断是否为支持的压缩包格式。
    /// </summary>
    public static bool IsArchiveFile(string path) =>
        ArchiveEngineFactory.GetEngineByExtension(path) != null;

    /// <summary>
    /// 获取压缩包格式。
    /// </summary>
    public static ArchiveFormat GetFormat(string path) =>
        ArchiveEngineFactory.GetFormatByExtension(path);

    /// <summary>
    /// 「打开压缩包」对话框的筛选模式 —— 由设置里「文件类型关联」列表的**右列**（打开展示）派生。
    /// </summary>
    /// <remarks>
    /// 单一事实源：<see cref="ArchiveExtensionCatalog"/>（有哪些格式）+ <see cref="AssocPreferences.GetOpenDialogExtensions"/>
    /// （用户勾了哪些）。没勾的格式不会出现在筛选里，但仍可通过拖拽 / 命令行 / 双击打开；
    /// 对话框下拉里始终有「所有文件」兜底。
    /// </remarks>
    public static IReadOnlyList<string> BuildOpenDialogPatterns(AppSettings settings)
        => BuildOpenDialogPatterns(AssocPreferences.GetOpenDialogExtensions(settings));

    /// <summary>
    /// 按给定扩展名列表构造筛选模式（供设置未就绪的调用点与测试使用；<c>null</c> = 目录默认值）。
    /// </summary>
    /// <param name="extensions">
    /// 要显示的扩展名（形如 <c>.xyz</c> 或 <c>xyz</c>）；大小写、前导点由本方法归一化，非法项忽略。
    /// </param>
    public static IReadOnlyList<string> BuildOpenDialogPatterns(IEnumerable<string>? extensions = null)
    {
        var source = extensions ?? ArchiveExtensionCatalog.DefaultOpenDialogExtensions;
        var patterns = new List<string>();

        foreach (var raw in source)
        {
            if (NormalizeCustomExtension(raw) is not { } ext)
                continue;

            var pattern = "*" + ext;
            if (!patterns.Contains(pattern, StringComparer.OrdinalIgnoreCase))
                patterns.Add(pattern);
        }

        // 分卷首卷恒可见：`.001` 属于「分卷」而不是某个具体格式，用户把它从筛选里去掉的话
        // 分卷包就再也选不到了，代价大于收益。
        foreach (var pattern in ArchiveVolumeSet.VolumeFilterPatterns)
        {
            if (!patterns.Contains(pattern, StringComparer.OrdinalIgnoreCase))
                patterns.Add(pattern);
        }

        // 设置被清空时不至于只剩「所有文件」。
        if (patterns.Count == 0)
        {
            foreach (var ext in ArchiveExtensionCatalog.EngineExtensions)
                patterns.Add("*" + ext);
        }

        return patterns;
    }

    /// <summary>
    /// 自定义扩展名归一化：去空白、转小写、补前导点；空值 / 只有点号 / 含通配符或路径分隔符 → null。
    /// </summary>
    internal static string? NormalizeCustomExtension(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var ext = raw.Trim().ToLowerInvariant();
        if (!ext.StartsWith('.'))
            ext = "." + ext;

        if (ext.Length <= 1)
            return null;
        if (ext.IndexOfAny(['*', '?', '\\', '/']) >= 0)
            return null;
        // ".." / "..." 这类只剩点号的输入不能变成 "*.." 这种无意义模式。
        if (!ext.Any(char.IsLetterOrDigit))
            return null;

        return ext;
    }
}
