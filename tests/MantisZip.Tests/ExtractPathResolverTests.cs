using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests;

/// <summary>
/// ExtractPathResolver —— 统一路径计算的单元测试。
/// 预览树与实际解压共用此模块，测试锁定其裁剪 + 安全化语义，防止两处漂移。
/// </summary>
public class ExtractPathResolverTests
{
    private static ArchiveItem Item(string fullPath, bool isDir = false) => new()
    {
        FullPath = fullPath,
        Name = fullPath[(fullPath.LastIndexOf('/') + 1)..],
        IsDirectory = isDir
    };

    [Fact]
    public void TrimCurrentFolderPrefix_PreserveFullPathTrue_NoTrim()
    {
        Assert.Equal("docs/a.txt", ExtractPathResolver.TrimCurrentFolderPrefix("docs/a.txt", "docs", preserveFullPath: true));
    }

    [Fact]
    public void TrimCurrentFolderPrefix_EmptyCurrentFolder_NoTrim()
    {
        Assert.Equal("docs/a.txt", ExtractPathResolver.TrimCurrentFolderPrefix("docs/a.txt", "", preserveFullPath: false));
    }

    [Fact]
    public void TrimCurrentFolderPrefix_MatchesPrefix_Trims()
    {
        Assert.Equal("a.txt", ExtractPathResolver.TrimCurrentFolderPrefix("docs/a.txt", "docs", preserveFullPath: false));
    }

    [Fact]
    public void TrimCurrentFolderPrefix_DoesNotMatch_KeepsOriginal()
    {
        // 仅裁剪前缀匹配的条目；前缀不匹配时保持原路径（如开启"显示子文件夹内容"时分散在多处的文件）
        Assert.Equal("other/b.txt", ExtractPathResolver.TrimCurrentFolderPrefix("other/b.txt", "docs", preserveFullPath: false));
    }

    [Fact]
    public void TrimCurrentFolderPrefix_TrimRoot_OnlyAffectsSubFolder()
    {
        Assert.Equal("docs", ExtractPathResolver.TrimCurrentFolderPrefix("docs", "docs", preserveFullPath: false));
    }

    [Fact]
    public void ResolveRelativePath_SanitizesTraversal()
    {
        // Zip Slip：../ 组件被丢弃，但正常组件保留（最终逃逸由 GetSafePath 拦截）。
        // SanitizeEntryPath 语义：丢弃 ".."/"." 段，保留普通目录段。
        Assert.Equal("docs/evil.txt", ExtractPathResolver.ResolveRelativePath("docs/../../evil.txt", "", preserveFullPath: false));
    }

    [Fact]
    public void ResolveRelativePath_PureTraversal_DropsAll()
    {
        // 纯穿越路径净化后回到根级文件名
        Assert.Equal("evil.txt", ExtractPathResolver.ResolveRelativePath("../../evil.txt", "", preserveFullPath: false));
    }

    [Fact]
    public void ResolveRelativePath_InvalidChars_Sanitized()
    {
        // 非法文件名字符的组件被丢弃
        Assert.Equal("bad.txt", ExtractPathResolver.ResolveRelativePath("bad*name/bad.txt", "", preserveFullPath: false));
    }

    [Fact]
    public void ResolveRelativePath_TrimThenSanitize()
    {
        // 裁剪与净化叠加：先裁剪当前目录前缀，再净化剩余路径
        Assert.Equal("a.txt", ExtractPathResolver.ResolveRelativePath("docs/../a.txt", "docs", preserveFullPath: false));
    }

    [Fact]
    public void ResolveAll_ReturnsDictionaryKeyedByFullPath()
    {
        var entries = new[]
        {
            Item("docs/a.txt"),
            Item("docs/sub/b.txt")
        };
        var result = ExtractPathResolver.ResolveAll(entries, "docs", preserveFullPath: false);
        Assert.Equal("a.txt", result["docs/a.txt"]);
        Assert.Equal("sub/b.txt", result["docs/sub/b.txt"]);
    }

    [Fact]
    public void ResolveAll_PreserveFullPath_KeepsStructure()
    {
        var entries = new[]
        {
            Item("docs/a.txt"),
            Item("docs/sub/b.txt")
        };
        var result = ExtractPathResolver.ResolveAll(entries, "docs", preserveFullPath: true);
        Assert.Equal("docs/a.txt", result["docs/a.txt"]);
        Assert.Equal("docs/sub/b.txt", result["docs/sub/b.txt"]);
    }

    [Fact]
    public void ResolveAll_AllowDirectory_ResolvesDirectoryEntries()
    {
        var entries = new[]
        {
            Item("docs/sub", isDir: true),
            Item("docs/sub/b.txt")
        };
        var result = ExtractPathResolver.ResolveAll(entries, "docs", preserveFullPath: false);
        Assert.Equal("sub", result["docs/sub"]);
        Assert.Equal("sub/b.txt", result["docs/sub/b.txt"]);
    }
}