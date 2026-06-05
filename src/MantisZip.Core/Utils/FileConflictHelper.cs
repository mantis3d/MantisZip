using System.Collections.Generic;
using System.IO;
using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 解压冲突处理工具
/// </summary>
public static class FileConflictHelper
{
    /// <summary>
    /// 根据冲突策略返回实际写入路径。可能返回 null（表示跳过）。
    /// 遇 Ask 时调用 options.ConflictResolver 回调决定处理方式。
    /// 回调通过 <see cref="FileConflictInfo.CustomName"/> 支持用户自定义文件名。
    /// </summary>
    public static string? ResolvePath(string outputPath, ArchiveOptions? options, DateTime? entryModified = null, long? entrySize = null)
    {
        if (!File.Exists(outputPath))
            return outputPath;

        var action = options?.ConflictAction ?? FileConflictAction.Overwrite;

        // Ask → 调用外部回调
        if (action == FileConflictAction.Ask && options?.ConflictResolver != null)
        {
            var info = new FileConflictInfo
            {
                FilePath = outputPath,
                EntrySize = entrySize,
                EntryModified = entryModified,
            };
            try
            {
                var fi = new FileInfo(outputPath);
                if (fi.Exists)
                {
                    info.ExistingSize = fi.Length;
                    info.ExistingModified = fi.LastWriteTime;
                }
            }
            catch (Exception fiEx) { CoreLog.Info($"FileConflictHelper: failed to get file info for {outputPath}: {fiEx.Message}"); }

            // 预计算自动重命名的建议名，供对话框预填
            info.SuggestedName = Path.GetFileName(GetUniquePath(outputPath));

            action = options.ConflictResolver(info);

            // 回调选择了 Rename 且用户填写了自定义名 → 直接使用
            if (action == FileConflictAction.Rename && !string.IsNullOrWhiteSpace(info.CustomName))
            {
                var dir = Path.GetDirectoryName(outputPath) ?? ".";
                return Path.Combine(dir, SanitizeFileName(info.CustomName));
            }
        }

        return ResolveByAction(outputPath, action, entryModified, entrySize);
    }

    /// <summary>
    /// 根据冲突策略返回实际写入路径。可能返回 null（表示跳过）。
    /// </summary>
    public static string? ResolvePath(string outputPath, FileConflictAction action, DateTime? entryModified = null, long? entrySize = null)
    {
        if (!File.Exists(outputPath))
            return outputPath;

        return ResolveByAction(outputPath, action, entryModified, entrySize);
    }

    private static string? ResolveByAction(string outputPath, FileConflictAction action, DateTime? entryModified, long? entrySize)
    {
        var resolved = action switch
        {
            FileConflictAction.Overwrite => outputPath,
            FileConflictAction.Skip => null,
            FileConflictAction.Rename => GetUniquePath(outputPath),
            FileConflictAction.OverwriteIfOlder => ShouldOverwriteByTime(outputPath, entryModified) ? outputPath : null,
            FileConflictAction.OverwriteIfSmaller => ShouldOverwriteBySize(outputPath, entrySize) ? outputPath : null,
            _ => outputPath
        };
        CoreLog.Info($"FileConflictHelper.ResolveByAction: path='{outputPath}', action={action} -> {(resolved ?? "(skip)")}");
        return resolved;
    }

    private static bool ShouldOverwriteBySize(string outputPath, long? entrySize)
    {
        if (entrySize == null) return true;
        try
        {
            var existingSize = new FileInfo(outputPath).Length;
            // "覆盖较小"：压缩包内的文件更大 → 覆盖掉磁盘上较小的文件
            var result = entrySize.Value > existingSize;
            CoreLog.Info($"FileConflictHelper.OverwriteIfSmaller: entry={entrySize}, existing={existingSize} -> {(result ? "overwrite" : "skip")}");
            return result;
        }
        catch (Exception ex)
        {
            CoreLog.Info($"FileConflictHelper.OverwriteIfSmaller: failed to get file size for '{outputPath}': {ex.Message}");
            return true;
        }
    }

    private static bool ShouldOverwriteByTime(string outputPath, DateTime? entryModified)
    {
        if (entryModified == null)
        {
            CoreLog.Info($"FileConflictHelper.OverwriteIfOlder: no entry modified time -> overwrite");
            return true;
        }
        try
        {
            var existingTime = File.GetLastWriteTime(outputPath);
            var result = entryModified.Value > existingTime;
            CoreLog.Info($"FileConflictHelper.OverwriteIfOlder: entry={entryModified:yyyy-MM-dd HH:mm:ss}, existing={existingTime:yyyy-MM-dd HH:mm:ss} -> {(result ? "overwrite" : "skip")}");
            return result;
        }
        catch (Exception ex)
        {
            CoreLog.Info($"FileConflictHelper.OverwriteIfOlder: failed to get file time for '{outputPath}': {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// 防止路径遍历攻击 (Zip Slip) — 确保解压路径在目标目录内
    /// 使用 Path.GetRelativePath 进行可靠的包容性检查，替代不安全的 StartsWith。
    /// </summary>
    public static string GetSafePath(string destinationDir, string entryName)
    {
        var fullPath = Path.GetFullPath(Path.Combine(destinationDir, entryName));
        var destFullPath = Path.GetFullPath(destinationDir);

        // 使用 GetRelativePath 检查：如果相对路径以 ".." 开头或以根路径形式出现，
        // 说明 fullPath 逃逸出了目标目录
        var relative = Path.GetRelativePath(destFullPath, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            throw new InvalidOperationException($"压缩条目包含非法路径: {entryName}");

        return fullPath;
    }

    /// <summary>
    /// 净化压缩包条目路径：移除 "../"、"..\"、"." 等路径穿越组件，
    /// 以及包含非法文件名字符的组件。用于从不可信的压缩包条目名构建安全输出路径。
    /// 示例: "folder/../../evil.txt" → "evil.txt"
    /// </summary>
    public static string SanitizeEntryPath(string entryPath)
    {
        // 统一分隔符
        var segments = entryPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var safe = new List<string>();
        foreach (var seg in segments)
        {
            if (seg is ".." or ".")
                continue; // 丢弃路径穿越组件
            if (seg.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                continue; // 丢弃包含非法字符的组件
            safe.Add(seg);
        }

        if (safe.Count == 0)
            throw new InvalidOperationException($"条目路径净化后为空: {entryPath}");

        return string.Join("/", safe);
    }

    private static string GetUniquePath(string path)
    {
        return PathHelper.GetUniquePath(path);
    }

    /// <summary>
    /// 净化用户输入的文件名：防止路径穿越，剔除非法字符，空名回退。
    /// </summary>
    internal static string SanitizeFileName(string fileName)
    {
        // 防止路径穿越（丢弃目录部分，只保留文件名）
        fileName = Path.GetFileName(fileName);

        // 剔除 Windows 文件名非法字符
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
            fileName = fileName.Replace(c.ToString(), "");

        return string.IsNullOrWhiteSpace(fileName) ? "renamed" : fileName.Trim();
    }
}
