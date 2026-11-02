using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// <see cref="RarHeaderValidator"/>：RAR5 块链结构校验（issue #69 在 RAR 上的残留）。
/// 语料见 tests/corpus/rar5/（WinRAR 7.00 生成）；损坏变体在临时目录里现改，不额外提交坏包。
/// </summary>
public sealed class RarHeaderValidatorTests : IDisposable
{
    private readonly string _work = Path.Combine(
        Path.GetTempPath(), "MantisZipRarValidatorTests", Guid.NewGuid().ToString("N"));

    public RarHeaderValidatorTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_work))
                Directory.Delete(_work, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响断言
        }
    }

    private static string Rar5CorpusPath(string fileName) =>
        Path.Combine(GetRepoRoot(), "tests", "corpus", "rar5", fileName);

    private static string Rar4CorpusPath(string fileName) =>
        Path.Combine(GetRepoRoot(), "tests", "corpus", "corrupt-archives", fileName);

    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException(
            $"无法定位仓库根（祖先目录链中未找到 'src'）。Base directory: {AppContext.BaseDirectory}");
    }

    /// <summary>把语料里的 RAR5 分卷集整套复制到临时目录，返回各卷路径（按卷号顺序）。</summary>
    private List<string> CopySplitSet()
    {
        var corpusDir = Path.GetDirectoryName(Rar5CorpusPath("valid-rar5-split.part1.rar"))!;
        var volumes = Directory.GetFiles(corpusDir, "valid-rar5-split.part*.rar")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var copies = new List<string>(volumes.Count);
        foreach (var src in volumes)
        {
            var dest = Path.Combine(_work, Path.GetFileName(src));
            File.Copy(src, dest);
            copies.Add(dest);
        }
        return copies;
    }

    [Fact]
    public void Validate_SingleVolumeRar5_IsIntact()
    {
        Assert.Equal(RarStructureStatus.Intact, RarHeaderValidator.Validate(Rar5CorpusPath("valid-rar5.rar")));
    }

    [Fact]
    public void Validate_SplitRar5_IsIntact()
    {
        var volumes = CopySplitSet();

        Assert.Equal(RarStructureStatus.Intact, RarHeaderValidator.Validate(volumes[0]));
        // 从任意一卷传入都应整组校验通过
        Assert.Equal(RarStructureStatus.Intact, RarHeaderValidator.Validate(volumes[^1]));
    }

    [Fact]
    public void Validate_TailCorruptedLastVolume_IsBroken()
    {
        // 报告者 #68② 的现象：末卷尾部（end-of-archive 块）被改坏，7z.dll 的 Check 仍报通过
        var volumes = CopySplitSet();
        var last = volumes[^1];
        var bytes = File.ReadAllBytes(last);
        for (var i = Math.Max(0, bytes.Length - 64); i < bytes.Length; i++)
            bytes[i] ^= 0xFF;
        File.WriteAllBytes(last, bytes);

        Assert.Equal(RarStructureStatus.Broken, RarHeaderValidator.Validate(volumes[0]));
    }

    [Fact]
    public void Validate_MissingLastVolume_IsBroken()
    {
        var volumes = CopySplitSet();
        File.Delete(volumes[^1]);

        Assert.Equal(RarStructureStatus.Broken, RarHeaderValidator.Validate(volumes[0]));
    }

    [Fact]
    public void Validate_TruncatedMiddleVolume_IsBroken()
    {
        var volumes = CopySplitSet();
        var middle = volumes[1];
        var bytes = File.ReadAllBytes(middle);
        File.WriteAllBytes(middle, bytes[..(bytes.Length - 1024)]);

        Assert.Equal(RarStructureStatus.Broken, RarHeaderValidator.Validate(volumes[0]));
    }

    [Fact]
    public void Validate_DamagedMiddleVolumeData_StaysIntact()
    {
        // 数据区损坏由 7z.dll 的 Check 负责（块链只看头），探针不该抢着判坏
        var volumes = CopySplitSet();
        var middle = volumes[1];
        var bytes = File.ReadAllBytes(middle);
        for (var i = bytes.Length / 3; i < bytes.Length / 3 + 64; i++)
            bytes[i] ^= 0xFF;
        File.WriteAllBytes(middle, bytes);

        Assert.Equal(RarStructureStatus.Intact, RarHeaderValidator.Validate(volumes[0]));
    }

    [Fact]
    public void Validate_Rar4Corpus_IsUnknown()
    {
        // 只支持 RAR5（RAR4 块结构不同，本轮不实现，已登记 KNOWN_ISSUES）
        Assert.Equal(RarStructureStatus.Unknown, RarHeaderValidator.Validate(Rar4CorpusPath("valid-rar4.rar")));
    }

    [Fact]
    public void Validate_NonRarFile_IsUnknown()
    {
        var zip = Path.Combine(_work, "plain.zip");
        File.WriteAllBytes(zip, [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00]);

        Assert.Equal(RarStructureStatus.Unknown, RarHeaderValidator.Validate(zip));
        Assert.Equal(RarStructureStatus.Unknown, RarHeaderValidator.Validate(string.Empty));
        Assert.Equal(RarStructureStatus.Unknown, RarHeaderValidator.Validate(Path.Combine(_work, "missing.rar")));
    }
}
