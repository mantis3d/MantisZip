using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// <see cref="ArchiveVolumeSet"/> 分卷命名识别与兄弟卷枚举（issue #70①②③ 的单一事实源）。
/// 全部用临时文件构造，不依赖语料。
/// </summary>
public sealed class ArchiveVolumeSetTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "MantisZipVolumeSetTests", Guid.NewGuid().ToString("N"));

    public ArchiveVolumeSetTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响断言
        }
    }

    private string Create(string name, int bytes = 4)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public void TryGetVolumeBase_SplitsBaseNameAndIndex()
    {
        var path = Path.Combine(_dir, "pack.7z.003");

        Assert.True(ArchiveVolumeSet.TryGetVolumeBase(path, out var basePath, out var index));
        Assert.Equal(Path.Combine(_dir, "pack.7z"), basePath);
        Assert.Equal(3, index);
    }

    [Fact]
    public void TryGetVolumeBase_RejectsPlainAndMalformedNames()
    {
        Assert.False(ArchiveVolumeSet.TryGetVolumeBase(Path.Combine(_dir, "pack.7z"), out _, out _));
        Assert.False(ArchiveVolumeSet.TryGetVolumeBase(Path.Combine(_dir, "pack.7z.000"), out _, out _));
        Assert.False(ArchiveVolumeSet.TryGetVolumeBase(Path.Combine(_dir, "pack.7z.1"), out _, out _));
        Assert.False(ArchiveVolumeSet.TryGetVolumeBase(string.Empty, out _, out _));
    }

    [Fact]
    public void EnumerateVolumes_ReturnsContiguousSetInOrder()
    {
        Create("pack.7z.001");
        Create("pack.7z.002");
        var last = Create("pack.7z.003");

        var volumes = ArchiveVolumeSet.EnumerateVolumes(last);

        Assert.Equal(
            [Path.Combine(_dir, "pack.7z.001"), Path.Combine(_dir, "pack.7z.002"), last],
            volumes);
    }

    [Fact]
    public void EnumerateVolumes_StopsAtFirstGap()
    {
        Create("pack.zip.001");
        var third = Create("pack.zip.003"); // .002 缺失

        var volumes = ArchiveVolumeSet.EnumerateVolumes(third);

        Assert.Single(volumes);
        Assert.EndsWith("pack.zip.001", volumes[0], StringComparison.Ordinal);
    }

    [Fact]
    public void EnumerateVolumes_PlainFileReturnsItself()
    {
        var plain = Create("single.zip");

        Assert.Equal([plain], ArchiveVolumeSet.EnumerateVolumes(plain));
    }

    [Fact]
    public void FindFirstVolume_AnchorsAnyVolumeToFirst()
    {
        var first = Create("pack.7z.001");
        Create("pack.7z.002");
        var second = Path.Combine(_dir, "pack.7z.002");

        Assert.Equal(first, ArchiveVolumeSet.FindFirstVolume(second));
        Assert.Equal(first, ArchiveVolumeSet.FindFirstVolume(first));
    }

    [Fact]
    public void FindFirstVolume_ReturnsNullWhenFirstVolumeMissing()
    {
        var second = Create("pack.7z.002"); // .001 不存在

        Assert.Null(ArchiveVolumeSet.FindFirstVolume(second));
    }

    [Fact]
    public void FindFirstVolume_KeepsPlainPathAndRejectsMissingFile()
    {
        var plain = Create("single.zip");

        Assert.Equal(plain, ArchiveVolumeSet.FindFirstVolume(plain));
        Assert.Null(ArchiveVolumeSet.FindFirstVolume(Path.Combine(_dir, "nope.zip")));
    }
}
