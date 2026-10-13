using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using Xunit;

namespace MantisZip.Tests.Engines;

/// <summary>
/// 回归：<see cref="ZipEngine.TestArchiveAsync"/> 逐条目失败定位（spec §12 Follow-up #6）——
/// 损坏的 deflate 条目应上报 <c>EntryStatus=Failed</c> + <c>EntryFailureReason</c>，供进度窗口
/// 「列表」与红色摘要定位到具体文件；健康包逐条目上报 <c>Completed</c> 且整体返回 true。
/// </summary>
public class ZipEngineTestArchiveLocalizationTests
{
    [Fact]
    public async Task TestArchiveAsync_CorruptEntry_ReportsFailedWithReason()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zip-tloc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, "corrupt.zip");
        try
        {
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                void Add(string n, byte[] c)
                {
                    var e = z.CreateEntry(n, CompressionLevel.Optimal);
                    using var s = e.Open();
                    s.Write(c, 0, c.Length);
                }
                Add("good.txt", Encoding.UTF8.GetBytes(new string('A', 4000)));
                var buf = new byte[30000];
                for (int i = 0; i < buf.Length; i++) buf[i] = (byte)('A' + (i % 16));
                Add("damaged.bin", buf);
            }
            ScrambleEntryData(zip, "damaged.bin");

            var reports = new List<ArchiveProgress>();
            var ok = await new ZipEngine().TestArchiveAsync(zip, null, new CaptureProgress(reports), CancellationToken.None);

            Assert.False(ok);
            var failed = Assert.Single(reports.Where(r => r.EntryStatus == ArchiveEntryStatus.Failed));
            Assert.Equal("damaged.bin", failed.EntryKey);
            Assert.NotNull(failed.EntryFailureReason);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 临时目录尽力清理 */ }
        }
    }

    [Fact]
    public async Task TestArchiveAsync_HealthyZip_ReportsCompletedAndReturnsTrue()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zip-tok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, "ok.zip");
        try
        {
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var e = z.CreateEntry("a.txt");
                using var s = e.Open();
                s.Write(new byte[] { 1, 2, 3 }, 0, 3);
            }

            var reports = new List<ArchiveProgress>();
            var ok = await new ZipEngine().TestArchiveAsync(zip, null, new CaptureProgress(reports), CancellationToken.None);

            Assert.True(ok);
            Assert.Contains(reports, r => r.EntryStatus == ArchiveEntryStatus.Completed && r.EntryKey == "a.txt");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 临时目录尽力清理 */ }
        }
    }

    /// <summary>把指定条目的压缩数据整段打乱（可压缩数据 → deflate 会真正解码失败）。</summary>
    private static void ScrambleEntryData(string zipPath, string target)
    {
        var b = File.ReadAllBytes(zipPath);
        int eocd = -1;
        for (int i = b.Length - 22; i >= 0 && i >= b.Length - 22 - 65536; i--)
            if (b[i] == 0x50 && b[i + 1] == 0x4B && b[i + 2] == 0x05 && b[i + 3] == 0x06) { eocd = i; break; }
        int cdCount = b[eocd + 10] | (b[eocd + 11] << 8);
        int cdOffset = b[eocd + 16] | (b[eocd + 17] << 8) | (b[eocd + 18] << 16) | (b[eocd + 19] << 24);
        int p = cdOffset;
        for (int n = 0; n < cdCount; n++)
        {
            int nameLen = b[p + 28] | (b[p + 29] << 8);
            int extraLen = b[p + 30] | (b[p + 31] << 8);
            int commentLen = b[p + 32] | (b[p + 33] << 8);
            int compSize = b[p + 20] | (b[p + 21] << 8) | (b[p + 22] << 16) | (b[p + 23] << 24);
            int lho = b[p + 42] | (b[p + 43] << 8) | (b[p + 44] << 16) | (b[p + 45] << 24);
            string name = Encoding.UTF8.GetString(b, p + 46, nameLen);
            if (name == target)
            {
                int lNameLen = b[lho + 26] | (b[lho + 27] << 8);
                int lExtraLen = b[lho + 28] | (b[lho + 29] << 8);
                int dataStart = lho + 30 + lNameLen + lExtraLen;
                for (int k = 0; k < compSize && dataStart + k < b.Length; k++) b[dataStart + k] ^= 0xA5;
                File.WriteAllBytes(zipPath, b);
                return;
            }
            p += 46 + nameLen + extraLen + commentLen;
        }
        throw new InvalidOperationException("target entry not found: " + target);
    }

    private sealed class CaptureProgress : IProgress<ArchiveProgress>
    {
        private readonly List<ArchiveProgress> _list;
        public CaptureProgress(List<ArchiveProgress> list) => _list = list;
        public void Report(ArchiveProgress value) { lock (_list) _list.Add(value); }
    }
}
