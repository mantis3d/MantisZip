using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 把解压端「包内根目录」四态解析成引擎真正要执行的两个段：要剥掉的根、要加上的根。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么单开一层</b>：四态的语义是「看包才知道该不该动」（智能去根要求包确有唯一根，
/// 智能加根要求包没有唯一根），而引擎只该收到「剥 X、加 Y」这种确定指令 —— 判定留在 UI/Core 的
/// 决策层，引擎保持无状态。<see cref="ArchiveRootPolicy.Resolve"/> 是纯函数，本类只负责
/// 把「包结构」喂给它。
/// </para>
/// <para>
/// <b>预览与执行必须共用本类</b>：预览树和实解各算一遍很容易漂移（一处读了设置、另一处忘了），
/// 结果就是「预览少一层、解压多一层」。预览侧传的是已经列好的条目，执行侧传同一份条目即可。
/// </para>
/// </remarks>
public static class ExtractRootPlan
{
    /// <summary>
    /// 解析本次解压要剥掉 / 要加上的根段。
    /// </summary>
    /// <param name="mode">用户在设置窗选的「包内根目录」四态。</param>
    /// <param name="entries">该压缩包的条目列表（用于判定是否确有唯一根）。</param>
    /// <param name="archivePath">压缩包路径（加根时默认取它的主名作为根名）。</param>
    /// <returns>
    /// <c>StripRoot</c> 非空 = 剥掉这一层；<c>AddRoot</c> 非空 = 最前面加这一层。
    /// 原样模式下两者都是 <see langword="null"/>（与引入本功能前逐字节一致）。
    /// </returns>
    public static (string? StripRoot, string? AddRoot) Resolve(
        RootDirectoryMode mode,
        IReadOnlyList<ArchiveItem>? entries,
        string? archivePath)
    {
        if (mode == RootDirectoryMode.Original)
            return (null, null);

        var hasSingleRoot = entries is { Count: > 0 } && ArchiveStructureAnalyzer.HasSingleRootDirectory(entries);
        var adjustment = ArchiveRootPolicy.Resolve(mode, hasSingleRoot);

        // 剥哪一段：只有「确有唯一根」时才有可剥的名字；加哪一段：默认用包名（去扩展名）。
        var stripRoot = adjustment.Strip
            ? ArchiveStructureAnalyzer.GetSingleRootDirectory(entries!)
            : null;
        var addRoot = adjustment.Wrap
            ? ArchiveRootPolicy.GetWrapName(preferredName: null, fallbackArchivePath: archivePath)
            : null;

        // 剥根段拿不到（例如纯目录包）时宁可不动，也不要"剥了但没有名字"这种半生效状态。
        if (adjustment.Strip && stripRoot == null)
            return (null, null);

        return (stripRoot, addRoot);
    }

    /// <summary>
    /// 与 <see cref="Resolve"/> 相同，但自己去列一次条目。
    /// </summary>
    /// <remarks>
    /// 三条执行链（整包 / 过滤 / 选中条目）都要判「包有没有唯一根」，各写一次就等于三个判定点
    /// 迟早不一致；统一从这里进。原样模式直接短路，不列条目、零额外开销。
    /// </remarks>
    public static async Task<(string? StripRoot, string? AddRoot)> ResolveAsync(
        IArchiveEngine? engine,
        string archivePath,
        string? password,
        CancellationToken cancellationToken,
        string? nameEncoding,
        RootDirectoryMode mode)
    {
        if (mode == RootDirectoryMode.Original || engine == null)
            return (null, null);

        var entries = await engine.ListEntriesAsync(archivePath, password, cancellationToken, nameEncoding);
        return Resolve(mode, entries, archivePath);
    }
}
