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

    [Fact]
    public void TryGetVolumeBase_ParsesWinRarPartNaming()
    {
        Assert.True(ArchiveVolumeSet.TryGetVolumeBase(Path.Combine(_dir, "pack.part2.rar"), out var basePath, out var index));
        Assert.Equal(Path.Combine(_dir, "pack.rar"), basePath);
        Assert.Equal(2, index);

        // 补零写法（part01）同样识别，锚点仍是 archive.rar
        Assert.True(ArchiveVolumeSet.TryGetVolumeBase(Path.Combine(_dir, "pack.part01.rar"), out basePath, out index));
        Assert.Equal(Path.Combine(_dir, "pack.rar"), basePath);
        Assert.Equal(1, index);

        // .rar 本身与非法卷号不算分卷
        Assert.False(ArchiveVolumeSet.TryGetVolumeBase(Path.Combine(_dir, "pack.rar"), out _, out _));
        Assert.False(ArchiveVolumeSet.TryGetVolumeBase(Path.Combine(_dir, "pack.part0.rar"), out _, out _));
    }

    [Fact]
    public void TryGetVolumeBase_ParsesWinRarOldNaming()
    {
        // 旧式：首卷 archive.rar，其后 .r00（第 2 卷）、.r01（第 3 卷）
        Assert.True(ArchiveVolumeSet.TryGetVolumeBase(Path.Combine(_dir, "pack.r00"), out var basePath, out var index));
        Assert.Equal(Path.Combine(_dir, "pack.rar"), basePath);
        Assert.Equal(2, index);

        Assert.True(ArchiveVolumeSet.TryGetVolumeBase(Path.Combine(_dir, "pack.r01"), out basePath, out index));
        Assert.Equal(Path.Combine(_dir, "pack.rar"), basePath);
        Assert.Equal(3, index);
    }

    [Fact]
    public void EnumerateVolumes_RarPartSet_IsContiguousAndOrdered()
    {
        var first = Create("pack.part1.rar");
        var second = Create("pack.part2.rar");
        var third = Create("pack.part3.rar");
        Create("pack.part5.rar"); // 缺 part4 → 枚举到此为止

        var volumes = ArchiveVolumeSet.EnumerateVolumes(Path.Combine(_dir, "pack.part2.rar"));

        Assert.Equal([first, second, third], volumes);
    }

    [Fact]
    public void EnumerateVolumes_RarOldSet_StartsWithPlainRar()
    {
        var first = Create("old.rar");
        var r00 = Create("old.r00");
        Create("old.r02"); // 缺 .r01 → 枚举到此为止

        Assert.Equal([first, r00], ArchiveVolumeSet.EnumerateVolumes(Path.Combine(_dir, "old.r00")));
    }

    [Fact]
    public void FindFirstVolume_RarPartAnchorsToPart1()
    {
        var first = Create("pack.part1.rar");
        var third = Create("pack.part3.rar");

        Assert.Equal(first, ArchiveVolumeSet.FindFirstVolume(third));
    }

    [Fact]
    public void GetVolumeWatchFilter_CoversWholeSet()
    {
        // 监视过滤器要能看见整组的增删（ArchiveFileGuard 依赖它判定 Missing / Replaced）
        Assert.Equal("pack.7z.*", ArchiveVolumeSet.GetVolumeWatchFilter(Path.Combine(_dir, "pack.7z.002")));
        Assert.Equal("pack.part*.rar", ArchiveVolumeSet.GetVolumeWatchFilter(Path.Combine(_dir, "pack.part2.rar")));
        Assert.Equal("pack.*", ArchiveVolumeSet.GetVolumeWatchFilter(Path.Combine(_dir, "pack.r00")));
        Assert.Equal("pack.zip", ArchiveVolumeSet.GetVolumeWatchFilter(Path.Combine(_dir, "pack.zip")));
    }

    [Fact]
    public void MatchesArchiveExtension_ShowsEveryVolumeOfTheSet()
    {
        // issue #70③：TEST.7z.002 之类中间卷也要能显示（此前只亮 *.001）
        Assert.True(ArchiveVolumeSet.MatchesArchiveExtension(Path.Combine(_dir, "TEST.7z.002"), ".7z"));
        Assert.True(ArchiveVolumeSet.MatchesArchiveExtension(Path.Combine(_dir, "TEST_RAR.part2.rar"), ".rar"));
        Assert.True(ArchiveVolumeSet.MatchesArchiveExtension(Path.Combine(_dir, "old.r00"), ".rar"));
        Assert.True(ArchiveVolumeSet.MatchesArchiveExtension(Path.Combine(_dir, "pack.tar.gz.002"), ".tar.gz"));

        // 基名格式不同 / 不是分卷 / 空参数 → 不匹配
        Assert.False(ArchiveVolumeSet.MatchesArchiveExtension(Path.Combine(_dir, "TEST.7z.002"), ".zip"));
        Assert.False(ArchiveVolumeSet.MatchesArchiveExtension(Path.Combine(_dir, "TEST.7z"), ".7z"));
        Assert.False(ArchiveVolumeSet.MatchesArchiveExtension(Path.Combine(_dir, "TEST.7z.002"), string.Empty));
    }
}
