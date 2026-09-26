using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// FileConflictHelper —— 路径安全与冲突处理的单元测试。
/// 重点覆盖 Zip Slip 防护（GetSafePath/SanitizeEntryPath）和文件名净化。
/// </summary>
public class FileConflictHelperTests
{
    // ════════════════════════════════════════════════════════════════
    //  GetSafePath — Zip Slip 防护
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetSafePath_NormalPath_ReturnsCombinedPath()
    {
        var result = FileConflictHelper.GetSafePath(@"C:\dest", "file.txt");
        Assert.Equal(Path.GetFullPath(@"C:\dest\file.txt"), result);
    }

    [Fact]
    public void GetSafePath_SubdirectoryPath_ReturnsCombinedPath()
    {
        var result = FileConflictHelper.GetSafePath(@"C:\dest", "subdir/file.txt");
        Assert.Equal(Path.GetFullPath(@"C:\dest\subdir\file.txt"), result);
    }

    [Fact]
    public void GetSafePath_TraversalAttack_ThrowsInvalidOperationException()
    {
        // Zip Slip: ../ 试图逃逸目标目录
        Assert.Throws<InvalidOperationException>(() =>
            FileConflictHelper.GetSafePath(@"C:\dest", "../evil.txt"));
    }

    [Fact]
    public void GetSafePath_NestedTraversal_ThrowsInvalidOperationException()
    {
        // 多层穿越
        Assert.Throws<InvalidOperationException>(() =>
            FileConflictHelper.GetSafePath(@"C:\dest", "sub/../../evil.txt"));
    }

    [Fact]
    public void GetSafePath_AbsolutePath_ThrowsInvalidOperationException()
    {
        // 绝对路径注入
        Assert.Throws<InvalidOperationException>(() =>
            FileConflictHelper.GetSafePath(@"C:\dest", @"C:\Windows\System32\evil.txt"));
    }

    [Fact]
    public void GetSafePath_BackslashTraversal_ThrowsInvalidOperationException()
    {
        // 反斜杠穿越
        Assert.Throws<InvalidOperationException>(() =>
            FileConflictHelper.GetSafePath(@"C:\dest", @"..\evil.txt"));
    }

    [Fact]
    public void GetSafePath_DotDotOnly_ThrowsInvalidOperationException()
    {
        // 纯 ".."
        Assert.Throws<InvalidOperationException>(() =>
            FileConflictHelper.GetSafePath(@"C:\dest", ".."));
    }

    [Fact]
    public void GetSafePath_EmptyEntryName_ReturnsDestDir()
    {
        // 空条目名 → 组合后就是目标目录本身
        var result = FileConflictHelper.GetSafePath(@"C:\dest", "");
        Assert.Equal(Path.GetFullPath(@"C:\dest"), result);
    }

    // ════════════════════════════════════════════════════════════════
    //  SanitizeEntryPath — 条目路径净化
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SanitizeEntryPath_NormalPath_Unchanged()
    {
        Assert.Equal("docs/file.txt", FileConflictHelper.SanitizeEntryPath("docs/file.txt"));
    }

    [Fact]
    public void SanitizeEntryPath_Backslash_ConvertedToSlash()
    {
        Assert.Equal("docs/file.txt", FileConflictHelper.SanitizeEntryPath(@"docs\file.txt"));
    }

    [Fact]
    public void SanitizeEntryPath_TraversalComponents_Dropped()
    {
        // ../ 被丢弃，保留正常组件
        Assert.Equal("docs/evil.txt", FileConflictHelper.SanitizeEntryPath("docs/../../evil.txt"));
    }

    [Fact]
    public void SanitizeEntryPath_PureTraversal_DropsAll()
    {
        // 纯穿越路径净化后回到根级文件名
        Assert.Equal("evil.txt", FileConflictHelper.SanitizeEntryPath("../../evil.txt"));
    }

    [Fact]
    public void SanitizeEntryPath_DotComponents_Dropped()
    {
        // . 组件被丢弃
        Assert.Equal("docs/file.txt", FileConflictHelper.SanitizeEntryPath("docs/./file.txt"));
    }

    [Fact]
    public void SanitizeEntryPath_InvalidFileNameChars_Dropped()
    {
        // 包含非法字符的组件被丢弃
        Assert.Equal("bad.txt", FileConflictHelper.SanitizeEntryPath("bad*name/bad.txt"));
    }

    [Fact]
    public void SanitizeEntryPath_MixedComponents_KeepsValid()
    {
        // 混合有效/无效组件：bad* 被丢弃（* 是非法字符）
        Assert.Equal("good/file.txt", FileConflictHelper.SanitizeEntryPath("good/bad*/file.txt"));
    }

    [Fact]
    public void SanitizeEntryPath_AllInvalid_ThrowsInvalidOperationException()
    {
        // 所有组件都被净化掉 → 抛异常
        Assert.Throws<InvalidOperationException>(() =>
            FileConflictHelper.SanitizeEntryPath("../.."));
    }

    [Fact]
    public void SanitizeEntryPath_LeadingSlash_Trimmed()
    {
        // 前导斜杠被 Normalize 处理
        Assert.Equal("file.txt", FileConflictHelper.SanitizeEntryPath("/file.txt"));
    }

    // ════════════════════════════════════════════════════════════════
    //  ResolveByAction — 冲突策略
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolvePath_FileNotExist_ReturnsSamePath()
    {
        // 文件不存在 → 直接返回路径（无需冲突处理）
        var testFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.txt");
        var result = FileConflictHelper.ResolvePath(testFile, null);
        Assert.Equal(testFile, result);
    }
}
