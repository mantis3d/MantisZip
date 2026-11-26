using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// <see cref="FileScanner.CollectFiles"/> 的「包内根目录」四态回归测试。
/// <para>
/// 这一层决定的是**包内条目名**（相对路径），Zip / TarGz / 7z / 预览树都读它：
/// 一旦四态在这里走偏，压缩包里就会出现「去根没去干净」或「套根套成两层」。
/// 用例各自建独立 Guid 临时目录，可并行。
/// </para>
/// </summary>
public sealed class FileScannerRootModeTests : IDisposable
{
    private readonly string _tempRoot;

    public FileScannerRootModeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MantisZipRootMode_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响断言结论
        }
    }

    // ── 夹具 ──

    private string CreateSourceDir(string name = "7zip")
    {
        var dir = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "a");
        var inner = Path.Combine(dir, "inner");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, "b.txt"), "b");
        return dir;
    }

    private string CreateLooseFile(string name = "solo.txt")
    {
        var file = Path.Combine(_tempRoot, name);
        File.WriteAllText(file, "s");
        return file;
    }

    /// <summary>收集并排序（'/' 分隔、忽略大小写）——集合语义比较，顺序无关。</summary>
    private static string[] Collect(string[] sources, RootDirectoryMode mode, string? wrapRoot = null)
    {
        var (files, _) = FileScanner.CollectFiles(sources, null, CancellationToken.None, null, mode, wrapRoot);
        return files
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // ── ① 原样：与引入本功能前逐字节一致 ──

    [Fact]
    public void Original_SingleDirectory_KeepsDirectoryAsTopEntry()
    {
        var dir = CreateSourceDir();
        Assert.Equal(new[] { "7zip/a.txt", "7zip/inner/b.txt" }, Collect([dir], RootDirectoryMode.Original));
    }

    [Fact]
    public void Original_SingleFile_KeepsFileName()
    {
        var file = CreateLooseFile();
        Assert.Equal(new[] { "solo.txt" }, Collect([file], RootDirectoryMode.Original));
    }

    // ── ② 智能去除唯一根：只剥那一个目录源 ──

    [Fact]
    public void SmartStrip_SingleDirectory_DropsThatDirectoryLayer()
    {
        var dir = CreateSourceDir();
        Assert.Equal(new[] { "a.txt", "inner/b.txt" }, Collect([dir], RootDirectoryMode.SmartStrip));
    }

    [Fact]
    public void SmartStrip_TwoDirectories_LeavesBothLayers()
    {
        var a = CreateSourceDir("one");
        var b = CreateSourceDir("two");
        Assert.Equal(
            new[] { "one/a.txt", "one/inner/b.txt", "two/a.txt", "two/inner/b.txt" },
            Collect([a, b], RootDirectoryMode.SmartStrip));
    }

    [Fact]
    public void SmartStrip_SingleFile_DoesNotStripItsName()
    {
        var file = CreateLooseFile();
        Assert.Equal(new[] { "solo.txt" }, Collect([file], RootDirectoryMode.SmartStrip));
    }

    // ── ③ 智能添加唯一根：已有唯一根就不动 ──

    [Fact]
    public void SmartWrap_SingleDirectory_KeepsExistingRootUnchanged()
    {
        var dir = CreateSourceDir();
        // 已经是唯一根（7zip/）→ 智能加不动，不产生 wrap/7zip/a.txt
        Assert.Equal(new[] { "7zip/a.txt", "7zip/inner/b.txt" }, Collect([dir], RootDirectoryMode.SmartWrap, "wrap"));
    }

    [Fact]
    public void SmartWrap_TwoDirectories_AddsOneSharedRoot()
    {
        var a = CreateSourceDir("one");
        var b = CreateSourceDir("two");
        Assert.Equal(
            new[] { "wrap/one/a.txt", "wrap/one/inner/b.txt", "wrap/two/a.txt", "wrap/two/inner/b.txt" },
            Collect([a, b], RootDirectoryMode.SmartWrap, "wrap"));
    }

    [Fact]
    public void SmartWrap_SingleFile_AddsRoot()
    {
        var file = CreateLooseFile();
        Assert.Equal(new[] { "wrap/solo.txt" }, Collect([file], RootDirectoryMode.SmartWrap, "wrap"));
    }

    // ── ④ 始终添加唯一根：无条件加一层 ──

    [Fact]
    public void AlwaysWrap_SingleDirectory_ProducesTwoLayers()
    {
        var dir = CreateSourceDir();
        // 这正是 ③ 与 ④ 的唯一区别：本来已是唯一根，仍然再加一层
        Assert.Equal(new[] { "wrap/7zip/a.txt", "wrap/7zip/inner/b.txt" }, Collect([dir], RootDirectoryMode.AlwaysWrap, "wrap"));
    }

    [Fact]
    public void AlwaysWrap_SingleFile_AddsRoot()
    {
        var file = CreateLooseFile();
        Assert.Equal(new[] { "wrap/solo.txt" }, Collect([file], RootDirectoryMode.AlwaysWrap, "wrap"));
    }

    // ── 根名回落与净化 ──

    [Fact]
    public void Wrap_EmptyRootName_FallsBackToArchiveDefault()
    {
        var a = CreateSourceDir("one");
        var b = CreateSourceDir("two");
        // FileScanner 拿不到压缩包路径，回落名固定为 "archive"
        Assert.Equal(
            new[] { "archive/one/a.txt", "archive/one/inner/b.txt", "archive/two/a.txt", "archive/two/inner/b.txt" },
            Collect([a, b], RootDirectoryMode.SmartWrap, wrapRoot: null));
    }

    [Fact]
    public void Wrap_InvalidRootName_IsSanitized()
    {
        var a = CreateSourceDir("one");
        var b = CreateSourceDir("two");
        var names = Collect([a, b], RootDirectoryMode.SmartWrap, "a:b*c");
        Assert.All(names, n => Assert.StartsWith("a_b_c/", n, StringComparison.Ordinal));
    }

    [Fact]
    public void Wrap_KeepsFileCountAndTotalBytesUnchanged()
    {
        var dir = CreateSourceDir();
        var (original, originalBytes) = FileScanner.CollectFiles(
            [dir], null, CancellationToken.None, null, RootDirectoryMode.Original);
        var (wrapped, wrappedBytes) = FileScanner.CollectFiles(
            [dir], null, CancellationToken.None, null, RootDirectoryMode.AlwaysWrap, "wrap");

        Assert.Equal(original.Count, wrapped.Count);
        Assert.Equal(originalBytes, wrappedBytes);
        // 加根只改名字，不改任何文件的落点集合
        Assert.All(wrapped, w => Assert.StartsWith("wrap/", w.RelativePath));
    }
}
