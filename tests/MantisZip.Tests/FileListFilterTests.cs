using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
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

    // ===== Task 7: ShowSubfolders 模式测试 =====

    public static IEnumerable<object[]> GetItemsForSubfolderTest()
    {
        var items = CreateTestItems();
        yield return new object[] { items };
    }

    /// <summary>
    /// 辅助方法：模拟 FilterFiles 的 showSubfolders=true 模式。
    /// 从 _allItems 中收集当前 prefix 下的所有非目录条目。
    /// </summary>
    private static List<ArchiveItem> FilterShowSubfolders(IReadOnlyList<ArchiveItem> allItems, string folderPath)
    {
        string prefix = string.IsNullOrEmpty(folderPath) ? "" : folderPath + "/";
        return allItems
            .Where(i => !i.IsDirectory)
            .Where(i => string.IsNullOrEmpty(folderPath) || i.FullPath.StartsWith(prefix))
            .ToList();
    }

    [Fact]
    public void FilterFiles_Root_ShowSubfolders_ReturnsAllFiles()
    {
        var items = CreateTestItems();
        var result = FilterShowSubfolders(items, "");

        // 根目录扁平模式返回所有非目录条目
        Assert.DoesNotContain(result, i => i.IsDirectory);
        Assert.Contains(result, i => i.Name == "readme.txt");
        Assert.Contains(result, i => i.Name == "helper.cs");       // src/utils/helper.cs
        Assert.Contains(result, i => i.Name == "index.html");      // docs/index.html
        Assert.Equal(11, result.Count);  // 12 total items - 1 directory = 11 files
    }

    [Fact]
    public void FilterFiles_Subdir_ShowSubfolders_ReturnsNestedFiles()
    {
        var items = CreateTestItems();
        var result = FilterShowSubfolders(items, "src");

        // 子目录 src/ 扁平模式返回 src/main.cs 和 src/utils/helper.cs
        Assert.Contains(result, i => i.FullPath == "src/main.cs");
        Assert.Contains(result, i => i.FullPath == "src/utils/helper.cs");
        Assert.DoesNotContain(result, i => i.FullPath == "docs/index.html");
        Assert.Equal(4, result.Count);  // main.cs, util.cs, helper.cs, converter.cs
    }

    [Fact]
    public void FilterFiles_ShowSubfolders_ExcludesDirectories()
    {
        var items = CreateTestItems();
        var result = FilterShowSubfolders(items, "");

        Assert.DoesNotContain(result, i => i.IsDirectory);
        Assert.All(result, i => Assert.False(i.IsDirectory));
    }

    [Fact]
    public void FilterFiles_Root_DefaultMode_ContainsDirectories()
    {
        // 默认模式（非扁平）下，结果应包含隐式合成的目录条目
        var items = CreateTestItems();
        var implicitDirs = new HashSet<string>();
        var result = new List<ArchiveItem>();

        foreach (var item in items)
        {
            if (!item.FullPath.Contains("/")) { result.Add(item); continue; }
            var firstSlash = item.FullPath.IndexOf('/');
            var dirName = item.FullPath[..firstSlash];
            if (!implicitDirs.Add(dirName)) continue;
            if (item.IsDirectory && item.FullPath == dirName)
            { result.Add(item); continue; }
            result.Add(new ArchiveItem { Name = dirName + "/", FullPath = dirName, Size = 0, IsDirectory = true });
        }

        // 去重
        var seen = new HashSet<string>();
        var deduped = result.Where(i => seen.Add(i.FullPath)).ToList();

        Assert.Contains(deduped, i => i.IsDirectory);      // 包含目录
        Assert.Contains(deduped, i => i.FullPath == "src");  // 隐式目录
        Assert.Contains(deduped, i => i.FullPath == "docs");
        Assert.Contains(deduped, i => i.FullPath == "images");
        Assert.Contains(deduped, i => i.Name == "readme.txt"); // 根目录文件
    }

    // ===== Task 8: 多维度过滤引擎测试 =====

    [Fact]
    public void Filter_Text_ByName()
    {
        var items = CreateTestItems();
        var result = ArchiveFilter.ApplyFilters(items, new SearchFilters { Text = "helper" });

        Assert.Contains(result, i => i.FullPath == "src/utils/helper.cs");
        Assert.DoesNotContain(result, i => i.FullPath == "readme.txt");
    }

    [Fact]
    public void Filter_Text_CaseInsensitive()
    {
        var items = CreateTestItems();
        var result = ArchiveFilter.ApplyFilters(items, new SearchFilters { Text = "README" });

        Assert.Contains(result, i => i.Name == "readme.txt");
    }

    [Fact]
    public void Filter_Text_PartialMatch()
    {
        var items = CreateTestItems();
        var result = ArchiveFilter.ApplyFilters(items, new SearchFilters { Text = "main" });

        Assert.Contains(result, i => i.FullPath == "src/main.cs");
    }

    [Fact]
    public void Filter_Text_NoMatch_ReturnsEmpty()
    {
        var items = CreateTestItems();
        var result = ArchiveFilter.ApplyFilters(items, new SearchFilters { Text = "nonexistent" });

        Assert.Empty(result);
    }

    [Fact]
    public void Filter_Date_After()
    {
        var items = CreateTestItems();
        var cutoff = new DateTime(2026, 3, 20);
        var result = ArchiveFilter.ApplyFilters(items, new SearchFilters { DateFrom = cutoff });

        Assert.All(result, i => Assert.True(i.LastModified >= cutoff));
    }

    [Fact]
    public void Filter_Date_Before()
    {
        var items = CreateTestItems();
        var cutoff = new DateTime(2026, 3, 10);
        var result = ArchiveFilter.ApplyFilters(items, new SearchFilters { DateTo = cutoff });

        Assert.All(result, i => Assert.True(i.LastModified <= cutoff));
    }

    [Fact]
    public void Filter_Date_Range()
    {
        var items = CreateTestItems();
        var from = new DateTime(2026, 3, 10);
        var to = new DateTime(2026, 3, 20);
        var result = ArchiveFilter.ApplyFilters(items, new SearchFilters { DateFrom = from, DateTo = to });

        Assert.All(result, i => Assert.True(i.LastModified >= from && i.LastModified <= to));
    }

    [Fact]
    public void Filter_Date_NoMatch()
    {
        var items = CreateTestItems();
        var from = new DateTime(2025, 1, 1);
        var to = new DateTime(2025, 12, 31);
        var result = ArchiveFilter.ApplyFilters(items, new SearchFilters { DateFrom = from, DateTo = to });

        Assert.Empty(result);
    }

    [Fact]
    public void Filter_Size_Min()
    {
        var items = CreateTestItems();
        var result = ArchiveFilter.ApplyFilters(items, new SearchFilters { SizeMin = 10000 });

        Assert.All(result, i => Assert.True(i.Size >= 10000));
        Assert.DoesNotContain(result, i => i.Name == "readme.txt"); // 100 bytes
    }

    [Fact]
    public void Filter_Size_Max()
    {
        var items = CreateTestItems();
        var result = ArchiveFilter.ApplyFilters(items, new SearchFilters { SizeMax = 1000 });

        Assert.All(result, i => Assert.True(i.Size <= 1000));
    }

    [Fact]
    public void Filter_Size_Range()
    {
        var items = CreateTestItems();
        var result = ArchiveFilter.ApplyFilters(items, new SearchFilters { SizeMin = 1000, SizeMax = 5000 });

        Assert.Contains(result, i => i.FullPath == "src/utils/helper.cs");    // 3000
        Assert.Contains(result, i => i.FullPath == "src/main.cs");            // 1500
        Assert.DoesNotContain(result, i => i.FullPath == "src/utils/converter.cs"); // 12000
        Assert.DoesNotContain(result, i => i.FullPath == "readme.txt");       // 100
    }

    [Fact]
    public void Filter_Size_UnitConversion()
    {
        // 测试 ParseSizeWithUnit
        Assert.Equal(1024, ArchiveFilter.ParseSizeWithUnit("1", "KB"));
        Assert.Equal(1572864, ArchiveFilter.ParseSizeWithUnit("1.5", "MB"));  // 1.5 * 1024 * 1024
        Assert.Equal(2147483648, ArchiveFilter.ParseSizeWithUnit("2", "GB")); // 2 * 1024^3
        Assert.Equal(500, ArchiveFilter.ParseSizeWithUnit("500", "B"));
        Assert.Null(ArchiveFilter.ParseSizeWithUnit("", "KB"));
        Assert.Null(ArchiveFilter.ParseSizeWithUnit("abc", "MB"));
        Assert.Null(ArchiveFilter.ParseSizeWithUnit("-5", "KB"));
    }

    [Fact]
    public void Filter_Combined_TextAndDate()
    {
        var items = CreateTestItems();
        var from = new DateTime(2026, 3, 10);
        var result = ArchiveFilter.ApplyFilters(items, new SearchFilters { Text = "helper", DateFrom = from });

        // "helper" 只有一个文件 src/utils/helper.cs，它在 3月25日 (base+10) → 在区间内
        Assert.Single(result);
        Assert.Contains(result, i => i.FullPath == "src/utils/helper.cs");
    }

    [Fact]
    public void Filter_Combined_AllThree()
    {
        var items = CreateTestItems();
        var result = ArchiveFilter.ApplyFilters(items, new SearchFilters
        {
            Text = "converter",
            SizeMin = 1000,
            SizeMax = 50000,
        });

        // "converter" 匹配 converter.cs(12000)，大小 1000-50000 包含它
        Assert.Single(result);
        Assert.Contains(result, i => i.FullPath == "src/utils/converter.cs");
    }

    [Fact]
    public void Filter_Empty_ReturnsAll()
    {
        var items = CreateTestItems();
        var result = ArchiveFilter.ApplyFilters(items, new SearchFilters());

        Assert.Equal(items.Count, result.Count);
    }
}
