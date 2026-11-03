using System.Text;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using SharpCompress.Common;
using SharpCompress.Writers.Tar;
using Xunit;

namespace MantisZip.Tests;

/// <summary>
/// TAR 条目名的 auto 检测：TAR 没有 ZIP 的 UTF-8 标志位（bit 11），
/// 所以「先按 UTF-8 解、出现替换字符再换系统 ANSI 重试一次」是 auto 模式下唯一的可用信号。
/// </summary>
public sealed class TarNameEncodingAutoTests
{
    private const int GbkCodePage = 936;

    [Fact]
    public async Task ListEntries_AutoMode_FallsBackToAnsiForGbkNames()
    {
        // 只有在「系统 ANSI = 936」的机器上这条才成立（其它区域设置下 GBK 字节按 ANSI 解仍会乱码）
        if (NameEncodingCatalog.ResolveSystemEncoding().CodePage != GbkCodePage)
            return;

        var archivePath = Path.Combine(Path.GetTempPath(), $"mantiszip_tar_gbk_{Guid.NewGuid():N}.tar");
        try
        {
            CreateTar(archivePath, Encoding.GetEncoding(GbkCodePage), "中文文件.txt", "测试二.txt");

            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            Assert.NotNull(engine);

            string? resolved = null;
            var items = await engine!.ListEntriesAsync(
                archivePath,
                password: null,
                cancellationToken: CancellationToken.None,
                fileNameEncoding: null,
                onEncodingResolved: key => resolved = key);

            Assert.Contains(items, item => item.Name == "中文文件.txt");
            Assert.Contains(items, item => item.Name == "测试二.txt");
            // 回传的必须是重试后真正生效的编码，而不是最初的 UTF-8
            Assert.Equal(NameEncodingCatalog.Describe(NameEncodingCatalog.ResolveSystemEncoding()), resolved);
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
        }
    }

    [Fact]
    public async Task ListEntries_AutoMode_KeepsUtf8ForUtf8Names()
    {
        var archivePath = Path.Combine(Path.GetTempPath(), $"mantiszip_tar_utf8_{Guid.NewGuid():N}.tar");
        try
        {
            CreateTar(archivePath, Encoding.UTF8, "中文文件.txt", "plain.txt");

            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            Assert.NotNull(engine);

            string? resolved = null;
            var items = await engine!.ListEntriesAsync(
                archivePath,
                password: null,
                cancellationToken: CancellationToken.None,
                fileNameEncoding: null,
                onEncodingResolved: key => resolved = key);

            Assert.Contains(items, item => item.Name == "中文文件.txt");
            Assert.Equal(NameEncodingCatalog.Describe(Encoding.UTF8), resolved);
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
        }
    }

    private static void CreateTar(string path, Encoding encoding, params string[] entryNames)
    {
        using var stream = File.Create(path);
        using var writer = new TarWriter(stream, new TarWriterOptions(CompressionType.None, finalizeArchiveOnClose: true)
        {
            ArchiveEncoding = new ArchiveEncoding { Default = encoding },
            LeaveStreamOpen = false,
        });

        foreach (var name in entryNames)
        {
            using var payload = new MemoryStream(Encoding.UTF8.GetBytes("content-of " + name));
            writer.Write(name, payload, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
    }
}
