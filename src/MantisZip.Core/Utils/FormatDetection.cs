namespace MantisZip.Core.Utils;

/// <summary>
/// 压缩包**容器**类型（粗分类）。回答的是「这是什么格式的压缩包」，不涉及 zip 内部子类型
/// （docx / odt / jar / apk 都是容器 <see cref="Zip"/>）。
/// </summary>
/// <remarks>
/// 与 <see cref="FileFormat"/> 的分工（2026-10-27 引入，修 issue #54 / #36）：
/// <list type="bullet">
/// <item>要选**引擎**、判断「能不能当压缩包打开」→ 只看容器。子类型在这里一律不参与，
/// 否则「识别得越细越打不开」（jar 被细判成 Odt 就没有引擎了）。</item>
/// <item>要选**预览器**、判断「内容是不是已经压缩过」→ 才需要子类型，走 <see cref="FileFormatDetector.Detect"/>。</item>
/// </list>
/// </remarks>
public enum ContainerFormat
{
    /// <summary>不是已知压缩包容器。</summary>
    Unknown,

    /// <summary>ZIP 家族（zip / docx / xlsx / pptx / epub / odt / ods / odp / jar / war / ear / apk …）。</summary>
    Zip,

    /// <summary>7-Zip。</summary>
    SevenZip,

    /// <summary>RAR（1.5 ~ 5.x）。</summary>
    Rar,

    /// <summary>TAR（现状只按扩展名识别，没有魔数分支）。</summary>
    Tar,

    /// <summary>GZip（含 .tgz / .tar.gz）。</summary>
    GZip,

    /// <summary>ISO 光盘映像（现状只按扩展名识别，没有魔数分支）。</summary>
    Iso,
}

/// <summary>
/// 一次格式识别的完整结论：容器（粗分类）+ 子类型（细分）。
/// </summary>
/// <param name="Container">容器类型，用于挑选引擎。</param>
/// <param name="Subtype">子类型，用于挑选预览器 / 判断内容是否已压缩。</param>
public readonly record struct FormatDetection(ContainerFormat Container, FileFormat Subtype)
{
    /// <summary>什么也没识别出来。</summary>
    public static readonly FormatDetection Unknown = new(ContainerFormat.Unknown, FileFormat.Unknown);

    /// <summary>是否识别出了可交给引擎的容器。</summary>
    public bool IsContainer => Container != ContainerFormat.Unknown;

    /// <summary>转成「容器」字符串，仅用于日志。</summary>
    public override string ToString() => $"{Container}/{Subtype}";
}
