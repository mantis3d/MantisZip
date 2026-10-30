using MantisZip.Core.Utils;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 文件关联与「打开压缩包」筛选的读写口径（单一入口）。
/// </summary>
/// <remarks>
/// 两个开关共用一套「扩展名集合 + 目录默认值」的模型：
/// <list type="bullet">
/// <item><see cref="GetAssocExtensions"/>：注册到资源管理器的扩展名（决定双击 / 图标归谁）。</item>
/// <item><see cref="GetOpenDialogExtensions"/>：应用内「打开压缩包」对话框筛选里显示的扩展名。</item>
/// </list>
/// 集合为 <c>null</c> 时回落到 <see cref="ArchiveExtensionCatalog"/> 的默认值，
/// 这样老配置升级后不会突然少一堆格式。
/// </remarks>
public static class AssocPreferences
{
    /// <summary>把扩展名列表归一化：去空白、小写、补前导点、去重、保持顺序。</summary>
    public static List<string> Normalize(IEnumerable<string>? extensions)
    {
        var result = new List<string>();
        if (extensions == null) return result;

        foreach (var raw in extensions)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var ext = ArchiveExtensionCatalog.NormalizeExtension(raw);
            if (ext.Length <= 1) continue;
            if (!result.Contains(ext, StringComparer.Ordinal)) result.Add(ext);
        }

        return result;
    }

    /// <summary>当前应注册关联的扩展名集合（null 设置 → 目录默认值 + 用户自定义扩展名）。</summary>
    public static IReadOnlyList<string> GetAssocExtensions(AppSettings settings)
        => settings.AssocExtensions ?? AppendCustom(ArchiveExtensionCatalog.DefaultAssocExtensions, settings);

    /// <summary>当前「打开压缩包」筛选应显示的扩展名集合（null 设置 → 目录默认值 + 用户自定义扩展名）。</summary>
    public static IReadOnlyList<string> GetOpenDialogExtensions(AppSettings settings)
        => settings.OpenDialogExtensions ?? AppendCustom(ArchiveExtensionCatalog.DefaultOpenDialogExtensions, settings);

    /// <summary>
    /// 老配置（集合为 null）里的自定义扩展名默认都算启用 —— 与迁移前「列表里有就等于已关联」一致。
    /// </summary>
    private static List<string> AppendCustom(IEnumerable<string> defaults, AppSettings settings)
    {
        var list = new List<string>(defaults);
        foreach (var ext in Normalize(settings.CustomAssocExtensions))
        {
            if (!list.Contains(ext, StringComparer.Ordinal)) list.Add(ext);
        }
        return list;
    }

    /// <summary>该扩展名当前是否注册了关联。</summary>
    public static bool IsAssocEnabled(AppSettings settings, string extension)
    {
        var ext = ArchiveExtensionCatalog.NormalizeExtension(extension);
        // .tar.gz 与 .tgz 同义：任一在集合里都算启用
        if (ext is ".tgz" or ".tar.gz")
        {
            return GetAssocExtensions(settings).Any(e =>
                string.Equals(ArchiveExtensionCatalog.NormalizeExtension(e), ".tgz", StringComparison.Ordinal)
                || string.Equals(ArchiveExtensionCatalog.NormalizeExtension(e), ".tar.gz", StringComparison.Ordinal));
        }

        return GetAssocExtensions(settings)
            .Any(e => string.Equals(ArchiveExtensionCatalog.NormalizeExtension(e), ext, StringComparison.Ordinal));
    }

    /// <summary>该扩展名当前是否出现在「打开压缩包」筛选里。</summary>
    public static bool IsOpenDialogVisible(AppSettings settings, string extension)
    {
        var ext = ArchiveExtensionCatalog.NormalizeExtension(extension);
        return GetOpenDialogExtensions(settings)
            .Any(e => string.Equals(ArchiveExtensionCatalog.NormalizeExtension(e), ext, StringComparison.Ordinal));
    }

    /// <summary>设置某个扩展名的关联开关。</summary>
    public static void SetAssocEnabled(AppSettings settings, string extension, bool enabled)
    {
        var ext = ArchiveExtensionCatalog.NormalizeExtension(extension);
        var list = new List<string>(GetAssocExtensions(settings));

        if (ext is ".tgz" or ".tar.gz")
        {
            list.RemoveAll(e => ArchiveExtensionCatalog.NormalizeExtension(e) is ".tgz" or ".tar.gz");
            if (enabled) list.Add(ext);
        }
        else
        {
            list.RemoveAll(e => string.Equals(ArchiveExtensionCatalog.NormalizeExtension(e), ext, StringComparison.Ordinal));
            if (enabled) list.Add(ext);
        }

        settings.AssocExtensions = Normalize(list);
    }

    /// <summary>设置某个扩展名是否出现在「打开压缩包」筛选里。</summary>
    public static void SetOpenDialogVisible(AppSettings settings, string extension, bool visible)
    {
        var ext = ArchiveExtensionCatalog.NormalizeExtension(extension);
        var list = new List<string>(GetOpenDialogExtensions(settings));
        list.RemoveAll(e => string.Equals(ArchiveExtensionCatalog.NormalizeExtension(e), ext, StringComparison.Ordinal));
        if (visible) list.Add(ext);
        settings.OpenDialogExtensions = Normalize(list);
    }

    /// <summary>把老配置的 7 个布尔开关迁移成集合（只会执行一次：迁移后写回集合，布尔不再被读）。</summary>
    public static List<string> MigrateLegacyFlags(AppSettings settings)
    {
        var migrated = new List<string>();
        if (settings.AssocZip) migrated.Add(".zip");
        if (settings.Assoc7z) migrated.Add(".7z");
        if (settings.AssocRar) migrated.Add(".rar");
        if (settings.AssocTar) migrated.Add(".tar");
        if (settings.AssocTarGz) migrated.Add(".tgz");
        if (settings.AssocGz) migrated.Add(".gz");
        if (settings.AssocIso) migrated.Add(".iso");
        // 自定义扩展名：老模型里「在列表里」就等同于「已关联」
        if (settings.CustomAssocExtensions != null) migrated.AddRange(settings.CustomAssocExtensions);
        return Normalize(migrated);
    }
}
