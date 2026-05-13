using System.IO;
using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 解压冲突处理工具
/// </summary>
internal static class FileConflictHelper
{
    /// <summary>
    /// 根据冲突策略返回实际写入路径。可能返回 null（表示跳过）。
    /// 遇 Ask 时调用 options.ConflictResolver 回调决定处理方式。
    /// </summary>
    public static string? ResolvePath(string outputPath, ArchiveOptions? options)
    {
        if (!File.Exists(outputPath))
            return outputPath;

        var action = options?.ConflictAction ?? FileConflictAction.Overwrite;

        // Ask → 调用外部回调
        if (action == FileConflictAction.Ask && options?.ConflictResolver != null)
        {
            action = options.ConflictResolver(outputPath);
        }

        return action switch
        {
            FileConflictAction.Overwrite => outputPath,
            FileConflictAction.Skip => null,
            FileConflictAction.Rename => GetUniquePath(outputPath),
            _ => outputPath
        };
    }

    /// <summary>
    /// 根据冲突策略返回实际写入路径。可能返回 null（表示跳过）。
    /// </summary>
    public static string? ResolvePath(string outputPath, FileConflictAction action)
    {
        if (!File.Exists(outputPath))
            return outputPath;

        return action switch
        {
            FileConflictAction.Overwrite => outputPath,
            FileConflictAction.Skip => null,
            FileConflictAction.Rename => GetUniquePath(outputPath),
            _ => outputPath
        };
    }

    private static string GetUniquePath(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (int i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
        return path; // 1000 个同名文件后直接覆盖
    }
}
