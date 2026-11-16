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
    /// <summary>单个压缩包：所有源合并进一个包，输出路径可手动指定。</summary>
    Manual,

    /// <summary>逐项压缩：每个源文件/文件夹各自生成一个压缩包，落在各自源所在目录。</summary>
    Separate
}
