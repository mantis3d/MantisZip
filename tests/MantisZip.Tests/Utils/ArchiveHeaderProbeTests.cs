using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// <see cref="ArchiveHeaderProbe"/> 头部结构探测测试（方案 B）。
/// 语料见 tests/corpus/corrupt-archives/（损坏 7z/RAR 与加密头同消息，靠结构区分）。
/// 纯字节解析，无 7z.dll 依赖。
/// </summary>
public class ArchiveHeaderProbeTests
{
    private static string CorpusPath(string fileName) =>
        Path.Combine(GetRepoRoot(), "tests", "corpus", "corrupt-archives", fileName);

    /// <summary>从测试程序集输出目录向上遍历，找到第一个含 src 子目录的目录作为仓库根。</summary>
    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException(
            "无法定位仓库根（祖先目录链中未找到 'src' 目录）。" +
            $"Base directory: {AppContext.BaseDirectory}");
    }

    [Fact]
    public void Corpus_IsLocatable()
    {
        Assert.True(File.Exists(CorpusPath("valid.7z")),
            $"语料库样本缺失：{CorpusPath("valid.7z")}");
    }

    [Theory]
    // 7z：结构完好（含加密头 / 数据区损坏但头部完好）→ Intact
    [InlineData("valid.7z", HeaderStructure.Intact)]
    [InlineData("corrupt-15-7z-solid-data-flipped.7z", HeaderStructure.Intact)]
    [InlineData("corrupt-16-7z-data-flipped.7z", HeaderStructure.Intact)]
    [InlineData("enc-03-7z-encrypted.7z", HeaderStructure.Intact)]
    [InlineData("enc-04-7z-encrypt-headers.7z", HeaderStructure.Intact)]
    [InlineData("vol-01-7z-split.001", HeaderStructure.Intact)]
    // 7z：结构明确损坏 → Broken
    [InlineData("corrupt-08-7z-bad-header.7z", HeaderStructure.Broken)]
    [InlineData("corrupt-14-7z-truncated-tail.7z", HeaderStructure.Broken)]
    [InlineData("vol-02-7z-missing-part.001", HeaderStructure.Broken)]
    // RAR：结构完好（含加密头）→ Intact
    [InlineData("valid.rar", HeaderStructure.Intact)]
    [InlineData("valid-rar4.rar", HeaderStructure.Intact)]
    [InlineData("corrupt-25-rar-truncated.rar", HeaderStructure.Intact)]
    [InlineData("corrupt-26-rar-data-flipped.rar", HeaderStructure.Intact)]
    [InlineData("enc-05-rar-encrypted.rar", HeaderStructure.Intact)]
    [InlineData("enc-06-rar-headers-encrypted.rar", HeaderStructure.Intact)]
    // RAR：签名损坏 → Broken
    [InlineData("corrupt-24-rar-bad-signature.rar", HeaderStructure.Broken)]
    // 非 7z/RAR（ZIP/TAR）→ Unknown（SharpCompress 消息清晰，无需探测）
    [InlineData("valid.zip", HeaderStructure.Unknown)]
    [InlineData("corrupt-06-empty.zip", HeaderStructure.Unknown)]
    [InlineData("enc-01-zip-aes.zip", HeaderStructure.Unknown)]
    [InlineData("valid.tar", HeaderStructure.Unknown)]
    public void Probe_CorpusSamples_ReturnsExpected(string fileName, HeaderStructure expected)
    {
        Assert.Equal(expected, ArchiveHeaderProbe.Probe(CorpusPath(fileName)));
    }

    [Fact]
    public void Probe_MissingFile_ReturnsUnknown()
    {
        Assert.Equal(HeaderStructure.Unknown,
            ArchiveHeaderProbe.Probe(CorpusPath("does-not-exist.7z")));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Probe_NullOrEmpty_ReturnsUnknown(string? path)
    {
        Assert.Equal(HeaderStructure.Unknown, ArchiveHeaderProbe.Probe(path!));
    }
}
