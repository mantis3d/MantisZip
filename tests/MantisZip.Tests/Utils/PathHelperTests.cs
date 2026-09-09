using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// PathHelper —— 唯一路径生成的单元测试。
/// 覆盖正常路径、双扩展名、以及并发碰撞场景。
/// </summary>
public class PathHelperTests : IDisposable
{
    private readonly string _tempDir;

    public PathHelperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PathHelperTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void GetUniquePath_FileNotExist_ReturnsWithSuffix1()
    {
        var path = Path.Combine(_tempDir, "file.txt");
        var result = PathHelper.GetUniquePath(path);
        Assert.Equal(Path.Combine(_tempDir, "file (1).txt"), result);
    }

    [Fact]
    public void GetUniquePath_FileExists_ReturnsWithNextSuffix()
    {
        var path = Path.Combine(_tempDir, "file.txt");
        File.WriteAllText(path, "content");
        var result = PathHelper.GetUniquePath(path);
        Assert.Equal(Path.Combine(_tempDir, "file (1).txt"), result);
    }

    [Fact]
    public void GetUniquePath_MultipleCollisions_Iterates()
    {
        var dir = _tempDir;
        File.WriteAllText(Path.Combine(dir, "file.txt"), "");
        File.WriteAllText(Path.Combine(dir, "file (1).txt"), "");
        File.WriteAllText(Path.Combine(dir, "file (2).txt"), "");
        var result = PathHelper.GetUniquePath(Path.Combine(dir, "file.txt"));
        Assert.Equal(Path.Combine(dir, "file (3).txt"), result);
    }

    [Fact]
    public void GetUniquePath_TarGz_DoubleExtension_Handled()
    {
        var path = Path.Combine(_tempDir, "archive.tar.gz");
        File.WriteAllText(path, "");
        var result = PathHelper.GetUniquePath(path);
        Assert.Equal(Path.Combine(_tempDir, "archive (1).tar.gz"), result);
    }

    [Fact]
    public void GetUniquePath_TarGz_NoCollision_ReturnsWithSuffix1()
    {
        var path = Path.Combine(_tempDir, "archive.tar.gz");
        var result = PathHelper.GetUniquePath(path);
        Assert.Equal(Path.Combine(_tempDir, "archive (1).tar.gz"), result);
    }

    [Fact]
    public void GetUniquePath_Subdirectory_PreservesDir()
    {
        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);
        var path = Path.Combine(subDir, "file.txt");
        File.WriteAllText(path, "");
        var result = PathHelper.GetUniquePath(path);
        Assert.Equal(Path.Combine(subDir, "file (1).txt"), result);
    }

    [Fact]
    public void GetUniquePath_999Collisions_ReturnsOriginalPath()
    {
        // 占满所有 999 个候选名
        for (int i = 0; i < 999; i++)
            File.WriteAllText(Path.Combine(_tempDir, $"file ({i + 1}).txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "file.txt"), "");

        var result = PathHelper.GetUniquePath(Path.Combine(_tempDir, "file.txt"));
        // 超过上限 → 返回原路径（将覆盖）
        Assert.Equal(Path.Combine(_tempDir, "file.txt"), result);
    }
}
