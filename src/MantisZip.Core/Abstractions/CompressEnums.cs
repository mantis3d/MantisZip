namespace MantisZip.Core.Abstractions;

/// <summary>
/// 压缩冲突处理方式
/// </summary>
public enum CompressConflictAction
{
    Overwrite,
    Add,
    Rename,
    Cancel
}

/// <summary>
/// 压缩输出模式
/// </summary>
public enum CompressOutputMode
{
    Manual,
    Separate,
    Combined
}
