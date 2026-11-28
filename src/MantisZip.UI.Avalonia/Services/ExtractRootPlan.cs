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

        // 「能不能剥」与「剥哪一段」是同一个判定，交给 HasStrippableRoot 一次答完：
        // 空包与「只有目录条目」的包在这里就已经是 false，不会出现「窗里能选、跑起来什么都不做」。
        var hasStrippableRoot = ArchiveStructureAnalyzer.HasStrippableRoot(entries);
        var adjustment = ArchiveRootPolicy.Resolve(mode, hasStrippableRoot);

        // 剥哪一段：只有「确有唯一根」时才有可剥的名字；加哪一段：默认用包名（去扩展名）。
        var stripRoot = adjustment.Strip
            ? ArchiveStructureAnalyzer.GetSingleRootDirectory(entries!)
            : null;
        var addRoot = adjustment.Wrap
            ? ArchiveRootPolicy.GetWrapName(preferredName: null, fallbackArchivePath: archivePath)
            : null;

        // 兜底（理论上不可达：HasStrippableRoot 已保证有名字）。宁可不动，也不要「剥了但没有名字」的半生效状态。
        if (adjustment.Strip && stripRoot == null)
            return (null, null);

        return (stripRoot, addRoot);
    }

    /// <summary>
    /// 与 <see cref="Resolve"/> 相同，但在没有现成条目时自己去列一次。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 三条执行链（整包 / 过滤 / 选中条目）都要判「包有没有唯一根」，各写一次就等于三个判定点
    /// 迟早不一致；统一从这里进。原样模式直接短路，不列条目、零额外开销。
    /// </para>
    /// <para>
    /// <paramref name="knownEntries"/> 非空时直接用这批条目判定，<b>不再列包</b>：确认事务已经把
    /// 「用户当时看到的条目」冻结进载荷（<see cref="Models.ExtractSettings.KnownEntries"/>），
    /// 执行段再列一次既慢又可能因编码/密码差异列出一份不同的名字，反倒让「预览 = 实际」漂移。
    /// 对 TAR/GZ 尤其关键 —— 它们的 <c>ListEntriesAsync</c> 是全流扫描，成本≈解压一次。
    /// </para>
    /// </remarks>
    public static async Task<(string? StripRoot, string? AddRoot)> ResolveAsync(
        IArchiveEngine? engine,
        string archivePath,
        string? password,
        CancellationToken cancellationToken,
        string? nameEncoding,
        RootDirectoryMode mode,
        IReadOnlyList<ArchiveItem>? knownEntries = null)
    {
        if (mode == RootDirectoryMode.Original)
            return (null, null);

        if (knownEntries is { Count: > 0 })
            return Resolve(mode, knownEntries, archivePath);

        if (engine == null)
            return (null, null);

        var entries = await engine.ListEntriesAsync(archivePath, password, cancellationToken, nameEncoding);
        return Resolve(mode, entries, archivePath);
    }
}
