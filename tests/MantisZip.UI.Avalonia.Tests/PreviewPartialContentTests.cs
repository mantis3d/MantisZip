using System.IO.Compression;
using System.Text;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 切片 4（T10）部分预览回归测试：文本类超过 <see cref="PreviewService.MaxTextPreviewBytes"/> 时
/// 不再一律拒绝，而是返回 head + <see cref="PreviewContent.IsPartial"/> 标记；
/// 超过 <see cref="PreviewService.MaxPartialPreviewBytes"/> 硬上限时连部分预览都不做、且不落盘。
///
/// <para>
/// 写法跟随 <c>PreviewServiceContentTests</c>：<see cref="Guid.NewGuid"/> 造独立临时目录、
/// <c>try/finally</c> 删除、静态属性改完复位、所有带 <see cref="CancellationToken"/> 的调用
/// 都显式传 <c>TestContext.Current.CancellationToken</c>（避免 xUnit1051 警告）。
/// </para>
/// </summary>
public class PreviewPartialContentTests
{
    private const int OneMb = 1024 * 1024;

    /// <summary>任意小的 PNG 样例字节（用例只断言取数路径，不解码）。</summary>
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

    /// <summary>
    /// 2MB 文本条目（Text / Csv / Markdown）+ 默认 1MB <see cref="PreviewService.MaxTextPreviewBytes"/>
    /// → 返回 head 字节并标记 <see cref="PreviewContent.IsPartial"/>，<see cref="PreviewContent.TotalSize"/>
    /// 仍是条目原始大小。
    /// </summary>
    [Theory]
    [InlineData(PreviewType.Text)]
    [InlineData(PreviewType.Csv)]
    [InlineData(PreviewType.Markdown)]
    public async Task LoadPreviewContentAsync_LargeTextEntry_ReturnsPartialHead(PreviewType previewType)
    {
        var originalMaxText = PreviewService.MaxTextPreviewBytes;
        var dir = NewTempDir();
        try
        {
            PreviewService.ResetHeadCacheForTests();
            PreviewService.MaxTextPreviewBytes = OneMb;

            var payload = LargeTextPayload(2 * OneMb);
            var zip = CreateZip(dir, ("big.log", payload));
            var entry = NewEntry("big.log", payload.Length);

            var content = await PreviewService.LoadPreviewContentAsync(
                zip, entry, ArchiveFormat.Zip, previewType, ct: TestContext.Current.CancellationToken);

            Assert.NotNull(content);
            var result = content!;
            Assert.NotNull(result.Bytes);
            Assert.Null(result.TempPath);
            Assert.False(result.ExceedsPartialLimit);
            Assert.True(result.IsPartial);
            Assert.Equal(entry.Size, result.TotalSize);
            Assert.Equal(OneMb, result.Bytes!.Length);
            Assert.Equal((byte)'a', result.Bytes[0]);
        }
        finally
        {
            PreviewService.MaxTextPreviewBytes = originalMaxText;
            PreviewService.ResetHeadCacheForTests();
            DeleteDir(dir);
        }
    }

    /// <summary>小于 1MB 的文本条目 → 完整内容，<see cref="PreviewContent.IsPartial"/> 为 false。</summary>
    [Fact]
    public async Task LoadPreviewContentAsync_SmallTextEntry_IsNotPartial()
    {
        var originalMaxText = PreviewService.MaxTextPreviewBytes;
        var dir = NewTempDir();
        try
        {
            PreviewService.ResetHeadCacheForTests();
            PreviewService.MaxTextPreviewBytes = OneMb;

            var payload = Encoding.UTF8.GetBytes("small enough to be shown in full");
            var zip = CreateZip(dir, ("small.txt", payload));
            var entry = NewEntry("small.txt", payload.Length);

            var content = await PreviewService.LoadPreviewContentAsync(
                zip, entry, ArchiveFormat.Zip, PreviewType.Text, ct: TestContext.Current.CancellationToken);

            Assert.NotNull(content);
            var result = content!;
            Assert.NotNull(result.Bytes);
            Assert.False(result.IsPartial);
            Assert.False(result.ExceedsPartialLimit);
            Assert.Equal(entry.Size, result.TotalSize);
            Assert.Equal(payload.Length, result.Bytes!.Length);
            Assert.Equal(payload, result.Bytes);
        }
        finally
        {
            PreviewService.MaxTextPreviewBytes = originalMaxText;
            PreviewService.ResetHeadCacheForTests();
            DeleteDir(dir);
        }
    }

    /// <summary>
    /// 超过硬上限（把 <see cref="PreviewService.MaxPartialPreviewBytes"/> 临时改成 1KB，条目 4KB）
    /// → 只回 <see cref="PreviewContent.ExceedsPartialLimit"/> 信号，<c>Bytes</c> 与 <c>TempPath</c>
    /// 都是 null，且 Preview 临时目录里**没有多出**本用例的落盘文件（证明没有走整条目解压）。
    /// </summary>
    [Fact]
    public async Task LoadPreviewContentAsync_TextEntryBeyondPartialLimit_SignalsWithoutTempPath()
    {
        var originalMaxText = PreviewService.MaxTextPreviewBytes;
        var originalMaxPartial = PreviewService.MaxPartialPreviewBytes;
        var dir = NewTempDir();
        var previewDir = Path.Combine(AppSettings.GetTempDir(), "Preview");
        int logFilesBefore = CountLogFilesInPreviewDir(previewDir);
        try
        {
            PreviewService.ResetHeadCacheForTests();
            PreviewService.MaxTextPreviewBytes = OneMb;
            PreviewService.MaxPartialPreviewBytes = 1024;

            // 4KB > 1KB 硬上限（但远小于默认 15MB 的 MaxPreviewFileSize，确保拦截来自本闸门）
            var payload = LargeTextPayload(4 * 1024);
            var zip = CreateZip(dir, ("huge.log", payload));
            var entry = NewEntry("huge.log", payload.Length);

            var content = await PreviewService.LoadPreviewContentAsync(
                zip, entry, ArchiveFormat.Zip, PreviewType.Text, ct: TestContext.Current.CancellationToken);

            Assert.NotNull(content);
            var result = content!;
            Assert.True(result.ExceedsPartialLimit);
            Assert.Null(result.Bytes);
            Assert.Null(result.TempPath);
            Assert.Equal(entry.Size, result.TotalSize);
            Assert.Equal(logFilesBefore, CountLogFilesInPreviewDir(previewDir));
        }
        finally
        {
            PreviewService.MaxTextPreviewBytes = originalMaxText;
            PreviewService.MaxPartialPreviewBytes = originalMaxPartial;
            PreviewService.ResetHeadCacheForTests();
            DeleteDir(dir);
        }
    }

    /// <summary>
    /// <see cref="PreviewService.CanPreviewPartially"/> 真值表：
    /// 文本三类为 true，Html / 图片 / PDF / Office 三族 / 不支持一律 false（Office 是切片 3 的接口位）。
    /// </summary>
    [Theory]
    [InlineData(PreviewType.Text, true)]
    [InlineData(PreviewType.Csv, true)]
    [InlineData(PreviewType.Markdown, true)]
    [InlineData(PreviewType.Html, false)]
    [InlineData(PreviewType.Image, false)]
    [InlineData(PreviewType.Pdf, false)]
    [InlineData(PreviewType.Docx, false)]
    [InlineData(PreviewType.Xlsx, false)]
    [InlineData(PreviewType.Pptx, false)]
    [InlineData(PreviewType.Office, false)]
    [InlineData(PreviewType.Unsupported, false)]
    public void CanPreviewPartially_ReturnsExpectedValue(PreviewType previewType, bool expected) =>
        Assert.Equal(expected, PreviewService.CanPreviewPartially(previewType));

    /// <summary>
    /// 硬上限不误伤图片：<see cref="PreviewService.MaxPartialPreviewBytes"/> 调成 1KB 时，
    /// 图片仍走 <see cref="PreviewService.MaxPreviewFileSize"/> 那条闸门并返回内存字节
    /// （部分预览闸门只作用于文本类）。
    /// </summary>
    [Fact]
    public async Task LoadPreviewContentAsync_ImageEntry_IgnoresPartialPreviewLimit()
    {
        var originalMaxPartial = PreviewService.MaxPartialPreviewBytes;
        var dir = NewTempDir();
        string? tempPath = null;
        try
        {
            PreviewService.ResetHeadCacheForTests();
            PreviewService.MaxPartialPreviewBytes = 1024;

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
            Assert.False(result.ExceedsPartialLimit);
            Assert.False(result.IsPartial);
        }
        finally
        {
            PreviewService.MaxPartialPreviewBytes = originalMaxPartial;
            PreviewService.ResetHeadCacheForTests();
            TryDeleteFile(tempPath);
            DeleteDir(dir);
        }
    }

    // ── 测试辅助 ──

    private static byte[] LargeTextPayload(int length)
    {
        var payload = new byte[length];
        Array.Fill(payload, (byte)'a');
        return payload;
    }

    private static int CountLogFilesInPreviewDir(string previewDir)
    {
        try
        {
            return Directory.Exists(previewDir)
                ? Directory.GetFiles(previewDir, "preview_*.log").Length
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mantiszip_prv_partial_{Guid.NewGuid():N}");
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
