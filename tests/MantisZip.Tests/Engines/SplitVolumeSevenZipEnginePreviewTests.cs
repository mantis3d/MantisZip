using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Engines;

/// <summary>
/// 分卷包「单条预览 / 单条提取」回归（2026-11-05，KI-017）。
/// </summary>
/// <remarks>
/// UI 曾经用扩展名直判格式：<c>vol-01-7z-split.001</c> 被当成 <see cref="ArchiveFormat.Zip"/> 交给
/// SharpCompress，预览 / 双击打开 / 缩略图全部抛 <c>Failed to locate the Zip Header</c>；
/// 传入有效格式（SevenZip）时同一入口正常。
/// <para>
/// 类名带 <c>SevenZipEngine</c>：与既有 7z 用例一样依赖真实 7z.dll，CI 的
/// <c>--filter "FullyQualifiedName!~SevenZipEngine"</c> 会跳过它；本机缺 7z.dll 时用例自行返回。
/// </para>
/// </remarks>
public sealed class SplitVolumeSevenZipEnginePreviewTests : IDisposable
{
    private const string EntryName = "note1.txt";
    private const string PayloadName = "payload.bin";
    private const int PayloadSize = 262144;

    private readonly string _work = Path.Combine(
        Path.GetTempPath(), "MantisZipSplitPreviewTests", Guid.NewGuid().ToString("N"));

    public SplitVolumeSevenZipEnginePreviewTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_work))
                Directory.Delete(_work, recursive: true);
        }
        catch (IOException)
        {
            // 7z.dll 可能还握着句柄
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

    private static bool HasSevenZipLibrary()
    {
        if (File.Exists(SevenZipEngine.SevenZipDllPath))
            return true;
        return File.Exists(Path.Combine(AppContext.BaseDirectory, "x64", "7z.dll"))
            || File.Exists(Path.Combine(AppContext.BaseDirectory, "x86", "7z.dll"))
            || File.Exists(@"C:\Program Files\7-Zip\7z.dll");
    }

    /// <summary>把语料里的分卷集整套复制到临时目录（改名成 <paramref name="baseName"/>），返回首卷路径。</summary>
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
    public async Task Split7z_PreviewThroughSevenZipEngine_ReadsEntryHead()
    {
        if (!HasSevenZipLibrary())
            return;

        var first = CopyVolumeSet("vol-01-7z-split.001", "set.7z");

        var format = ArchiveEngineFactory.ResolveReaderFormat(first);
        Assert.Equal(ArchiveFormat.SevenZip, format);

        var head = await ArchiveEntryExtractor.ExtractHeadAsync(first, EntryName, 256, format);

        Assert.NotEmpty(head);
        Assert.True(head.Length <= 256, $"head 长度越界：{head.Length}");
    }

    [Fact]
    public async Task Split7z_SingleEntryThroughSevenZipEngine_ExtractsWholeFile()
    {
        if (!HasSevenZipLibrary())
            return;

        var first = CopyVolumeSet("vol-01-7z-split.001", "one.7z");
        var format = ArchiveEngineFactory.ResolveReaderFormat(first);
        var output = Path.Combine(_work, "out-payload.bin");

        await ArchiveEntryExtractor.ExtractEntryAsync(first, PayloadName, output, format);

        Assert.True(File.Exists(output));
        Assert.Equal(PayloadSize, new FileInfo(output).Length);
    }

    [Fact]
    public async Task SplitZip_SingleEntryThroughSevenZipEngine_ExtractsFromVolumeSet()
    {
        if (!HasSevenZipLibrary())
            return;

        // 载荷是 ZIP，但读取器仍是 7z.dll：SharpCompress 读不了多卷
        var first = CopyVolumeSet("vol-03.zip.001", "split.zip");
        var format = ArchiveEngineFactory.ResolveReaderFormat(first);
        Assert.Equal(ArchiveFormat.SevenZip, format);

        var head = await ArchiveEntryExtractor.ExtractHeadAsync(first, EntryName, 128, format);

        Assert.NotEmpty(head);
    }
}
