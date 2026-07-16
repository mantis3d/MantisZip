using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MantisZip.Core.FileFilter;

namespace MantisZip.UI;

/// <summary>
/// UI 层文件过滤辅助类。
/// 处理目录递归枚举和路径级别的过滤（压缩场景）。
/// </summary>
public static class FileFilterHelper
{
    /// <summary>
    /// 对路径数组应用过滤条件。
    /// - 文件路径：直接匹配
    /// - 目录路径：递归枚举目录内所有文件并逐一匹配
    /// 匹配的文件保留相对于目录的结构。
    /// filter 为 null 或未激活时返回原数组。
    /// </summary>
    public static string[] ApplyFilter(string[] paths, FileFilterCriteria? filter)
    {
        if (filter == null || !filter.IsActive)
            return paths;

        var result = new List<string>();

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                if (FileFilterMatcher.IsMatch(filter, path))
                    result.Add(path);
            }
            else if (Directory.Exists(path))
            {
                var dirFiles = EnumerateFilesRecursive(path);
                var matched = dirFiles
                    .Where(f => FileFilterMatcher.IsMatch(filter, f))
                    .Select(f => f) // full path already
                    .ToArray();
                // If no files matched, still add empty dir to preserve structure?
                // No - compression engine skips empty dirs anyway.
                result.AddRange(matched);
            }
            // else: path doesn't exist, skip silently
        }

        return result.ToArray();
    }

    /// <summary>
    /// 递归枚举目录内所有文件。
    /// </summary>
    private static IEnumerable<string> EnumerateFilesRecursive(string directory)
    {
        var results = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                results.Add(file);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip inaccessible directories
        }
        catch (DirectoryNotFoundException)
        {
            // Skip if directory was deleted during enumeration
        }
        return results;
    }
}
