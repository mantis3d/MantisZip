using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// ArchivePath —— 压缩包路径工具的单元测试。
/// 覆盖路径归一化、尾部斜杠处理、文件名/目录名提取、双扩展名处理。
/// </summary>
public class ArchivePathTests
{
    // ════════════════════════════════════════════════════════════════
    //  Normalize — 反斜杠→正斜杠
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Normalize_Backslash_ConvertedToSlash()
    {
        Assert.Equal("docs/file.txt", ArchivePath.Normalize(@"docs\file.txt"));
    }

    [Fact]
    public void Normalize_Null_ReturnsEmpty()
    {
        Assert.Equal("", ArchivePath.Normalize(null));
    }

    [Fact]
    public void Normalize_Empty_ReturnsEmpty()
    {
        Assert.Equal("", ArchivePath.Normalize(""));
    }

    [Fact]
    public void Normalize_AlreadyNormalized_Unchanged()
    {
        Assert.Equal("docs/file.txt", ArchivePath.Normalize("docs/file.txt"));
    }

    [Fact]
    public void Normalize_MixedSlashes_AllForward()
    {
        Assert.Equal("a/b/c.txt", ArchivePath.Normalize(@"a\b/c.txt"));
    }

    // ════════════════════════════════════════════════════════════════
    //  TrimEndSeparator — 尾部斜杠清理
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void TrimEndSeparator_TrailingSlash_Removed()
    {
        Assert.Equal("docs/file.txt", ArchivePath.TrimEndSeparator("docs/file.txt/"));
    }

    [Fact]
    public void TrimEndSeparator_TrailingBackslash_Removed()
    {
        // 只去除尾部斜杠，不做斜杠转换
        Assert.Equal(@"docs\file.txt", ArchivePath.TrimEndSeparator(@"docs\file.txt\"));
    }

    [Fact]
    public void TrimEndSeparator_NoSeparator_Unchanged()
    {
        Assert.Equal("docs/file.txt", ArchivePath.TrimEndSeparator("docs/file.txt"));
    }

    [Fact]
    public void TrimEndSeparator_RootPathC_Preserved()
    {
        Assert.Equal(@"C:\", ArchivePath.TrimEndSeparator(@"C:\"));
    }

    [Fact]
    public void TrimEndSeparator_RootPathCNoSlash_Preserved()
    {
        // 长度 <= 3 的路径不处理
        Assert.Equal("C:", ArchivePath.TrimEndSeparator("C:"));
    }

    [Fact]
    public void TrimEndSeparator_MultipleTrailing_AllRemoved()
    {
        Assert.Equal("docs", ArchivePath.TrimEndSeparator("docs///"));
    }

    // ════════════════════════════════════════════════════════════════
    //  GetFileName — 文件名提取
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetFileName_NormalFile_ReturnsName()
    {
        Assert.Equal("file.txt", ArchivePath.GetFileName("docs/file.txt"));
    }

    [Fact]
    public void GetFileName_TrailingSlash_ReturnsName()
    {
        Assert.Equal("file.txt", ArchivePath.GetFileName("docs/file.txt/"));
    }

    [Fact]
    public void GetFileName_DirectoryPath_ReturnsDirName()
    {
        Assert.Equal("sub", ArchivePath.GetFileName("docs/sub"));
    }

    // ════════════════════════════════════════════════════════════════
    //  GetDirectoryName — 父目录提取
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetDirectoryName_NormalFile_ReturnsDir()
    {
        Assert.Equal("docs", ArchivePath.GetDirectoryName("docs/file.txt"));
    }

    [Fact]
    public void GetDirectoryName_TrailingSlash_ReturnsDir()
    {
        Assert.Equal("docs", ArchivePath.GetDirectoryName("docs/file.txt/"));
    }

    [Fact]
    public void GetDirectoryName_TopLevelFile_ReturnsEmpty()
    {
        // Path.GetDirectoryName("file.txt") returns "" on Windows, not null
        Assert.Equal("", ArchivePath.GetDirectoryName("file.txt"));
    }

    // ════════════════════════════════════════════════════════════════
    //  GetFileNameWithoutExtension — 文件名（不含扩展名）
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetFileNameWithoutExtension_NormalFile_ReturnsName()
    {
        Assert.Equal("file", ArchivePath.GetFileNameWithoutExtension("docs/file.txt"));
    }

    [Fact]
    public void GetFileNameWithoutExtension_TarGz_DoubleExt_Handled()
    {
        // .tar.gz 作为整体扩展名处理
        Assert.Equal("archive", ArchivePath.GetFileNameWithoutExtension("docs/archive.tar.gz"));
    }

    [Fact]
    public void GetFileNameWithoutExtension_TarGz_TrailingSlash_Handled()
    {
        Assert.Equal("archive", ArchivePath.GetFileNameWithoutExtension("docs/archive.tar.gz/"));
    }

    [Fact]
    public void GetFileNameWithoutExtension_TarGz_CaseInsensitive()
    {
        Assert.Equal("archive", ArchivePath.GetFileNameWithoutExtension("archive.TAR.GZ"));
    }

    [Fact]
    public void GetFileNameWithoutExtension_GzOnly_NotDoubleExt()
    {
        // 只有 .gz 不是双扩展名
        Assert.Equal("archive", ArchivePath.GetFileNameWithoutExtension("archive.gz"));
    }

    [Fact]
    public void GetFileNameWithoutExtension_NoExtension_ReturnsName()
    {
        Assert.Equal("Makefile", ArchivePath.GetFileNameWithoutExtension("Makefile"));
    }
}
