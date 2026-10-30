using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 一条「可识别扩展名」的定义。
/// </summary>
/// <param name="Extension">扩展名（小写、带前导点，如 <c>.zip</c> / <c>.tar.gz</c> / <c>.001</c>）。</param>
/// <param name="Format">按扩展名路由到的归档格式。</param>
/// <param name="IsSplitVolume">是否为分卷命名（<c>.001</c>）：占位扩展名，真实格式由基名决定。</param>
/// <param name="ListedInSettings">是否在「设置 → 文件类型」列表里显示（<c>.tar.gz</c> 与 <c>.tgz</c> 同义，只列一行）。</param>
/// <param name="AssociateByDefault">默认是否注册文件关联（写资源管理器）。</param>
/// <param name="ShowInDialogByDefault">默认是否出现在「打开压缩包」对话框的筛选里。</param>
public sealed record ArchiveExtensionInfo(
    string Extension,
    ArchiveFormat Format,
    bool IsSplitVolume = false,
    bool ListedInSettings = true,
    bool AssociateByDefault = true,
    bool ShowInDialogByDefault = true);

/// <summary>
/// 「哪些扩展名算压缩包」的**单一事实源**。
/// </summary>
/// <remarks>
/// 在此之前这件事散落成多份清单，加一个格式要改好几处且很容易漏：
/// <list type="bullet">
/// <item><see cref="ArchiveEngineFactory.SupportedExtensions"/>（引擎路由与格式识别）</item>
/// <item><c>ShellIntegration.ArchiveExtensions</c>（文件关联注册）</item>
/// <item>设置页「文件类型」列表里手写的 builtins 数组，以及 <see cref="ArchiveVolumeSet.VolumeFilterPatterns"/>（打开对话框筛选）</item>
/// </list>
/// 现在都从本目录派生：加格式 = 在这里加一行。
/// 用户侧的两个开关（是否关联 / 是否在打开对话框里显示）以集合形式存进 <c>AppSettings</c>，
/// 未设置时回落到这里的默认值。
/// </remarks>
public static class ArchiveExtensionCatalog
{
    /// <summary>全量定义（顺序即设置页与筛选模式的显示顺序）。</summary>
    public static readonly IReadOnlyList<ArchiveExtensionInfo> All =
    [
        new(".zip", ArchiveFormat.Zip),
        new(".7z", ArchiveFormat.SevenZip),
        new(".rar", ArchiveFormat.Rar),
        new(".tar", ArchiveFormat.Tar),
        new(".tgz", ArchiveFormat.Tar),
        // .tar.gz 与 .tgz 同义：引擎路由与筛选都要认，但设置页只列一行
        new(".tar.gz", ArchiveFormat.Tar, ListedInSettings: false),
        new(".gz", ArchiveFormat.GZip),
        // ISO 默认不抢关联（现状如此），但默认出现在打开对话框里
        new(".iso", ArchiveFormat.Iso, AssociateByDefault: false),
        // 本质是 ZIP：能打开能解压，但默认不抢关联（用户的 .jar 可能归 JDK / IDE，issue #54）
        new(".jar", ArchiveFormat.Zip, AssociateByDefault: false),
        new(".war", ArchiveFormat.Zip, AssociateByDefault: false),
        new(".ear", ArchiveFormat.Zip, AssociateByDefault: false),
        new(".apk", ArchiveFormat.Zip, AssociateByDefault: false),
        // 分卷首卷：真实格式由基名决定（见 ArchiveVolumeSet），默认不抢关联（.001 也可能是非压缩分卷）
        new(".001", ArchiveFormat.Zip, IsSplitVolume: true, AssociateByDefault: false),
    ];

    /// <summary>引擎可识别的扩展名（不含分卷占位扩展名），顺序与注册顺序一致。</summary>
    public static readonly IReadOnlyList<string> EngineExtensions =
        All.Where(info => !info.IsSplitVolume).Select(info => info.Extension).ToArray();

    /// <summary>在设置页里逐行显示的条目（<c>.tar.gz</c> 等别名不重复显示）。</summary>
    public static readonly IReadOnlyList<ArchiveExtensionInfo> Listed =
        All.Where(info => info.ListedInSettings).ToArray();

    /// <summary>默认参与文件关联的扩展名。</summary>
    public static readonly IReadOnlyList<string> DefaultAssocExtensions =
        All.Where(info => info.AssociateByDefault).Select(info => info.Extension).ToArray();

    /// <summary>默认出现在「打开压缩包」筛选里的扩展名。</summary>
    public static readonly IReadOnlyList<string> DefaultOpenDialogExtensions =
        All.Where(info => info.ShowInDialogByDefault).Select(info => info.Extension).ToArray();

    /// <summary>把用户输入的扩展名归一化为目录里的写法（去空白 + 小写 + 补前导点）。</summary>
    public static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim().ToLowerInvariant();
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }

    /// <summary>按扩展名查定义（大小写不敏感；未收录时返回 false）。</summary>
    public static bool TryGet(string? extension, out ArchiveExtensionInfo info)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            info = null!;
            return false;
        }

        var normalized = NormalizeExtension(extension);
        foreach (var entry in All)
        {
            if (string.Equals(entry.Extension, normalized, StringComparison.Ordinal))
            {
                info = entry;
                return true;
            }
        }

        info = null!;
        return false;
    }

    /// <summary>扩展名是否已被目录收录。</summary>
    public static bool Contains(string? extension) => TryGet(extension, out _);
}
