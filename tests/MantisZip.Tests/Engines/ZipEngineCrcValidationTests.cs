using System.IO.Compression;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Models;
using Xunit;

namespace MantisZip.Tests.Engines;

/// <summary>
/// ZIP CRC 校验回归测试。
/// 背景：SharpCompress 的 <c>OpenEntryStream()</c> 返回裸流、不校验 CRC，
/// 引擎曾因此在测试/解压路径静默接受“数据被改动但仍可解码”的损坏包。
/// 修复后走官方 <c>WriteTo + ExtractionOptions.CheckCrc=true</c> 路径应能检出并逐条目上报失败。
/// </summary>
public class ZipEngineCrcValidationTests
{
    private static string NewDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>创建一个含单个 stored（不压缩）条目的 ZIP —— 数据字节被改动后仍可解码，只有 CRC 会不匹配。</summary>
    private static string CreateStoredZip(string dir, string entryName, byte[] content)
    {
        var path = Path.Combine(dir, "src.zip");
        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
            using var s = e.Open();
            s.Write(content, 0, content.Length);
        }
        return path;
    }

    /// <summary>翻转第一个本地文件头之后的数据字节（定位 30 + nameLen + extraLen）。</summary>
    private static void FlipFirstEntryDataByte(string zipPath)
    {
        var b = File.ReadAllBytes(zipPath);
        int nameLen = BitConverter.ToUInt16(b, 26);
        int extraLen = BitConverter.ToUInt16(b, 28);
        int dataOffset = 30 + nameLen + extraLen;
        b[dataOffset + 3] ^= 0xFF;
        File.WriteAllBytes(zipPath, b);
    }

    private static byte[] Content()
    {
        var c = new byte[256];
        for (int i = 0; i < c.Length; i++) c[i] = (byte)(i % 256);
        return c;
    }

    private sealed class ProgressCollector : IProgress<ArchiveProgress>
    {
        public readonly List<ArchiveProgress> Events = new();
        public void Report(ArchiveProgress value) { lock (Events) Events.Add(value); }
    }

    [Fact]
    public async Task TestArchiveAsync_CrcCorruptStoredEntry_ReturnsFalse()
    {
        var dir = NewDir();
        try
        {
            var zip = CreateStoredZip(dir, "data.bin", Content());
            FlipFirstEntryDataByte(zip);

            var engine = new ZipEngine();
            var ok = await engine.TestArchiveAsync(zip);

            Assert.False(ok, "CRC 损坏的 stored 条目应使测试失败");
        }
        finally { TryCleanup(dir); }
    }

    [Fact]
    public async Task TestArchiveAsync_ValidStoredEntry_ReturnsTrue()
    {
        var dir = NewDir();
        try
        {
            var zip = CreateStoredZip(dir, "data.bin", Content());

            var engine = new ZipEngine();
            Assert.True(await engine.TestArchiveAsync(zip));
        }
        finally { TryCleanup(dir); }
    }

    [Fact]
    public async Task ExtractAsync_CrcCorruptEntry_ReportsFailedAndDeletesCorruptOutput()
    {
        var dir = NewDir();
        try
        {
            var zip = CreateStoredZip(dir, "data.bin", Content());
            FlipFirstEntryDataByte(zip);
            var dest = Path.Combine(dir, "out");
            Directory.CreateDirectory(dest);

            var engine = new ZipEngine();
            var progress = new ProgressCollector();
            var result = await engine.ExtractAsync(zip, dest, password: null, progress: progress,
                options: new ArchiveOptions { ParallelExtractDegree = 1 });

            Assert.Equal(1, result.FailedEntries);
            Assert.False(File.Exists(Path.Combine(dest, "data.bin")), "损坏的输出文件应被删除");

            var failed = progress.Events.FirstOrDefault(e => e.EntryStatus == ArchiveEntryStatus.Failed);
            Assert.NotNull(failed);
            Assert.Equal(EntryFailureReason.CrcMismatch, failed!.EntryFailureReason);
        }
        finally { TryCleanup(dir); }
    }

    [Fact]
    public async Task ExtractAsync_ValidStoredEntry_ExtractsCorrectBytes()
    {
        var dir = NewDir();
        try
        {
            var content = Content();
            var zip = CreateStoredZip(dir, "data.bin", content);
            var dest = Path.Combine(dir, "out");
            Directory.CreateDirectory(dest);

            var engine = new ZipEngine();
            var result = await engine.ExtractAsync(zip, dest, password: null,
                options: new ArchiveOptions { ParallelExtractDegree = 1 });

            Assert.Equal(1, result.SucceededEntries);
            Assert.Equal(0, result.FailedEntries);
            Assert.Equal(content, File.ReadAllBytes(Path.Combine(dest, "data.bin")));
        }
        finally { TryCleanup(dir); }
    }

    private static void TryCleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }
}
