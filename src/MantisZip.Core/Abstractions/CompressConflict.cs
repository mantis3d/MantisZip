namespace MantisZip.Core.Abstractions;

/// <summary>
/// 压缩冲突信息，传递给回调供调用方决定处理方式
/// </summary>
/// <param name="OutputPath">目标文件路径</param>
/// <param name="CanAdd">是否支持"添加到已有压缩包"</param>
/// <param name="SuggestedName">预计算的重命名建议名（不含路径），用于对话框预填</param>
public sealed record CompressConflictInfo(
    string OutputPath,
    bool CanAdd,
    string? SuggestedName);

/// <summary>
/// 压缩冲突处理结果，由回调返回
/// </summary>
/// <param name="Action">处理方式</param>
/// <param name="CustomName">用户自定义文件名（Rename 时使用）</param>
public sealed record CompressConflictResolution(
    CompressConflictAction Action,
    string? CustomName);

/// <summary>
/// 压缩冲突处理回调委托
/// </summary>
public delegate CompressConflictResolution CompressConflictResolver(CompressConflictInfo info);
