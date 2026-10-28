using System.IO.Compression;
using System.Text;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 预览取数层（内存优先）回归测试：T2 按类型分流、T4 临时文件命名与清理策略、
/// T5 head LRU 缓存。
///
/// <para>
/// 每个用例用 <see cref="Guid.NewGuid"/> 造独立临时目录（不学 KI-004 的固定目录写法），
/// 用例结束在 <c>finally</c> 里删除；静态属性（<see cref="PreviewService.MaxTextPreviewBytes"/>）
/// 与 head 缓存都在 <c>finally</c> 里复位，避免与其他用例串扰。
/// </para>
/// </summary>
public class PreviewServiceContentTests
{
    /// <summary>任意小的 PNG 样例字节（用例只断言落盘与长度，不解码）。</summary>
    private static readonly byte[] TinyPng = new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    };

    private const string TextPayload = "hello preview";

    /// <summary>文本类条目（Text）→ 走内存，不产生临时文件。</summary>
    [Fact]
    public async Task LoadPreviewContentAsync_TextEntry_ReturnsBytesWithoutTempPath()
    {
        var originalMaxText = PreviewService.MaxTextPreviewBytes;
        var dir = NewTempDir();
        try
        {
            PreviewService.ResetHeadCacheForTests();

            var zip = CreateZip(dir, ("a.txt", Encoding.UTF8.GetBytes(TextPayload)));
            var entry = NewEntry("a.txt", Encoding.UTF8.GetByteCount(TextPayload));

            var content = await PreviewService.LoadPreviewContentAsync(
                zip, entry, ArchiveFormat.Zip, PreviewType.Text, ct: TestContext.Current.CancellationToken);

            Assert.NotNull(content);
            var result = content!;
            Assert.NotNull(result.Bytes);
            Assert.Null(result.TempPath);
            Assert.Equal(TextPayload, Encoding.UTF8.GetString(result.Bytes!));
            Assert.Equal(entry.Size, result.TotalSize);
            Assert.False(result.IsPartial); // 本批不做切片，恒 false
        }
        finally
        {
            PreviewService.MaxTextPreviewBytes = originalMaxText;
            PreviewService.ResetHeadCacheForTests();
            DeleteDir(dir);
        }
    }

    /// <summary>
    /// 图片类条目（Image）→ 内存优先：小图直接返回字节，不产生临时文件。
    /// （切片 2 起图片改走 <c>ExtractEntryToMemoryAsync</c>，落盘仅作回落。）
    /// </summary>
    [Fact]
    public async Task LoadPreviewContentAsync_ImageEntry_ReturnsBytesWithoutTempPath()
    {
        var originalMaxText = PreviewService.MaxTextPreviewBytes;
        var dir = NewTempDir();
        string? tempPath = null;
        try
        {
            PreviewService.ResetHeadCacheForTests();

            var zip = CreateZip(dir, ("a.png", TinyPng));
            var entry = NewEntry("a.png", TinyPng.Length);

            var content = await PreviewService.LoadPreviewContentAsync(
                zip, entry, ArchiveFormat.Zip, PreviewType.Image, ct: TestContext.Current.CancellationToken);

            Assert.NotNull(content);
            var result = content!;
            tempPath = result.TempPath;
            Assert.NotNull(result.Bytes);
            Assert.Equal(TinyPng, result.Bytes);
            Assert.Null(result.TempPath);
            Assert.Equal(entry.Size, result.TotalSize);
            Assert.False(result.IsPartial);
        }
        finally
        {
            PreviewService.MaxTextPreviewBytes = originalMaxText;
            PreviewService.ResetHeadCacheForTests();
            TryDeleteFile(tempPath);
            DeleteDir(dir);
        }
    }

    /// <summary>
    /// 需要真实文件路径的图片（.ico / .gif / .apng / .svg / .tga / .avif）→ 不走内存，
    /// 直接落盘返回 TempPath（解码器只有 path 版）。
    /// </summary>
    [Theory]
    [InlineData("a.ico")]
    [InlineData("a.gif")]
    [InlineData("a.svg")]
    public async Task LoadPreviewContentAsync_RealPathOnlyImageEntry_ReturnsTempPath(string name)
    {
        var dir = NewTempDir();
        string? tempPath = null;
        try
        {
            PreviewService.ResetHeadCacheForTests();

            var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var zip = CreateZip(dir, (name, payload));
            var entry = NewEntry(name, payload.Length);

            var content = await PreviewService.LoadPreviewContentAsync(
                zip, entry, ArchiveFormat.Zip, PreviewType.Image, ct: TestContext.Current.CancellationToken);

            Assert.NotNull(content);
            var result = content!;
            tempPath = result.TempPath;
            Assert.Null(result.Bytes);
            Assert.NotNull(result.TempPath);
            Assert.True(File.Exists(result.TempPath!));
            Assert.Equal(payload, await File.ReadAllBytesAsync(result.TempPath!, TestContext.Current.CancellationToken));
            Assert.Equal(entry.Size, result.TotalSize);
        }
        finally
        {
            PreviewService.ResetHeadCacheForTests();
            TryDeleteFile(tempPath);
            DeleteDir(dir);
        }
    }

    /// <summary>PDF 条目（Pdf）→ 内存优先：整份字节返回，不落盘。</summary>
    [Fact]
    public async Task LoadPreviewContentAsync_PdfEntry_ReturnsBytesWithoutTempPath()
    {
        var dir = NewTempDir();
        string? tempPath = null;
        try
        {
            PreviewService.ResetHeadCacheForTests();

            var payload = Encoding.ASCII.GetBytes("%PDF-1.4\n%fake pdf payload for content test\n");
            var zip = CreateZip(dir, ("doc.pdf", payload));
            var entry = NewEntry("doc.pdf", payload.Length);

            var content = await PreviewService.LoadPreviewContentAsync(
                zip, entry, ArchiveFormat.Zip, PreviewType.Pdf, ct: TestContext.Current.CancellationToken);

            Assert.NotNull(content);
            var result = content!;
            tempPath = result.TempPath;
            Assert.NotNull(result.Bytes);
            Assert.Equal(payload, result.Bytes);
            Assert.Null(result.TempPath);
        }
        finally
        {
            PreviewService.ResetHeadCacheForTests();
            TryDeleteFile(tempPath);
            DeleteDir(dir);
        }
    }

    /// <summary>同一 (包, 条目, size, mtime) 连续取两次文本 → 第二次命中 LRU，缓存条数仍为 1。</summary>
    [Fact]
    public async Task LoadPreviewContentAsync_SameTextEntryTwice_HitsHeadCache()
    {
        var originalMaxText = PreviewService.MaxTextPreviewBytes;
        var dir = NewTempDir();
        try
        {
            PreviewService.ResetHeadCacheForTests();
            Assert.Equal(0, PreviewService.HeadCacheCountForTests);

            var zip = CreateZip(dir, ("a.txt", Encoding.UTF8.GetBytes(TextPayload)));
            var entry = NewEntry("a.txt", Encoding.UTF8.GetByteCount(TextPayload));

            var first = await PreviewService.LoadPreviewContentAsync(
                zip, entry, ArchiveFormat.Zip, PreviewType.Text, ct: TestContext.Current.CancellationToken);
            Assert.NotNull(first?.Bytes);
            Assert.Equal(1, PreviewService.HeadCacheCountForTests);

            var second = await PreviewService.LoadPreviewContentAsync(
                zip, entry, ArchiveFormat.Zip, PreviewType.Text, ct: TestContext.Current.CancellationToken);
            Assert.NotNull(second?.Bytes);
            Assert.Null(second!.TempPath);
            Assert.Equal(TextPayload, Encoding.UTF8.GetString(second!.Bytes!));

            // 命中缓存：没有新增缓存条目（否则说明又解压了一次）
            Assert.Equal(1, PreviewService.HeadCacheCountForTests);
        }
        finally
        {
            PreviewService.MaxTextPreviewBytes = originalMaxText;
            PreviewService.ResetHeadCacheForTests();
            DeleteDir(dir);
        }
    }

    /// <summary>另一个压缩包（不同路径 + 同条目名 + 不同大小）→ 缓存 miss，条数变 2。</summary>
    [Fact]
    public async Task LoadPreviewContentAsync_DifferentArchive_MissesHeadCache()
    {
        var originalMaxText = PreviewService.MaxTextPreviewBytes;
        var dir = NewTempDir();
        try
        {
            PreviewService.ResetHeadCacheForTests();

            var zip1 = CreateZip(dir, ("a.txt", Encoding.UTF8.GetBytes(TextPayload)));
            var zip2 = CreateZip(
                Path.Combine(dir, "second"),
                ("a.txt", Encoding.UTF8.GetBytes("changed content, different length")));

            var entry1 = NewEntry("a.txt", Encoding.UTF8.GetByteCount(TextPayload));
            var entry2 = NewEntry("a.txt", Encoding.UTF8.GetByteCount("changed content, different length"));

            await PreviewService.LoadPreviewContentAsync(zip1, entry1, ArchiveFormat.Zip, PreviewType.Text, ct: TestContext.Current.CancellationToken);
            Assert.Equal(1, PreviewService.HeadCacheCountForTests);

            var second = await PreviewService.LoadPreviewContentAsync(
                zip2, entry2, ArchiveFormat.Zip, PreviewType.Text, ct: TestContext.Current.CancellationToken);

            Assert.NotNull(second?.Bytes);
            Assert.Equal("changed content, different length", Encoding.UTF8.GetString(second!.Bytes!));
            Assert.Equal(2, PreviewService.HeadCacheCountForTests);
        }
        finally
        {
            PreviewService.MaxTextPreviewBytes = originalMaxText;
            PreviewService.ResetHeadCacheForTests();
            DeleteDir(dir);
        }
    }

    /// <summary>
    /// T4：临时文件名按条目稳定哈希生成（两个条目不同名），
    /// 且不再「每次预览前清空整个 Preview 目录」（预置的哨兵文件必须还在）。
    /// A→B→A 时同一路径命中，不删当前要返回的那个文件。
    /// </summary>
    [Fact]
    public async Task ExtractToTempAsync_TwoEntries_NamesDifferAndDirectoryIsNotWiped()
    {
        var dir = NewTempDir();
        var previewDir = Path.Combine(AppSettings.GetTempDir(), "Preview");
        Directory.CreateDirectory(previewDir);
        var sentinel = Path.Combine(previewDir, $"sentinel_{Guid.NewGuid():N}.bin");
        File.WriteAllText(sentinel, "keep me");

        string? txtTemp = null, pngTemp = null, txtTempAgain = null;
        try
        {
            var zip = CreateZip(
                dir,
                ("a.txt", Encoding.UTF8.GetBytes(TextPayload)),
                ("b.png", TinyPng));

            txtTemp = await PreviewService.ExtractToTempAsync(
                zip, NewEntry("a.txt", Encoding.UTF8.GetByteCount(TextPayload)), ArchiveFormat.Zip, ct: TestContext.Current.CancellationToken);
            pngTemp = await PreviewService.ExtractToTempAsync(
                zip, NewEntry("b.png", TinyPng.Length), ArchiveFormat.Zip, ct: TestContext.Current.CancellationToken);

            Assert.NotNull(txtTemp);
            Assert.NotNull(pngTemp);
            Assert.NotEqual(txtTemp, pngTemp);
            Assert.StartsWith("preview_", Path.GetFileName(txtTemp!));
            Assert.StartsWith("preview_", Path.GetFileName(pngTemp!));
            Assert.EndsWith(".txt", txtTemp!, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".png", pngTemp!, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(pngTemp!));

            // 上一条（a.txt）按策略被删；整个目录**没有**被清空
            Assert.False(File.Exists(txtTemp!));
            Assert.True(File.Exists(sentinel));

            // A → B → A：路径稳定命中，当前要返回的文件不能被自己删掉
            txtTempAgain = await PreviewService.ExtractToTempAsync(
                zip, NewEntry("a.txt", Encoding.UTF8.GetByteCount(TextPayload)), ArchiveFormat.Zip, ct: TestContext.Current.CancellationToken);
            Assert.Equal(txtTemp, txtTempAgain);
            Assert.True(File.Exists(txtTempAgain!));
            Assert.False(File.Exists(pngTemp!));
            Assert.True(File.Exists(sentinel));
        }
        finally
        {
            TryDeleteFile(txtTemp);
            TryDeleteFile(pngTemp);
            TryDeleteFile(txtTempAgain);
            TryDeleteFile(sentinel);
            DeleteDir(dir);
        }
    }

    /// <summary>smoke：<see cref="PreviewService.ExtractToTempAsync"/> 仍返回可用文件路径。</summary>
    [Fact]
    public async Task ExtractToTempAsync_SingleEntry_WritesFileContent()
    {
        var dir = NewTempDir();
        string? tempPath = null;
        try
        {
            var payload = Encoding.UTF8.GetBytes("extract smoke");
            var zip = CreateZip(dir, ("smoke.txt", payload));

            tempPath = await PreviewService.ExtractToTempAsync(
                zip, NewEntry("smoke.txt", payload.Length), ArchiveFormat.Zip, ct: TestContext.Current.CancellationToken);

            Assert.NotNull(tempPath);
            Assert.True(File.Exists(tempPath!));
            Assert.Equal(payload, await File.ReadAllBytesAsync(tempPath!, TestContext.Current.CancellationToken));
        }
        finally
        {
            TryDeleteFile(tempPath);
            DeleteDir(dir);
        }
    }

    // ── 测试辅助 ──

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mantiszip_prv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static ArchiveItemModel NewEntry(string fullPath, long size) => new()
    {
        Name = Path.GetFileName(fullPath),
        FullPath = fullPath,
        Size = size,
    };

    private static string CreateZip(string dir, params (string Name, byte[] Data)[] entries)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"pack_{Guid.NewGuid():N}.zip");
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, data) in entries)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
            using var stream = entry.Open();
            stream.Write(data, 0, data.Length);
        }
        return path;
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort：句柄未释放时留给系统临时目录清理
        }
    }

    private static void DeleteDir(string? dir)
    {
        if (string.IsNullOrEmpty(dir))
            return;

        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
