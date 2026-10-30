using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Engines;

/// <summary>
/// 分卷压缩包支持（issue #69 / #70）：路由到 7z 引擎、以第一卷为锚点、头部 CRC 校验能识别「末卷损坏」。
/// 语料见 tests/corpus/corrupt-archives/（vol-01-7z-split.001..003 与 vol-03.zip.001..003）。
/// </summary>
public sealed class SplitArchiveTests : IDisposable
{
    private readonly string _work = Path.Combine(
        Path.GetTempPath(), "MantisZipSplitTests", Guid.NewGuid().ToString("N"));

    public SplitArchiveTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_work))
                Directory.Delete(_work, recursive: true);
        }
        catch (IOException)
        {
            // 7z.dll 可能还握着句柄；清理失败不影响断言
        }
    }

    private static string CorpusPath(string fileName) =>
        Path.Combine(GetRepoRoot(), "tests", "corpus", "corrupt-archives", fileName);

    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException(
            $"无法定位仓库根（祖先目录链中未找到 'src'）。Base directory: {AppContext.BaseDirectory}");
    }

    /// <summary>把语料里的分卷集整套复制到临时目录，返回首卷路径。</summary>
    private string CopyVolumeSet(string corpusFirst, string baseName)
    {
        var corpusDir = Path.GetDirectoryName(CorpusPath(corpusFirst))!;
        var prefix = Path.GetFileName(corpusFirst)[..^4]; // 去掉 ".001"
        var first = string.Empty;
        foreach (var src in Directory.GetFiles(corpusDir, prefix + ".*"))
        {
            var suffix = Path.GetExtension(src); // ".001"
            var dest = Path.Combine(_work, baseName + suffix);
            File.Copy(src, dest);
            if (suffix == ".001")
                first = dest;
        }
        return first;
    }

    [Fact]
    public async Task SplitZip_ListsAndExtractsThroughSevenZipEngine()
    {
        // issue #70①：分卷 zip 交给 7z.dll 才能读（SharpCompress 不支持多卷）
        var first = CopyVolumeSet("vol-03.zip.001", "split.zip");
        var engine = new SevenZipEngine();

        var entries = await engine.ListEntriesAsync(first);
        Assert.Equal(15, entries.Count);

        var dest = Path.Combine(_work, "out");
        var result = await engine.ExtractAsync(first, dest);

        Assert.Equal(0, result.FailedEntries);
        Assert.Equal(14, Directory.GetFiles(dest, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Factory_RoutesSplitVolumesToSevenZipEngine()
    {
        Assert.IsType<SevenZipEngine>(ArchiveEngineFactory.GetEngineByExtension(@"C:\x\pack.zip.001"));
        Assert.IsType<SevenZipEngine>(ArchiveEngineFactory.GetEngineByExtension(@"C:\x\pack.zip.002"));
        Assert.IsType<SevenZipEngine>(ArchiveEngineFactory.GetEngineByExtension(@"C:\x\pack.7z.003"));
        Assert.IsType<SevenZipEngine>(ArchiveEngineFactory.GetEngineByExtension(@"C:\x\pack.rar.001"));
    }

    [Fact]
    public void Factory_ResolveVolumeAnchor_WalksBackToFirstVolume()
    {
        var first = CopyVolumeSet("vol-01-7z-split.001", "set.7z");
        var third = Path.Combine(_work, "set.7z.003");

        Assert.Equal(first, ArchiveEngineFactory.ResolveVolumeAnchor(third));
        Assert.Equal(first, ArchiveEngineFactory.ResolveVolumeAnchor(first));
        // 首卷缺失时原样返回，交给上游按「打不开」处理
        var orphan = Path.Combine(_work, "orphan.7z.002");
        Assert.Equal(orphan, ArchiveEngineFactory.ResolveVolumeAnchor(orphan));
    }

    [Fact]
    public void Probe_DamagedLastVolume_IsBroken()
    {
        // issue #69：末卷（存 next-header）内容被改、长度不变时，7z.dll 报「是否加密？」的歧义异常；
        // 头部 CRC 校验必须能把它判成损坏，否则会被当成加密包弹密码框。
        var first = CopyVolumeSet("vol-01-7z-split.001", "tail.7z");
        var last = Path.Combine(_work, "tail.7z.003");
        var bytes = File.ReadAllBytes(last);
        for (var i = Math.Max(0, bytes.Length - 64); i < bytes.Length; i++)
            bytes[i] ^= 0xFF;
        File.WriteAllBytes(last, bytes);

        Assert.Equal(HeaderStructure.Broken, ArchiveHeaderProbe.Probe(first));
    }

    [Fact]
    public void Probe_DamagedMiddleVolumeData_StaysIntact()
    {
        // 中间数据卷损坏不影响「能否列出条目」（7z 仍能打开、只是解压该文件会失败），
        // 故探针保持 Intact —— 这类包不该被误判成损坏。
        var first = CopyVolumeSet("vol-01-7z-split.001", "middle.7z");
        var middle = Path.Combine(_work, "middle.7z.002");
        var bytes = File.ReadAllBytes(middle);
        for (var i = bytes.Length / 3; i < bytes.Length / 3 + 64; i++)
            bytes[i] ^= 0xFF;
        File.WriteAllBytes(middle, bytes);

        Assert.Equal(HeaderStructure.Intact, ArchiveHeaderProbe.Probe(first));
    }

    [Fact]
    public void Probe_IntactVolumeSet_IsIntact()
    {
        var first = CopyVolumeSet("vol-01-7z-split.001", "ok.7z");

        Assert.Equal(HeaderStructure.Intact, ArchiveHeaderProbe.Probe(first));
    }
}
