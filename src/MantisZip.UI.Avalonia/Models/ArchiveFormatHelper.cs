using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Services;

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
    /// 获取当前路径的**有效格式**（= 哪个读取器会去读它，分卷 / SFX 已解析成 SevenZip）。
    /// </summary>
    /// <remarks>
    /// UI 里 <c>_currentFormat</c> 及其全部下游（预览、双击打开、缩略图、固实守卫、代码页、注释、
    /// 「压缩后大小」列、会话密码缓存键）都建立在它之上，所以这里必须走
    /// <see cref="ArchiveEngineFactory.ResolveReaderFormat"/>：曾经直接调 GetFormatByExtension，
    /// 导致 <c>1.7z.001</c> 被标成 Zip（2026-11-05，KI-017）。
    /// </remarks>
    public static ArchiveFormat GetFormat(string path) =>
        ArchiveEngineFactory.ResolveReaderFormat(path);

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
    /// 「打开压缩包」对话框的文件类型下拉项：压缩包（**默认选中**）/ 自解压程序 / 所有文件。
    /// </summary>
    /// <remarks>
    /// 自解压程序（.exe）单独一项且**不默认选中**（issue #36）：默认筛「压缩包」时目录里的
    /// 普通 exe / 安装程序不会混进列表，需要打开 SFX 的用户才切这一项；而「所有文件」照旧兜底。
    /// 注意别把 <c>*.exe</c> 塞进压缩包那组模式里——那等于默认把整个目录的可执行文件都列出来。
    /// </remarks>
    public static IReadOnlyList<FileTypeOption> BuildOpenDialogFileTypes(AppSettings settings)
        =>
        [
            new FileTypeOption(
                LocalizationManager.T("Picker_FileTypeArchive"),
                [.. BuildOpenDialogPatterns(settings)]),
            new FileTypeOption(
                LocalizationManager.T("Picker_FileTypeSfxExe"),
                ["*.exe"]),
            new FileTypeOption(
                LocalizationManager.T("Picker_FileTypeAll"),
                [])
        ];

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
