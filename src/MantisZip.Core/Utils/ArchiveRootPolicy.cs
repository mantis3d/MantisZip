namespace MantisZip.Core.Utils;

/// <summary>
/// 「包内根目录」处理方式（压缩端与解压端共用同一套四态）。
/// <para>
/// 四态之间是互斥单选，不是四个独立开关 —— 组合式的开关能拼出「既剥又加」这种非法态，
/// 而这里的 ③④ 都只是「加一层」，②只是「剥一层」，同一时刻只可能有一个生效。
/// </para>
/// <para>
/// 数值是持久化契约（<c>AppSettings</c> 里按 int 存），**不要重排或改值**；
/// 新增态一律追加在末尾。
/// </para>
/// </summary>
public enum RootDirectoryMode
{
    /// <summary>原样：不做任何结构调整（默认，与引入本功能前逐字节一致）。</summary>
    Original = 0,

    /// <summary>
    /// 智能去除唯一根：当输入**确实有**唯一根时剥掉那一层，否则保持原样。
    /// 压缩端条件 = 恰好一个目录源；解压端条件 = <see cref="ArchiveStructureAnalyzer.HasSingleRootDirectory"/>。
    /// </summary>
    SmartStrip = 1,

    /// <summary>
    /// 智能添加唯一根：当输入**没有**唯一根时加一层 <c>&lt;根名&gt;/</c>，否则保持原样。
    /// 与 <see cref="AlwaysWrap"/> 的唯一区别就是「已经是唯一根时不动」。
    /// </summary>
    SmartWrap = 2,

    /// <summary>
    /// 始终添加唯一根：无条件加一层 <c>&lt;根名&gt;/</c>（原本已是唯一根时会得到两层）。
    /// </summary>
    AlwaysWrap = 3,
}

/// <summary>
/// 一次结构调整的判定结果：剥哪个根、加哪层根。两者不可能同时为 true
/// （见 <see cref="ArchiveRootPolicy.Resolve"/>）。
/// </summary>
/// <param name="Strip">是否剥掉唯一根那一层。</param>
/// <param name="Wrap">是否在最前面加一层根目录。</param>
public readonly record struct RootAdjustment(bool Strip, bool Wrap)
{
    /// <summary>不做任何调整。</summary>
    public static RootAdjustment None => new(false, false);

    /// <summary>是否需要改写条目名。</summary>
    public bool IsActive => Strip || Wrap;
}

/// <summary>
/// 「包内根目录」四态的统一决策与条目名改写。
/// <para>
/// <b>为什么收在一处</b>：压缩侧要同时服务 Zip / TarGz（<see cref="FileScanner"/>）、7z
/// （<c>CompressFileDictionary</c>）与预览树，解压侧要同时服务三个引擎的全量路径与
/// <see cref="ExtractPathResolver"/>；每侧各写一份判定，必然漂移成「预览 ≠ 实际」。
/// </para>
/// </summary>
public static class ArchiveRootPolicy
{
    /// <summary>
    /// 按四态与实际结构解析出这次要做的调整。
    /// <para>
    /// ②③ 各自只在条件成立时动手、否则返回 <see cref="RootAdjustment.None"/>；
    /// ②与③的条件互斥（「是唯一根」与「不是唯一根」），故 <c>Strip</c> 与 <c>Wrap</c>
    /// 永不同时为 true。
    /// </para>
    /// </summary>
    /// <param name="mode">用户选择的四态之一。</param>
    /// <param name="hasSingleRoot">输入当前是否已有唯一根（压缩端 = 恰一个目录源；解压端 = 包结构判定）。</param>
    public static RootAdjustment Resolve(RootDirectoryMode mode, bool hasSingleRoot) => mode switch
    {
        // 智能剥：本来就没有唯一根时什么都不做（否则会把顶层文件/多根中的一层误剥）
        RootDirectoryMode.SmartStrip => hasSingleRoot ? new RootAdjustment(true, false) : RootAdjustment.None,
        // 智能加：本来就有唯一根时保持原样 —— 这正是它和「始终加」的唯一区别
        RootDirectoryMode.SmartWrap => hasSingleRoot ? RootAdjustment.None : new RootAdjustment(false, true),
        // 始终加：无条件，哪怕已经是唯一根（结果会是两层）
        RootDirectoryMode.AlwaysWrap => new RootAdjustment(false, true),
        _ => RootAdjustment.None,
    };

    /// <summary>
    /// 智能解压只支持两档：<see cref="RootDirectoryMode.SmartWrap"/>（智能加根，默认）与
    /// <see cref="RootDirectoryMode.SmartStrip"/>（智能去根）。
    /// <para>
    /// 其余任何值（含 <see cref="RootDirectoryMode.Original"/>、<see cref="RootDirectoryMode.AlwaysWrap"/>、
    /// 以及从 JSON 手改进来的越界值）一律回落 <see cref="RootDirectoryMode.SmartWrap"/>。
    /// <b>为什么不回落 Original</b>：Original 会让「散装包建包名目录」这个动作彻底消失，
    /// 用户只是手滑改了一个数字就静默退化成平铺，比报错更难排查；而 SmartWrap 是默认档，
    /// 退化成它至少与「没配置过」的用户一致。
    /// </para>
    /// </summary>
    public static RootDirectoryMode NormalizeSmartExtractMode(RootDirectoryMode mode)
        => mode == RootDirectoryMode.SmartStrip ? RootDirectoryMode.SmartStrip : RootDirectoryMode.SmartWrap;

    /// <summary>
    /// 压缩端「是否有唯一根」的判定：<paramref name="sourcePaths"/> 恰好是一个目录。
    /// <para>
    /// 为什么只认「恰一个目录」：多目录源必然在包顶层产出多个条目、文件+目录混合与纯文件源
    /// 一定有条目直接落在顶层 —— 这些情况「剥掉唯一的一层」都无从谈起；强行剥掉其中一个目录名
    /// 还会引发同名字项碰撞。故这种情况在 UI 上直接置灰。
    /// </para>
    /// </summary>
    public static bool HasSingleSourceDirectory(IReadOnlyList<string>? sourcePaths)
    {
        if (sourcePaths is not { Count: 1 })
            return false;
        var only = sourcePaths[0];
        return !string.IsNullOrWhiteSpace(only) && System.IO.Directory.Exists(only);
    }

    /// <summary>
    /// 压缩端剥根时要剥掉的那一层目录名（<c>D:\data\7zip</c> → <c>7zip</c>）。
    /// 调用方须先确认 <see cref="HasSingleSourceDirectory"/> 为 true。
    /// </summary>
    public static string GetSourceStripName(string sourcePath)
        => ArchivePath.GetFileName(ArchivePath.TrimEndSeparator(sourcePath));

    /// <summary>
    /// 加根时使用的根名。优先用调用方给的名字（压缩端输入框、解压端压缩包名），
    /// 为空时回落到 <paramref name="fallbackArchivePath"/> 的文件名去扩展名，再不行用 <c>"archive"</c>。
    /// <para>
    /// 结果会被 <see cref="SanitizeDirectorySegment"/> 清掉非法字符与首尾空白 —— 压缩包名
    /// 里出现 <c>:</c> 或 <c>/</c> 时不该让整个压缩失败。
    /// </para>
    /// </summary>
    public static string GetWrapName(string? preferredName, string? fallbackArchivePath = null)
    {
        var name = preferredName?.Trim();
        if (string.IsNullOrEmpty(name) && !string.IsNullOrWhiteSpace(fallbackArchivePath))
            name = ArchivePath.GetFileNameWithoutExtension(fallbackArchivePath);
        name = SanitizeDirectorySegment(name);
        return string.IsNullOrEmpty(name) ? "archive" : name;
    }

    /// <summary>
    /// 把一个相对路径按调整结果改写。
    /// <para>
    /// <paramref name="stripRoot"/> 非空且该路径位于它之下时剥掉该前缀（含该前缀本身的那个目录条目
    /// 会被整体丢弃，返回 null）；<paramref name="wrapRoot"/> 非空时在最前面加一层。
    /// </para>
    /// </summary>
    /// <param name="relativePath">原始相对路径（<c>/</c> 或 <c>\</c> 分隔均可）。</param>
    /// <param name="stripRoot">要剥掉的根段（不带分隔符）；null/空 = 不剥。</param>
    /// <param name="wrapRoot">要加的根段（不带分隔符）；null/空 = 不加。</param>
    /// <returns>改写后的相对路径（始终用 <c>/</c> 分隔）；该条目应被丢弃时返回 null。</returns>
    public static string? Apply(string? relativePath, string? stripRoot, string? wrapRoot)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        // 两个根段都没给时原样返回，**不做归一化**：调用方（FileScanner）在「原样」模式下
        // 必须逐字节产出与引入本功能前一致的分隔符（Windows 上是 '\'），否则既有的
        // 播种 Key 与进度上报会错位。
        if (string.IsNullOrEmpty(stripRoot) && string.IsNullOrEmpty(wrapRoot))
            return relativePath;

        var path = ArchivePath.Normalize(relativePath);
        if (path.Length == 0)
            return null;

        if (!string.IsNullOrEmpty(stripRoot))
        {
            var root = ArchivePath.Normalize(stripRoot).Trim('/');
            if (root.Length > 0)
            {
                // 目录条目本身（"7zip/"）剥完什么都不剩 → 丢弃（否则会在目标目录建出空壳）
                if (string.Equals(path.Trim('/'), root, StringComparison.OrdinalIgnoreCase))
                    return null;

                var prefix = root + "/";
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    path = path[prefix.Length..];
            }
        }

        if (!string.IsNullOrEmpty(wrapRoot))
        {
            var wrap = ArchivePath.Normalize(wrapRoot).Trim('/');
            if (wrap.Length > 0)
                path = wrap + "/" + path;
        }

        return path.Length == 0 ? null : path;
    }

    /// <summary>
    /// 与 <see cref="Apply"/> 相同，但按 <see cref="RootAdjustment"/> 的两个开关分别决定是否剥/加
    /// （两个根名参数都可能被传进来，真正生效的只有 adjustment 允许的那一半）。
    /// </summary>
    public static string? Apply(string? relativePath, RootAdjustment adjustment, string? stripRoot, string? wrapRoot)
        => Apply(relativePath, adjustment.Strip ? stripRoot : null, adjustment.Wrap ? wrapRoot : null);

    /// <summary>
    /// 把任意字符串清成可用的单段目录名：去掉路径分隔符与 Windows 非法字符、压缩首尾空白与点。
    /// 空结果返回空串（由调用方回落默认名）。
    /// </summary>
    public static string SanitizeDirectorySegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = value.Trim().ToCharArray();
        var invalid = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i]) || Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }

        var result = new string(chars).Trim().TrimEnd('.');
        return result;
    }
}
