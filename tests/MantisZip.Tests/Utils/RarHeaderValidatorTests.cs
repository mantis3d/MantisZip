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
    public void Validate_Rar4SingleVolumeCorpus_IsIntact()
    {
        // issue #68②：RAR4 此前一律 Unknown（不做块链校验），现在按 RAR4 块结构逐块验证
        Assert.Equal(RarStructureStatus.Intact, RarHeaderValidator.Validate(Rar4CorpusPath("valid-rar4.rar")));
    }

    [Fact]
    public void Validate_Rar4TruncatedCorpus_IsBroken()
    {
        Assert.Equal(RarStructureStatus.Broken, RarHeaderValidator.Validate(Rar4CorpusPath("corrupt-25-rar-truncated.rar")));
    }

    [Fact]
    public void Validate_Rar4TamperedEndOfArchiveSize_IsBroken()
    {
        // 报告者 #68 截图 shot1 的改法：把末尾 ENDARC 块的 HEAD_SIZE 由 7 改成 6，
        // WinRAR 报错、而我们此前说「完好」。这里按同样手法现场改坏一份语料。
        var broken = Path.Combine(_work, "tampered-endarc.rar");
        File.Copy(Rar4CorpusPath("valid-rar4.rar"), broken);

        var bytes = File.ReadAllBytes(broken);
        var endOfArchiveOffset = FindRar4EndOfArchiveOffset(bytes);
        Assert.True(endOfArchiveOffset >= 0, "语料里没找到 RAR4 结束块，夹具已变");

        // 固定头布局：HEAD_CRC(2) HEAD_TYPE(1) HEAD_FLAGS(2) HEAD_SIZE(2)
        var headSizeOffset = endOfArchiveOffset + 5;
        Assert.Equal(7, bytes[headSizeOffset] | (bytes[headSizeOffset + 1] << 8));
        bytes[headSizeOffset] = 6;
        bytes[headSizeOffset + 1] = 0;
        File.WriteAllBytes(broken, bytes);

        var status = RarHeaderValidator.Validate(broken, out var reason);
        Assert.Equal(RarStructureStatus.Broken, status);
        Assert.NotNull(reason);
    }

    /// <summary>在字节流里按 RAR4 块链找出 ENDARC 块（0x7B）的起始偏移，找不到返回 -1。</summary>
    private static int FindRar4EndOfArchiveOffset(byte[] bytes)
    {
        const int fixedHeaderLength = 7;   // HEAD_CRC(2) + HEAD_TYPE(1) + HEAD_FLAGS(2) + HEAD_SIZE(2)
        const int dataStart = 7;           // ADD_SIZE(4) 紧跟在固定头之后（绝对偏移 7）
        const int longBlockFlag = 0x8000;
        const int largeFileFlag = 0x0100;  // LHD_LARGE / LHD_LARGE：再跟 8 字节高位 PACK_SIZE

        var position = 7;                  // 跳过 7 字节签名
        while (position + fixedHeaderLength <= bytes.Length)
        {
            int headType = bytes[position + 2];
            int flags = bytes[position + 3] | (bytes[position + 4] << 8);
            int size = bytes[position + 5] | (bytes[position + 6] << 8);
            if (size < fixedHeaderLength)
                return -1;
            if (headType == 0x7B)
                return position;

            long dataSize = 0;
            if ((flags & longBlockFlag) != 0 && position + dataStart + 4 <= bytes.Length)
            {
                dataSize = (uint)(bytes[position + dataStart]
                                  | (bytes[position + dataStart + 1] << 8)
                                  | (bytes[position + dataStart + 2] << 16)
                                  | (bytes[position + dataStart + 3] << 24));

                if ((flags & largeFileFlag) != 0 && position + 32 <= bytes.Length)
                {
                    dataSize |= (long)(uint)(bytes[position + 30]
                                             | (bytes[position + 31] << 8)
                                             | (bytes[position + 32] << 16)
                                             | (bytes[position + 33] << 24)) << 32;
                }
            }

            position += size + (int)dataSize;
        }
        return -1;
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
