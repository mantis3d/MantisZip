namespace MantisZip.Core.Abstractions;

/// <summary>
/// 过滤后的压缩计划项（B 数据集中的一项）：一个源 → 输出压缩包路径 + 包含文件清单。
/// 预览构建时生成，执行侧只读消费，保证预览 = 实际。
/// </summary>
/// <param name="SourcePath">源（目录或文件）绝对路径。</param>
/// <param name="OutputArchivePath">输出压缩包绝对路径（Separate 每源一个；Manual/Combined 共享同一路径）。</param>
/// <param name="IncludedFiles">匹配过滤条件的文件绝对路径清单；null = 全部（未启用过滤）。</param>
public sealed record CompressPlanItem(
    string SourcePath,
    string OutputArchivePath,
    IReadOnlyList<string>? IncludedFiles);

/// <summary>
/// 过滤后的压缩计划（B 数据集）。由预览构建（BuildCompressPreview）派生，
/// 压缩执行侧（CompressService/CompressFlow）只读消费，不重新计算路径、不重新过滤。
/// </summary>
/// <param name="Mode">输出模式。</param>
/// <param name="OutputPath">Manual/Combined 模式的总输出路径；Separate 为 null。</param>
/// <param name="Items">每源一项的计划。</param>
public sealed record CompressPlan(
    CompressOutputMode Mode,
    string? OutputPath,
    IReadOnlyList<CompressPlanItem> Items);
