namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 骨架构建过程中的进度快照（**累计值**，不是单次增量）。
/// 由 <c>ResultPreviewService.BuildSourceSubtree</c> 在枚举每个文件后上报，供压缩预览的
/// 「加载中 N 个文件，X」实时文案轮询使用（每 0.5s 取最新值，不逐条刷 UI）。
/// </summary>
/// <remarks>
/// 放在 Models 而不是嵌套在 <c>ResultPreviewService</c> 里：它同时被
/// <see cref="PreviewTreeNode"/>（本命名空间）与 <c>CompressSettingsViewModel</c>（引用 Models）使用，
/// 嵌套会让调用方被迫写 <c>ResultPreviewService.SkeletonProgress</c>。
/// </remarks>
/// <param name="FileCount">该源已枚举（计入树）的文件总数。</param>
/// <param name="TotalSize">该源已枚举文件的大小之和（字节）。</param>
public sealed record SkeletonProgress(int FileCount, long TotalSize);
