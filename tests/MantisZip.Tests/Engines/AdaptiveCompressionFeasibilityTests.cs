using System.Diagnostics;
using System.Reflection;
using MantisZip.Core.Utils;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers.Zip;
using SharpSevenZip;
using Xunit;

namespace MantisZip.Tests.Engines;

/// <summary>
/// 自适应压缩（per-entry 级别）可行性行为探针。
///
/// 目的：在实现前验证「属性存在」之外的真实行为，避免实现到一半发现新限制。
/// 每条探针都编码一条计划中的假设；探针失败 = 该假设被证伪。
///
/// 计划引用：.omo/plans/未开始/compression-estimator.md（自适应压缩级别章节）
/// </summary>
public class AdaptiveCompressionFeasibilityTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles.Where(File.Exists))
            try { File.Delete(f); } catch { }
    }

    private string Track(string p) { _tempFiles.Add(p); return p; }

    /// <summary>7z.dll 是否可用（Probe3/14 依赖它做第三方互操作校验）。</summary>
    private static bool SevenZipAvailable() =>
        File.Exists(MantisZip.Core.Engines.SevenZipEngine.SevenZipDllPath);

    // ── 测试数据 ──────────────────────────────────────────────────────────

    /// <summary>高度可压缩（重复模式）——文本类模拟。</summary>
    private static byte[] Compressible(int size)
    {
        var pattern = System.Text.Encoding.UTF8.GetBytes(
            "MantisZip adaptive probe payload 0123456789 abcdefghijklmnopqrstuvwxyz ");
        var buf = new byte[size];
        for (int i = 0; i < size; i++) buf[i] = pattern[i % pattern.Length];
        return buf;
    }

    /// <summary>不可压缩（随机）——JPEG/MP4 类模拟。</summary>
    private static byte[] Incompressible(int size, int seed = 42)
    {
        var buf = new byte[size];
        new Random(seed).NextBytes(buf);
        return buf;
    }

    // ── 辅助：写 ZIP（逐条目级别）─────────────────────────────────────────

    private static string WriteZip(
        string tag,
        CompressionType type,
        (string Name, byte[] Data, int? Level)[] entries,
        int archiveLevel = 5)
    {
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipAdaptiveProbe");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{Guid.NewGuid():N}_{tag}.zip");

        var writerOptions = new ZipWriterOptions(type) { CompressionLevel = archiveLevel };
        using var fs = File.Create(path);
        using var zw = new ZipWriter(fs, writerOptions);

        foreach (var (name, data, level) in entries)
        {
            var entryOptions = new ZipWriterEntryOptions { ModificationDateTime = DateTime.Now };
            if (level.HasValue) entryOptions.CompressionLevel = level.Value;
            using var src = new MemoryStream(data);
            zw.Write(name, src, entryOptions);
        }
        return path;
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 1：per-entry CompressionLevel 是否真的生效（不只是属性存在）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Probe1_PerEntryCompressionLevel_IsHonored()
    {
        var data = Compressible(256 * 1024);
        var path = Track(WriteZip("levels", CompressionType.Deflate, new[]
        {
            ("l0.bin", data, (int?)0),
            ("l5.bin", data, (int?)5),
            ("l9.bin", data, (int?)9),
        }));

        using var za = ZipArchive.OpenArchive(path, new ReaderOptions());
        var sizes = za.Entries
            .Where(e => !e.IsDirectory)
            .ToDictionary(e => e.Key!, e => (e.Size, e.CompressedSize));

        foreach (var kv in sizes)
            Console.WriteLine($"PROBE1 {kv.Key}: size={kv.Value.Size} compressed={kv.Value.CompressedSize}");

        Assert.Equal(3, sizes.Count);
        // 关键断言：级别 0 明显大于级别 5；级别 9 不大于级别 9 之外
        Assert.True(sizes["l0.bin"].CompressedSize > sizes["l5.bin"].CompressedSize * 10,
            "level 0 (Store) should be far larger than level 5 if per-entry level is honored");
        Assert.True(sizes["l9.bin"].CompressedSize <= sizes["l5.bin"].CompressedSize,
            "level 9 should not be larger than level 5");
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 2：级别 0 是否等价于 Store（不压缩）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Probe2_Level0_IsEffectivelyStored()
    {
        var data = Compressible(256 * 1024);
        var path = Track(WriteZip("store", CompressionType.Deflate, new[]
        {
            ("l0.bin", data, (int?)0),
        }));

        using var za = ZipArchive.OpenArchive(path, new ReaderOptions());
        var e = za.Entries.First(x => !x.IsDirectory);
        Console.WriteLine($"PROBE2 size={e.Size} compressed={e.CompressedSize} ratio={(double)e.CompressedSize / e.Size:F4}");

        // Store 语义：压缩后 ≈ 原始大小（Deflate stored-block 允许极小的头部开销）
        Assert.True(e.CompressedSize >= e.Size, "level 0 must not shrink data");
        Assert.True((double)e.CompressedSize / e.Size < 1.02, "level 0 overhead must be negligible");
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 3：混合级别产物能否被独立读取器（7z.dll）正常解压 —— 互操作性
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Probe3_MixedLevelZip_InteropsWith7zDll()
    {
        if (!SevenZipAvailable()) return;

        var text = Compressible(128 * 1024);
        var rand = Incompressible(128 * 1024);
        var path = Track(WriteZip("interop", CompressionType.Deflate, new[]
        {
            ("text.txt", text, (int?)9),
            ("photo.jpg", rand, (int?)0),
        }));

        // 用 7z.dll（SharpSevenZipExtractor）独立读取，证明规范合法性
        var dest = Path.Combine(Path.GetTempPath(), "MantisZipAdaptiveProbe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        try
        {
            using (var ex = new SharpSevenZipExtractor(path))
            {
                ex.ExtractArchive(dest);
            }

            var textOut = Path.Combine(dest, "text.txt");
            var randOut = Path.Combine(dest, "photo.jpg");
            Assert.True(File.Exists(textOut), "7z.dll failed to extract text.txt");
            Assert.True(File.Exists(randOut), "7z.dll failed to extract photo.jpg");
            Assert.Equal(text, await File.ReadAllBytesAsync(textOut));
            Assert.Equal(rand, await File.ReadAllBytesAsync(randOut));
            Console.WriteLine("PROBE3 mixed-level ZIP extracted by 7z.dll OK (byte-identical)");
        }
        finally
        {
            try { Directory.Delete(dest, true); } catch { }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 4/5/6：BZip2 / LZMA / ZStandard 的 per-entry 级别行为
    // ══════════════════════════════════════════════════════════════════════

    private (int Size0, int Size9, string Note) TryPerEntryOn(CompressionType type)
    {
        var data = Compressible(256 * 1024);
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipAdaptiveProbe");
        Directory.CreateDirectory(dir);
        var p0 = Path.Combine(dir, $"{Guid.NewGuid():N}_{type}_0.zip");
        var p9 = Path.Combine(dir, $"{Guid.NewGuid():N}_{type}_9.zip");
        Track(p0); Track(p9);

        void Write(string path, int level)
        {
            using var fs = File.Create(path);
            using var zw = new ZipWriter(fs, new ZipWriterOptions(type) { CompressionLevel = level });
            var eo = new ZipWriterEntryOptions { CompressionLevel = level };
            using var src = new MemoryStream(data);
            zw.Write("a.bin", src, eo);
        }

        try
        {
            Write(p0, 0);
            Write(p9, 9);

            using var za0 = ZipArchive.OpenArchive(p0, new ReaderOptions());
            using var za9 = ZipArchive.OpenArchive(p9, new ReaderOptions());
            var s0 = (int)za0.Entries.First(e => !e.IsDirectory).CompressedSize;
            var s9 = (int)za9.Entries.First(e => !e.IsDirectory).CompressedSize;
            return (s0, s9, "ok");
        }
        catch (Exception ex)
        {
            return (-1, -1, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    [Fact]
    public void Probe4_BZip2_PerEntryLevelBehavior()
    {
        var (s0, s9, note) = TryPerEntryOn(CompressionType.BZip2);
        Console.WriteLine($"PROBE4 BZip2: level0={s0} level9={s9} note={note}");
        if (note == "ok")
            Console.WriteLine($"PROBE4 BZip2 per-entry level {(s0 == s9 ? "IGNORED (levels identical)" : "HONORED")}");
    }

    [Fact]
    public void Probe5_Lzma_PerEntryLevelBehavior()
    {
        var (s0, s9, note) = TryPerEntryOn(CompressionType.LZMA);
        Console.WriteLine($"PROBE5 LZMA: level0={s0} level9={s9} note={note}");
        if (note == "ok")
            Console.WriteLine($"PROBE5 LZMA per-entry level {(s0 == s9 ? "IGNORED (levels identical)" : "HONORED")}");
    }

    [Fact]
    public void Probe6_ZStandard_PerEntryLevelBehavior()
    {
        var (s0, s9, note) = TryPerEntryOn(CompressionType.ZStandard);
        Console.WriteLine($"PROBE6 ZStandard: level0={s0} level9={s9} note={note}");
        if (note == "ok")
            Console.WriteLine($"PROBE6 ZStandard per-entry level {(s0 == s9 ? "IGNORED (levels identical)" : "HONORED")}");
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 7：SharpCompress ZipWriter 是否真的不能写加密（决定加密路径必须走 SharpSevenZip）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Probe7_SharpCompress_ZipWriter_EncryptionCapability()
    {
        var t = typeof(ZipWriterOptions);
        var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => $"{p.Name}:{p.PropertyType.Name}")
            .OrderBy(s => s)
            .ToArray();

        Console.WriteLine($"PROBE7 ZipWriterOptions properties: {string.Join(", ", props)}");

        var hasPassword = props.Any(p =>
            p.StartsWith("Password", StringComparison.OrdinalIgnoreCase) ||
            (p.Contains("Password", StringComparison.OrdinalIgnoreCase) && p.EndsWith(":String")));

        Console.WriteLine($"PROBE7 has password property: {hasPassword}");
        Assert.False(hasPassword, "if a password property exists, the 'encrypted must use SharpSevenZip' assumption is wrong");
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 8：SharpSevenZip 是否真的没有 per-entry 级别 API（决定 7z 自适应只能全局）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Probe8_SharpSevenZipCompressor_ApiSurface()
    {
        var t = typeof(SharpSevenZipCompressor);
        var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => $"{p.Name}:{p.PropertyType.Name}")
            .OrderBy(s => s)
            .ToArray();

        Console.WriteLine($"PROBE8 SharpSevenZipCompressor properties: {string.Join(", ", props)}");

        // 断言：不存在「按条目/文件」的级别或方法属性（若存在则 7z 自适应可行性需重新评估）
        var perEntryProps = props.Where(p =>
            p.Contains("PerEntry", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("PerFile", StringComparison.OrdinalIgnoreCase)).ToArray();

        Console.WriteLine($"PROBE8 per-entry-ish properties: {(perEntryProps.Length == 0 ? "none" : string.Join(", ", perEntryProps))}");
        Assert.Empty(perEntryProps);
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 9：自适应压缩的前提是否成立 —— 不可压缩数据降级 Store 的收益
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Probe9_AdaptivePremise_IncompressibleDataBenefit()
    {
        var data = Incompressible(4 * 1024 * 1024);

        // 分开计时以便输出
        long TimeLevel(int level, out long size)
        {
            var dir = Path.Combine(Path.GetTempPath(), "MantisZipAdaptiveProbe");
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, $"{Guid.NewGuid():N}_premise_{level}.zip");
            Track(p);
            var sw = Stopwatch.StartNew();
            using (var fs = File.Create(p))
            using (var zw = new ZipWriter(fs, new ZipWriterOptions(CompressionType.Deflate) { CompressionLevel = level }))
            {
                using var src = new MemoryStream(data);
                zw.Write("photo.jpg", src, new ZipWriterEntryOptions { CompressionLevel = level });
            }
            sw.Stop();
            size = new FileInfo(p).Length;
            return sw.ElapsedMilliseconds;
        }

        var t0 = TimeLevel(0, out var s0);
        var t9 = TimeLevel(9, out var s9);

        Console.WriteLine($"PROBE9 incompressible 4MB: level0={s0}B/{t0}ms  level9={s9}B/{t9}ms");
        Console.WriteLine($"PROBE9 size delta={s9 - s0}B  time delta={t9 - t0}ms");

        // 前提：不可压缩数据上，级别 9 相比级别 0 体积几乎无收益（这正是自适应的价值）
        Assert.True(Math.Abs(s9 - s0) < s0 * 0.02,
            "level 9 should give ~no size benefit on incompressible data (adaptive premise)");
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 10：ZipWriterEntryOptions 的全部可 per-entry 属性（供设计参考）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Probe10_ZipWriterEntryOptions_FullSurface()
    {
        var props = typeof(ZipWriterEntryOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => $"{p.Name}:{p.PropertyType.Name}")
            .OrderBy(s => s)
            .ToArray();

        Console.WriteLine($"PROBE10 ZipWriterEntryOptions: {string.Join(", ", props)}");
        Assert.Contains("CompressionLevel:Nullable`1", props);
        Assert.Contains("CompressionType:Nullable`1", props);
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 11：Store 的正确实现方式 —— CompressionType.None vs CompressionLevel=0
    //   （探针 6 已证明 ZStandard 拒绝 level 0，故必须确定通用 Store 写法）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Probe11_StoreMechanism_Matrix()
    {
        var data = Compressible(256 * 1024);
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipAdaptiveProbe");
        Directory.CreateDirectory(dir);

        (string Note, string Detail) Try(string tag, CompressionType archiveType, int writerLevel, ZipWriterEntryOptions eo)
        {
            var p = Path.Combine(dir, $"{Guid.NewGuid():N}_{tag}.zip");
            Track(p);
            try
            {
                using (var fs = File.Create(p))
                using (var zw = new ZipWriter(fs, new ZipWriterOptions(archiveType) { CompressionLevel = writerLevel }))
                using (var src = new MemoryStream(data))
                    zw.Write("a.bin", src, eo);

                using var za = ZipArchive.OpenArchive(p, new ReaderOptions());
                var e = za.Entries.First(x => !x.IsDirectory);
                return ("ok", $"type={e.CompressionType} size={e.Size} compressed={e.CompressedSize}");
            }
            catch (Exception ex)
            {
                return ("THREW", $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        var r1 = Try("deflate_lvl0", CompressionType.Deflate, 9, new ZipWriterEntryOptions { CompressionLevel = 0 });
        var r2 = Try("deflate_none_lvl0", CompressionType.Deflate, 9, new ZipWriterEntryOptions { CompressionType = CompressionType.None, CompressionLevel = 0 });
        var r3 = Try("deflate_none_nolvl", CompressionType.Deflate, 9, new ZipWriterEntryOptions { CompressionType = CompressionType.None });
        var r4 = Try("zstd_none_lvl0", CompressionType.ZStandard, 3, new ZipWriterEntryOptions { CompressionType = CompressionType.None, CompressionLevel = 0 });
        var r5 = Try("bzip2_nolvl", CompressionType.BZip2, 0, new ZipWriterEntryOptions());

        Console.WriteLine($"PROBE11 deflate + lvl=0              : {r1.Note} {r1.Detail}");
        Console.WriteLine($"PROBE11 deflate + type=None, lvl=0   : {r2.Note} {r2.Detail}");
        Console.WriteLine($"PROBE11 deflate + type=None, no lvl  : {r3.Note} {r3.Detail}");
        Console.WriteLine($"PROBE11 zstd    + type=None, lvl=0   : {r4.Note} {r4.Detail}");
        Console.WriteLine($"PROBE11 bzip2   + (no per-entry opts): {r5.Note} {r5.Detail}");

        // 至少一种可用的 Store 配方
        Assert.True(r1.Note == "ok" || r2.Note == "ok", "no viable Store recipe found");
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 12：ZStandard 归档中如何 Store（level 0 会抛异常）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Probe12_ZStandard_StoreMechanism()
    {
        var data = Compressible(256 * 1024);
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipAdaptiveProbe");
        Directory.CreateDirectory(dir);

        (string Note, string Detail) Try(string tag, ZipWriterEntryOptions eo)
        {
            var p = Path.Combine(dir, $"{Guid.NewGuid():N}_{tag}.zip");
            Track(p);
            try
            {
                using (var fs = File.Create(p))
                using (var zw = new ZipWriter(fs, new ZipWriterOptions(CompressionType.ZStandard) { CompressionLevel = 3 }))
                using (var src = new MemoryStream(data))
                    zw.Write("a.bin", src, eo);

                using var za = ZipArchive.OpenArchive(p, new ReaderOptions());
                var e = za.Entries.First(x => !x.IsDirectory);
                return ("ok", $"type={e.CompressionType} size={e.Size} compressed={e.CompressedSize}");
            }
            catch (Exception ex)
            {
                return ("THREW", $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        var a = Try("zs_lvl0", new ZipWriterEntryOptions { CompressionLevel = 0 });
        var b = Try("zs_none_lvl0", new ZipWriterEntryOptions { CompressionType = CompressionType.None, CompressionLevel = 0 });
        var c = Try("zs_lvl1", new ZipWriterEntryOptions { CompressionLevel = 1 });
        var d = Try("zs_nolvl", new ZipWriterEntryOptions());

        Console.WriteLine($"PROBE12 zstd lvl=0            : {a.Note} {a.Detail}");
        Console.WriteLine($"PROBE12 zstd type=None, lvl=0 : {b.Note} {b.Detail}");
        Console.WriteLine($"PROBE12 zstd lvl=1            : {c.Note} {c.Detail}");
        Console.WriteLine($"PROBE12 zstd (no per-entry)   : {d.Note} {d.Detail}");

        Assert.True(b.Note == "ok" || c.Note == "ok" || d.Note == "ok",
            "no viable ZStandard per-entry recipe found");
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 13：PPMd 的 per-entry 级别行为（补完矩阵）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Probe13_Ppmd_PerEntryLevelBehavior()
    {
        var (s0, s9, note) = TryPerEntryOn(CompressionType.PPMd);
        Console.WriteLine($"PROBE13 PPMd: level0={s0} level9={s9} note={note}");
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 14：推荐方案的端到端验证 —— Deflate 归档混合「级别 9 + Store」
    //   并确认 7z.dll 可解压（模拟真实自适应产物）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Probe14_RecommendedApproach_EndToEnd()
    {
        if (!SevenZipAvailable()) return;

        var text = Compressible(256 * 1024);
        var photo = Incompressible(256 * 1024);
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipAdaptiveProbe");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{Guid.NewGuid():N}_recommended.zip");
        Track(path);

        // 推荐写法（经探针验证）：文本走用户级别（Deflate + level N），
        // 已压缩文件走 CompressionLevel = 0（Deflate stored-block；不可用 CompressionType.None，见探针 11）
        using (var fs = File.Create(path))
        using (var zw = new ZipWriter(fs, new ZipWriterOptions(CompressionType.Deflate) { CompressionLevel = 5 }))
        {
            using (var s1 = new MemoryStream(text))
                zw.Write("notes.txt", s1, new ZipWriterEntryOptions { CompressionLevel = 9 });

            using (var s2 = new MemoryStream(photo))
                zw.Write("photo.jpg", s2, new ZipWriterEntryOptions { CompressionLevel = 0 });
        }

        using (var za = ZipArchive.OpenArchive(path, new ReaderOptions()))
        {
            foreach (var e in za.Entries.Where(e => !e.IsDirectory))
                Console.WriteLine($"PROBE14 {e.Key}: type={e.CompressionType} size={e.Size} compressed={e.CompressedSize}");
        }

        // 7z.dll 独立解压并逐字节比对
        var dest = Path.Combine(dir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        try
        {
            using (var ex = new SharpSevenZipExtractor(path))
                ex.ExtractArchive(dest);

            Assert.Equal(text, await File.ReadAllBytesAsync(Path.Combine(dest, "notes.txt")));
            Assert.Equal(photo, await File.ReadAllBytesAsync(Path.Combine(dest, "photo.jpg")));
            Console.WriteLine("PROBE14 recommended adaptive output extracted by 7z.dll OK (byte-identical)");
        }
        finally
        {
            try { Directory.Delete(dest, true); } catch { }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 15：WriteToStream 是否同样遵守 per-entry 级别
    //   （MantisZip ZipEngine 实际使用 WriteToStream，而非探针 1 的 Write）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Probe15_WriteToStream_HonorsPerEntryLevel()
    {
        var data = Compressible(256 * 1024);
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipAdaptiveProbe");
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, $"{Guid.NewGuid():N}_wts.zip");
        Track(p);

        using (var fs = File.Create(p))
        using (var zw = new ZipWriter(fs, new ZipWriterOptions(CompressionType.Deflate) { CompressionLevel = 5 }))
        {
            using (var es = zw.WriteToStream("stored.bin", new ZipWriterEntryOptions { CompressionLevel = 0 }))
                es.Write(data, 0, data.Length);

            using (var es = zw.WriteToStream("pressed.bin", new ZipWriterEntryOptions { CompressionLevel = 9 }))
                es.Write(data, 0, data.Length);
        }

        using var za = ZipArchive.OpenArchive(p, new ReaderOptions());
        foreach (var e in za.Entries.Where(x => !x.IsDirectory))
            Console.WriteLine($"PROBE15 {e.Key}: type={e.CompressionType} size={e.Size} compressed={e.CompressedSize}");

        var stored = za.Entries.First(e => e.Key == "stored.bin");
        var pressed = za.Entries.First(e => e.Key == "pressed.bin");
        Assert.True(stored.CompressedSize > pressed.CompressedSize,
            "WriteToStream must honor per-entry CompressionLevel (engine uses this API)");
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 16：BZip2/LZMA/PPMd 归档中能否逐条目 Store
    //   （这些类型不支持「级别」，但可能仍支持 per-entry CompressionType.None）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Probe16_TypeNoneStore_InNonLevelArchives()
    {
        var data = Compressible(256 * 1024);
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipAdaptiveProbe");
        Directory.CreateDirectory(dir);

        (string Note, string Detail) Try(string tag, CompressionType archiveType)
        {
            var p = Path.Combine(dir, $"{Guid.NewGuid():N}_{tag}.zip");
            Track(p);
            try
            {
                using (var fs = File.Create(p))
                using (var zw = new ZipWriter(fs, new ZipWriterOptions(archiveType) { CompressionLevel = 0 }))
                using (var src = new MemoryStream(data))
                    zw.Write("a.bin", src, new ZipWriterEntryOptions
                    {
                        CompressionType = CompressionType.None,
                        CompressionLevel = 0,
                    });

                using var za = ZipArchive.OpenArchive(p, new ReaderOptions());
                var e = za.Entries.First(x => !x.IsDirectory);
                return ("ok", $"type={e.CompressionType} size={e.Size} compressed={e.CompressedSize}");
            }
            catch (Exception ex)
            {
                return ("THREW", $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        var bz = Try("bzip2_store", CompressionType.BZip2);
        var lz = Try("lzma_store", CompressionType.LZMA);
        var pp = Try("ppmd_store", CompressionType.PPMd);

        Console.WriteLine($"PROBE16 BZip2 + type=None,lvl=0 : {bz.Note} {bz.Detail}");
        Console.WriteLine($"PROBE16 LZMA  + type=None,lvl=0 : {lz.Note} {lz.Detail}");
        Console.WriteLine($"PROBE16 PPMd  + type=None,lvl=0 : {pp.Note} {pp.Detail}");
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 17：SharpSevenZip 是否支持纯内存压缩（预估器「标准采样」依赖此能力）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Probe17_SevenZip_MemoryStreamCapability()
    {
        var methods = typeof(SharpSevenZipCompressor)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name.StartsWith("Compress", StringComparison.Ordinal))
            .Select(m => m.ToString())
            .OrderBy(s => s)
            .ToArray();

        Console.WriteLine("PROBE17 SharpSevenZipCompressor Compress* overloads:");
        foreach (var m in methods)
            Console.WriteLine("  " + m);

        var hasStreamOutput = methods.Any(m => m.Contains("System.IO.Stream"));
        Console.WriteLine($"PROBE17 has Stream-based Compress overload: {hasStreamOutput}");

        // 实际验证：Stream → Stream 内存压缩（预估器「标准采样」需要此能力）
        try
        {
            var payload = Compressible(256 * 1024);
            using var inMs = new MemoryStream(payload);
            using var outMs = new MemoryStream();
            var compr = new SharpSevenZipCompressor { ArchiveFormat = OutArchiveFormat.SevenZip };
            compr.CompressStream(inMs, outMs, "");
            Console.WriteLine($"PROBE17 7z memory compression OK: in={payload.Length} out={outMs.Length} ratio={(double)outMs.Length / payload.Length:F4}");
            Assert.True(outMs.Length > 0, "7z memory compression produced no output");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PROBE17 7z memory compression FAILED: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // 探针 18：非级别归档（BZip2）中「混合 BZip2 + Store」是否成立并互通
    //   自适应在 BZip2/LZMA/PPMd 归档中的真实用法
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Probe18_NonLevelArchive_MixedStoreAndCompressed()
    {
        if (!SevenZipAvailable()) return;

        var text = Compressible(256 * 1024);
        var photo = Incompressible(256 * 1024);
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipAdaptiveProbe");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{Guid.NewGuid():N}_bzip2_mixed.zip");
        Track(path);

        using (var fs = File.Create(path))
        using (var zw = new ZipWriter(fs, new ZipWriterOptions(CompressionType.BZip2) { CompressionLevel = 0 }))
        {
            // 文本：正常 BZip2 压缩（不设 per-entry 选项）
            using (var s1 = new MemoryStream(text))
                zw.Write("notes.txt", s1, new ZipWriterEntryOptions());

            // 已压缩文件：逐条目 Store
            using (var s2 = new MemoryStream(photo))
                zw.Write("photo.jpg", s2, new ZipWriterEntryOptions
                {
                    CompressionType = CompressionType.None,
                    CompressionLevel = 0,
                });
        }

        using (var za = ZipArchive.OpenArchive(path, new ReaderOptions()))
        {
            foreach (var e in za.Entries.Where(x => !x.IsDirectory))
                Console.WriteLine($"PROBE18 {e.Key}: type={e.CompressionType} size={e.Size} compressed={e.CompressedSize}");
        }

        var dest = Path.Combine(dir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        try
        {
            using (var ex = new SharpSevenZipExtractor(path))
                ex.ExtractArchive(dest);

            Assert.Equal(text, await File.ReadAllBytesAsync(Path.Combine(dest, "notes.txt")));
            Assert.Equal(photo, await File.ReadAllBytesAsync(Path.Combine(dest, "photo.jpg")));
            Console.WriteLine("PROBE18 mixed BZip2+Store extracted by 7z.dll OK (byte-identical)");
        }
        finally
        {
            try { Directory.Delete(dest, true); } catch { }
        }
    }
}
