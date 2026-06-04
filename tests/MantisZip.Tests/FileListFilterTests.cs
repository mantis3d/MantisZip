using MantisZip.Core.Abstractions;
using Xunit;

namespace MantisZip.Tests;

/// <summary>
/// 文件列表筛选/搜索功能的测试基础设施和单元测试。
/// 不实例化任何 WPF 控件，仅测试数据层逻辑。
/// </summary>
public class FileListFilterTests
{
    /// <summary>
    /// 创建模拟的压缩包条目数据集，包含多层目录结构，
    /// 可用于 showSubfolders 和过滤引擎测试。
    /// </summary>
    public static List<ArchiveItem> CreateTestItems()
    {
        var baseDate = new DateTime(2026, 3, 15, 10, 0, 0);
        return new List<ArchiveItem>
        {
            // 根目录文件
            new() { Name = "readme.txt",       FullPath = "readme.txt",        Size = 100,    LastModified = baseDate,                     IsDirectory = false },
            new() { Name = "notes.md",         FullPath = "notes.md",          Size = 200,    LastModified = baseDate.AddDays(1),          IsDirectory = false },
            new() { Name = "archive.zip",      FullPath = "archive.zip",       Size = 50000,  LastModified = baseDate.AddDays(-5),         IsDirectory = false },

            // 子目录 src/ 的文件
            new() { Name = "main.cs",          FullPath = "src/main.cs",               Size = 1500,   LastModified = baseDate.AddDays(2),   IsDirectory = false },
            new() { Name = "util.cs",          FullPath = "src/util.cs",               Size = 800,    LastModified = baseDate.AddMonths(1), IsDirectory = false },

            // 深层子目录 src/utils/ 的文件
            new() { Name = "helper.cs",        FullPath = "src/utils/helper.cs",       Size = 3000,   LastModified = baseDate.AddDays(10),  IsDirectory = false },
            new() { Name = "converter.cs",     FullPath = "src/utils/converter.cs",    Size = 12000,  LastModified = baseDate.AddDays(-1),  IsDirectory = false },

            // 子目录 docs/ 的文件
            new() { Name = "index.html",       FullPath = "docs/index.html",           Size = 2500,   LastModified = baseDate.AddDays(3),   IsDirectory = false },
            new() { Name = "guide.pdf",        FullPath = "docs/guide.pdf",            Size = 150000, LastModified = baseDate.AddDays(20),  IsDirectory = false },

            // 子目录 images/ 的文件
            new() { Name = "logo.png",         FullPath = "images/logo.png",           Size = 45000,  LastModified = baseDate.AddDays(7),   IsDirectory = false },
            new() { Name = "banner.svg",       FullPath = "images/banner.svg",         Size = 12000,  LastModified = baseDate.AddDays(30),  IsDirectory = false },

            // 空子目录（只有目录条目，无文件）
            new() { Name = "empty/",           FullPath = "empty",                     Size = 0,      LastModified = baseDate,             IsDirectory = true },
        };
    }
}
