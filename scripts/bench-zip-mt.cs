#:project ../src/MantisZip.Core/MantisZip.Core.csproj

// ============================================================================
// ZIP 多线程压缩基准测试（实验性诊断脚本，非应用代码）
//
// 目的：量化评估 ArchiveOptions.MultiThreadedCompression 的真实净收益。
// 该设置此前没有任何实测数据（docs 中仅有 7z 格式 mt=on 的 4.63x，
// 那是 OutArchiveFormat.SevenZip，不适用于本脚本测的 ZIP Store/CompressGroup 混合路径）。
//
// 历史缺陷（2026-10-04 实测，20 核 / 文本语料 12.95MB）——**已修复**，保留记录以说明本脚本的检测项：
//   MT 路径曾在 7z.dll 上 mt=on 并行压缩【被丢弃】——
//     CompressGroupWithSevenZip  → 写入 tempZip          （并行做的功）
//     MergeTempZipToWriter       → OpenEntryStream()      （解压回原始字节，丢弃成果）
//     MergeTempZipToWriter       → WriteToStream(CompressionLevel = null)
//                                （SharpCompress 串行重新压缩）
//   即文件被压两遍，最终产物完全由第二遍决定，且 CompressionLevel=null
//   使压缩级别与用户设置无关。当时实测 B/C 与不开 MT 的产物【字节级完全相同】，耗时反而多 84%。
//
//   现状：MergeTempZipToWriter 已删除，MT 路径改为「方案 A」——ZIP 内容以 7z 产物为准，
//   纯压缩组直接 File.Move，混合组经 ZipBinaryRewriter 原样复制并追加 Store 条目，
//   100% 保留 7z 压缩字节（回归测试 CompressAsync_MultiThreaded_PreservesSevenZipCompressedBytes）。
//   下面「产物字节级一致」的自动检测保留为回归哨兵：若它再次报警，说明字节保真又被破坏。
//
// 另有预期缺陷：ZipEntryClassifier.GetAdaptiveLevel() 在 AdaptiveCompression=false 时
//   对所有文件返回 userLevel，因此「仅多线程」会把 JPEG/MP4 等已压缩格式
//   也送进 7z deflate（压不动 + 体积变大 + 烧 CPU）。
//
// 用法：
//   dotnet run scripts/bench-zip-mt.cs                          # 合成语料，text+media+mixed
//   dotnet run scripts/bench-zip-mt.cs -- --profile text        # 只测文本（CPU-bound）
//   dotnet run scripts/bench-zip-mt.cs -- --corpus D:\MyFolder   # 测真实文件夹
//   dotnet run scripts/bench-zip-mt.cs -- --reps 5 --level 9
//   dotnet run scripts/bench-zip-mt.cs -- --degrees 1,2,4,8
//   dotnet run scripts/bench-zip-mt.cs -- --json out.json
//
// 注意：I/O 基准对环境极敏感。请先关闭其他压缩软件、杀毒实时扫描、
//       网盘同步工具和 IDE 文件索引，否则数字不可信。
// ============================================================================

using System.Diagnostics;
using System.Globalization;
using System.Text;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;

return await Bench.RunAsync(args);

// ============================================================================
// 配置矩阵：自适应压缩 × 多线程压缩 的 2×2 全组合
// ============================================================================

/// <summary>单个压缩配置。</summary>
sealed record Config(string Id, string Label, bool Adaptive, bool MultiThreaded)
{
    public ArchiveOptions ToOptions(int level, int degree) => new()
    {
        Format = ArchiveFormat.Zip,
        CompressionLevel = level,
        Encrypt = false,              // MT 路径要求非加密（ZipEngine.cs:1397）
        ZipCompressionMethod = "deflate", // MT 路径要求 deflate/deflate64（ZipEngine.cs:1400）
        AdaptiveCompression = Adaptive,
        MultiThreadedCompression = MultiThreaded,
        ParallelCompressDegree = degree,
    };
}

static class Configs
{
    public static readonly Config[] All =
    {
        new("D", "传统默认（全关）",   false, false),
        new("A", "仅自适应",           true,  false),
        new("B", "仅多线程",           false, true),
        new("C", "自适应 + 多线程",    true,  true),
    };
}

// ============================================================================
// 语料生成：文本走真实可压缩内容，媒体走随机字节
// ============================================================================

static class Corpus
{
    /// <summary>生成文本语料：模拟真实源码/JSON/XML，deflate 压缩比约 3-4x（贴近真实世界）。</summary>
    public static void GenerateText(string dir, int fileCount, int sizeKb, int seed)
    {
        var rng = new Random(seed);
        for (int i = 0; i < fileCount; i++)
        {
            string ext = i % 5 == 0 ? ".json" : i % 7 == 0 ? ".xml" : i % 3 == 0 ? ".txt" : ".cs";
            string sub = $@"{dir}\src\mod{i % 8}";
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, $"File{i:D4}{ext}"), GenerateSourceText(rng, sizeKb * 1024), new UTF8Encoding(false));
        }
    }

    /// <summary>生成媒体语料：随机字节不可压缩，模拟 JPEG/MP4/PNG（deflate 压不动甚至变大）。</summary>
    public static void GenerateMedia(string dir, int fileCount, int sizeKb, int seed)
    {
        var rng = new Random(seed);
        byte[] buffer = new byte[64 * 1024];
        string[] exts = { ".jpg", ".png", ".mp4", ".zip" };
        for (int i = 0; i < fileCount; i++)
        {
            string sub = $@"{dir}\media";
            Directory.CreateDirectory(sub);
            long target = sizeKb * 1024L;
            using var fs = new FileStream(Path.Combine(sub, $"Clip{i:D4}{exts[i % exts.Length]}"), FileMode.Create, FileAccess.Write);
            while (fs.Position < target)
            {
                rng.NextBytes(buffer);
                int n = (int)Math.Min(buffer.Length, target - fs.Position);
                fs.Write(buffer, 0, n);
            }
        }
    }

    // C# 源码模板词库 —— 使用真实关键字与标识符形态，保证熵值接近真实源码
    static readonly string[] Keywords =
    {
        "public","private","protected","internal","static","readonly","async","await","override","virtual",
        "class","record","struct","interface","enum","void","var","string","int","double","bool","Task",
        "if","else","return","foreach","for","while","switch","case","break","try","catch","finally","new",
    };

    static readonly string[] Identifiers =
    {
        "HandleAsync","ProcessRequest","BuildResult","CreateInstance","ValidateInput","ResolvePath","LoadBuffer",
        "WriteEntry","GetOrCreate","TryParse","FlushCache","buffer","options","result","entries","archive","stream",
        "progress","cancellationToken","sourcePath","destination","fileInfo","engine","settings","mapper","index",
    };

    static readonly string[] Types =
    {
        "string","int","bool","double","long","byte[]","Task","ArchiveItem","ArchiveOptions","Stream","IDisposable",
    };

    /// <summary>按模板拼装类 C# 源码，逼近 targetBytes 大小。</summary>
    static string GenerateSourceText(Random rng, int targetBytes)
    {
        var sb = new StringBuilder(targetBytes + 512);
        while (sb.Length < targetBytes)
        {
            int indent = rng.Next(0, 5);
            string pad = new string(' ', indent * 4);
            switch (rng.Next(8))
            {
                case 0:
                    sb.Append(pad).Append("/// <summary>").Append(Pick(rng, Identifiers)).Append(" 处理器。</summary>\n");
                    break;
                case 1:
                    sb.Append(pad).Append(Pick(rng, Keywords)).Append(' ').Append(Pick(rng, Keywords)).Append(' ')
                      .Append(Pick(rng, Identifiers)).Append('(')
                      .Append(Pick(rng, Identifiers)).Append(", ").Append(Pick(rng, Identifiers)).Append(")\n");
                    break;
                case 2:
                    sb.Append(pad).Append("if (").Append(Pick(rng, Identifiers)).Append(" > ").Append(rng.Next(0, 4096)).Append(")\n");
                    break;
                case 3:
                    sb.Append(pad).Append("return await ").Append(Pick(rng, Identifiers)).Append(".InvokeAsync(")
                      .Append(Pick(rng, Identifiers)).Append(");\n");
                    break;
                case 4:
                    sb.Append(pad).Append(Pick(rng, Types)).Append(' ').Append(Pick(rng, Identifiers)).Append(" = ")
                      .Append(Pick(rng, Keywords)).Append('(').Append(rng.Next(0, 99999)).Append(");\n");
                    break;
                case 5:
                    sb.Append(pad).Append("foreach (var ").Append(Pick(rng, Identifiers)).Append(" in ")
                      .Append(Pick(rng, Identifiers)).Append(")\n");
                    break;
                case 6:
                    sb.Append('\n').Append(pad).Append("// 处理 ").Append(Pick(rng, Identifiers))
                      .Append(" 的边界条件，避免重复计算\n");
                    break;
                default:
                    sb.Append(pad).Append(Pick(rng, Keywords)).Append(' ').Append(Pick(rng, Identifiers))
                      .Append(" = ").Append(rng.Next(0, 100)).Append(";\n");
                    break;
            }
        }
        return sb.ToString();
    }

    static string Pick(Random rng, string[] pool) => pool[rng.Next(pool.Length)];
}

// ============================================================================
// 进度遥测：记录每次上报的（已耗时, 总百分比, 单文件百分比）
//
// 【重要】lead 指标（最大/平均领先）的正确读法
// ---------------------------------------------------------------------------
// lead = 上报的字节加权百分比 - 已耗时的墙钟占比
//
// 基线**不是 0**：字节加权进度与墙钟时间只在「所有条目压缩速率一致」时才同速。
// 语料异质时二者必然发散，方向可正可负：
//   · 自适应把已压缩媒体降级为 Store（近乎瞬间完成）→ 大文件瞬间计满，
//     字节百分比跑到时间前面 → lead 为正（实测 mixed 约 +20~22pt）。
//   · 小文本 Deflate 密集且慢 → 字节百分比落后于时间 → lead 为负（实测约 -31pt）。
//
// 因此：
//   · **lead 为正或负都不代表引擎缺陷**，它衡量的是「字节加权 vs 墙钟」的口径差，
//     不是正确性指标。不可为了压低 lead 而改成按时间伪造百分比（那是撒谎）。
//   · D（传统默认，关闭自适应）基线实测 0.0pt —— 说明字节记账本身是准的；
//     A/B/C 的偏离全部来自自适应改写了条目的压缩方法，而非进度计算错误。
//
// 真正该作为门禁的硬约束：末值 100%、无倒退、采样数够密。
//
// NullFilePercentRatio 在 MT（mt=on）下天然很高 —— 7z 不触发 Compressing
// （实测 started=60 compressing=0 finished=60），且 mt=on 无法把字节归因到单个文件，
// 故 MT 路径的 FilePercentComplete 设计上为 null，非缺陷。
// ============================================================================

sealed class ProgressRecorder : IProgress<ArchiveProgress>
{
    readonly Stopwatch _sw;
    readonly List<Sample> _samples = new();
    readonly object _gate = new();

    public ProgressRecorder(Stopwatch sw) => _sw = sw;

    public void Report(ArchiveProgress value)
    {
        lock (_gate)
        {
            // 逐条目终态（Skipped/Failed/Completed）是独立通道：UI 侧 ProgressViewModel.SetProgress
            // 对它早返回，明确不参与百分比/速度/ETA 计算，其 PercentComplete 恒为默认值 0。
            // 因此必须与百分比通道分开统计，否则「末值 0%」「平均大幅负领先」全是伪信号。
            bool isEntryStatus = value.EntryStatus.HasValue && !string.IsNullOrEmpty(value.EntryKey);
            _samples.Add(new Sample(
                _sw.Elapsed.TotalMilliseconds,
                value.PercentComplete,
                value.FilePercentComplete,
                isEntryStatus));
        }
    }

    public List<Sample> Drain()
    {
        lock (_gate)
        {
            var copy = new List<Sample>(_samples);
            _samples.Clear();
            return copy;
        }
    }

    internal readonly record struct Sample(double ElapsedMs, double Percent, double? FilePercent, bool IsEntryStatus);
}

/// <summary>进度失真统计结果。</summary>
sealed record ProgressStats(
    int SampleCount,
    int EntryStatusCount,     // 逐条目终态事件数（独立通道，不参与下方百分比统计）
    double MaxLeadPercent,     // 上报进度领先实际耗时的最大百分点（正数 = 抢跑）
    double AvgLeadPercent,
    bool NonMonotonic,         // 进度是否出现倒退
    double FinalPercent,
    double NullFilePercentRatio // FilePercentComplete 为 null 的比例（0 = 全程有单文件进度）
);

static class ProgressAnalysis
{
    public static ProgressStats Analyze(List<ProgressRecorder.Sample> samples, double totalMs)
    {
        int entryStatusCount = samples.Count(s => s.IsEntryStatus);

        // 只统计百分比通道：逐条目终态事件按引擎契约不携带百分比（恒为默认 0）
        var pct = samples.Where(s => !s.IsEntryStatus).ToList();
        if (pct.Count == 0)
            return new ProgressStats(0, entryStatusCount, 0, 0, false, 0, 1);

        double maxLead = double.MinValue, sumLead = 0;
        bool nonMono = false;
        double prev = pct[0].Percent;
        int nullCount = 0;

        foreach (var s in pct)
        {
            double elapsedFrac = totalMs > 0 ? s.ElapsedMs / totalMs : 0;
            double lead = (s.Percent / 100.0) - elapsedFrac;
            maxLead = Math.Max(maxLead, lead);
            sumLead += lead;

            if (s.Percent < prev - 0.5) nonMono = true;   // 容差 0.5pt 避免抖动误报
            prev = s.Percent;

            if (s.FilePercent is null) nullCount++;
        }

        return new ProgressStats(
            pct.Count,
            entryStatusCount,
            Math.Round(maxLead * 100, 1),
            Math.Round(sumLead / pct.Count * 100, 1),
            nonMono,
            Math.Round(prev, 1),
            Math.Round((double)nullCount / pct.Count, 2));
    }
}

// ============================================================================
// 单次测量结果
// ============================================================================

sealed record RunResult(
    string ConfigId,
    double Milliseconds,
    long CompressedBytes,
    int EntryCount,
    long UncompressedBytes,   // 条目未压缩字节总和，用作条目集合指纹
    bool ArchiveValid,
    ProgressStats Progress
);

sealed record ConfigSummary(
    string Id, string Label,
    double MinMs, double MedianMs,
    IReadOnlyList<double> AllSamples,
    long CompressedBytes,
    double Ratio,             // 原始字节 / 压缩字节，越大越好
    double CompressionRate,    // 压缩字节 / 原始字节，越小越好
    int EntryCount,
    bool ArchiveValid, bool MatchesBaseline,
    ProgressStats Progress);

sealed record ProfileSummary(
    string Profile, int Degree, long RawBytes, int FileCount,
    IReadOnlyList<ConfigSummary> Configs, string Verdict);

// ============================================================================
// 基准主体
// ============================================================================

static class Bench
{
    public static async Task<int> RunAsync(string[] argv)
    {
        // 统一 UTF-8 输出。注：PowerShell 用 *> 重定向时可能按自己的代码页解码导致显示乱码，
        // 那是捕获端的解码问题；用 --report <path> 可拿到一定是 UTF-8 的干净报告。
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch { /* 部分宿主不支持切换编码，忽略 */ }

        var opt = Options.Parse(argv);
        if (opt.ShowHelp) { Options.PrintHelp(); return 0; }

        PrintEnvironment(opt);
        ResolveSevenZipDll(opt);

        // 语料组选择：--corpus 只跑真实文件夹一组；--profile 只跑指定一组；默认全部三组
        var profiles = opt.Corpus is not null
            ? new[] { "real" }
            : opt.Profile is not null
                ? new[] { opt.Profile }
                : new[] { "text", "media", "mixed" };

        var summaries = new List<ProfileSummary>();
        foreach (var profile in profiles)
            summaries.AddRange(await RunProfileAsync(profile, opt));

        Report(summaries, opt);
        if (opt.JsonPath is not null) WriteJson(summaries, opt.JsonPath);

        // 门禁：只要有一个配置的输出校验失败，就以非零码退出，便于接入自动化
        bool allValid = summaries.SelectMany(s => s.Configs).All(c => c.ArchiveValid && c.MatchesBaseline);
        return allValid ? 0 : 1;
    }

    // ---------------------------------------------------------------- 环境信息

    static void PrintEnvironment(Options opt)
    {
        string temp = Path.GetTempPath();
        Console.WriteLine("==================== 环境 ====================");
        Console.WriteLine($"运行时      : {Environment.Version}");
        Console.WriteLine($"操作系统    : {Environment.OSVersion}");
        Console.WriteLine($"逻辑核心数  : {Environment.ProcessorCount}");
        Console.WriteLine($"压缩级别    : {opt.Level}");
        Console.WriteLine($"计时轮次    : {opt.Reps}（取最小值）");
        Console.WriteLine($"压缩组数    : {string.Join(",", opt.Degrees)}");
        Console.WriteLine($"临时目录    : {temp}");
        Console.WriteLine($"输出目录    : {opt.WorkDir}");
        // MT 路径会把压缩结果写一份同体积临时 zip 再合并回来，跨盘会显著改变 I/O 成本
        Console.WriteLine($"临时/输出同盘: {SameVolume(temp, opt.WorkDir)}");
        Console.WriteLine();
    }

    static bool SameVolume(string a, string b)
    {
        try { return string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    static void ResolveSevenZipDll(Options opt)
    {
        var candidates = new List<string>();
        if (opt.SevenZipDll is not null) candidates.Add(opt.SevenZipDll);
        candidates.Add(Path.Combine(RepoRoot(), "src", "MantisZip.Core", "bin", "Debug", "net10.0", "x64", "7z.dll"));
        candidates.Add(Path.Combine(RepoRoot(), "src", "MantisZip.Core", "bin", "Release", "net10.0", "x64", "7z.dll"));
        candidates.Add(@"C:\Program Files\7-Zip\7z.dll");
        candidates.Add(@"C:\Program Files (x86)\7-Zip\7z.dll");

        string? found = candidates.FirstOrDefault(File.Exists);
        if (found is null)
            {
                Console.Error.WriteLine("[致命错误] 未找到 7z.dll。多线程压缩路径依赖它，无法执行 B/C 配置。");
                Console.Error.WriteLine("请用 --7z <路径> 显式指定。");
                Environment.Exit(2);
            }

        // 显式赋值：file-based app 的 BaseDirectory 不含 7z.dll，必须手动指定
        SevenZipEngine.SevenZipDllPath = found!;
        opt.SevenZipDll = found;
        Console.WriteLine($"7z.dll      : {found}");
        Console.WriteLine();
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MantisZip.sln")))
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "MantisZip.Core", "MantisZip.Core.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Environment.CurrentDirectory;
    }

    // ---------------------------------------------------------------- 单组语料

    static async Task<List<ProfileSummary>> RunProfileAsync(string profile, Options opt)
    {
        var allSummaries = new List<ProfileSummary>();
        string corpusDir = Path.Combine(opt.WorkDir, $"corpus-{profile}");
        string outDir = Path.Combine(opt.WorkDir, $"out-{profile}");

        if (Directory.Exists(corpusDir)) Directory.Delete(corpusDir, true);
        Directory.CreateDirectory(corpusDir);
        Directory.CreateDirectory(outDir);

        Console.WriteLine($"==================== 语料：{profile} ====================");

        switch (profile)
        {
            case "text":
                Corpus.GenerateText(corpusDir, opt.TextFiles, opt.TextSizeKb, 20260904);
                break;
            case "media":
                Corpus.GenerateMedia(corpusDir, opt.MediaFiles, opt.MediaSizeKb, 20260905);
                break;
            case "mixed":
                Corpus.GenerateText(Path.Combine(corpusDir, "src"), opt.TextFiles, opt.TextSizeKb, 20260904);
                Corpus.GenerateMedia(Path.Combine(corpusDir, "media"), opt.MediaFiles, opt.MediaSizeKb, 20260905);
                break;
            case "real":
                if (opt.Corpus is null) throw new InvalidOperationException("real profile 需要 --corpus");
                break;
        }

        // real profile 压缩的是 --corpus 指向的真实目录；其余 profile 用上面生成的合成语料。
        // 二者必须一致：否则「文件数 / 原始体积」取自真实语料，实际压缩的却是空的 corpusDir，
        // 产出的体积与耗时数据毫无意义。
        var sourceDir = opt.Corpus ?? corpusDir;
        Console.WriteLine($"压缩源目录：{sourceDir}");

        var (rawBytes, fileCount) = MeasureCorpus(sourceDir);
        Console.WriteLine($"文件数 {fileCount}，原始 {Fmt.Bytes(rawBytes)}");

        var engine = new ZipEngine();

        foreach (var degree in opt.Degrees)
        {
        // ---- 预热轮：JIT + 7z.dll 加载 + 文件缓存，全部不计时
        Console.Write($"预热中 (degree={degree})");
        foreach (var cfg in Configs.All)
        {
            await MeasureOnceAsync(engine, cfg, sourceDir, outDir, opt, degree, throwaway: true);
            Console.Write(".");
        }
        Console.WriteLine(" 完成\n");

        // ---- 计时轮：每轮轮换配置顺序，避免文件缓存偏袒先跑的配置
        var results = new Dictionary<string, List<RunResult>>();
        foreach (var cfg in Configs.All) results[cfg.Id] = new List<RunResult>();

        for (int rep = 0; rep < opt.Reps; rep++)
        {
            var order = Configs.All.Skip(rep % Configs.All.Length)
                                 .Concat(Configs.All.Take(rep % Configs.All.Length)).ToArray();
            foreach (var cfg in order)
            {
                var r = await MeasureOnceAsync(engine, cfg, sourceDir, outDir, opt, degree, throwaway: false);
                results[cfg.Id].Add(r);
                Console.WriteLine($"  轮 {rep + 1}/{opt.Reps}  degree={degree,2}  {cfg.Id} {cfg.Label,-20} {r.Milliseconds / 1000,8:F2}s  {Fmt.Bytes(r.CompressedBytes),12}  {(r.ArchiveValid ? "OK" : "✗ CRC 校验失败")}");
            }
        }

        // ---- 基线一致性：D（传统默认）作为条目集合基准
        var baseline = results["D"][0];
        var summaries = new List<ConfigSummary>();
        foreach (var cfg in Configs.All)
        {
            var rs = results[cfg.Id];
            var times = rs.Select(r => r.Milliseconds).OrderBy(x => x).ToArray();
            double min = times[0];
            double median = times.Length % 2 == 1
                ? times[times.Length / 2]
                : (times[times.Length / 2 - 1] + times[times.Length / 2]) / 2;
            long compressed = (long)rs.Average(r => r.CompressedBytes);
            ProgressStats bestProg = rs.OrderBy(r => r.Milliseconds).First().Progress;

            summaries.Add(new ConfigSummary(
                cfg.Id, cfg.Label,
                Math.Round(min, 1), Math.Round(median, 1),
                times.Select(t => Math.Round(t, 1)).ToArray(),
                compressed,
                compressed > 0 ? Math.Round((double)rawBytes / compressed, 3) : 0,
                rawBytes > 0 ? Math.Round((double)compressed / rawBytes, 4) : 0,
                baseline.EntryCount,
                rs.All(r => r.ArchiveValid),
                rs.All(r => MatchesBaselineFingerprint(baseline, r)),
                bestProg));
        }

        Console.WriteLine();
        allSummaries.Add(new ProfileSummary(profile, degree, rawBytes, fileCount, summaries, string.Empty));
        } // foreach degree

        return allSummaries;
    }

    static (long bytes, int count) MeasureCorpus(string dir)
    {
        long bytes = 0;
        int count = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            bytes += new FileInfo(f).Length;
            count++;
        }
        return (bytes, count);
    }

    // ---------------------------------------------------------------- 单次测量

    static async Task<RunResult> MeasureOnceAsync(
        ZipEngine engine, Config cfg, string sourceDir, string outDir, Options opt, int degree, bool throwaway)
    {
        string outPath = Path.Combine(outDir, throwaway
            ? $"warmup-{cfg.Id}-{Guid.NewGuid():N}.zip"
            : $"run-{cfg.Id}-{Guid.NewGuid():N}.zip");

        var sw = Stopwatch.StartNew();
        var recorder = new ProgressRecorder(sw);

        try
        {
            await engine.CompressAsync(new[] { sourceDir }, outPath, cfg.ToOptions(opt.Level, degree), recorder);

            // 必须在停止计时前结束压缩，否则文件收尾成本被排除
            sw.Stop();

            double ms = sw.Elapsed.TotalMilliseconds;
            long compressed = new FileInfo(outPath).Length;
            var stats = ProgressAnalysis.Analyze(recorder.Drain(), ms);

            if (throwaway) return new RunResult(cfg.Id, ms, compressed, 0, 0, true, stats);

            bool valid = await engine.TestArchiveAsync(outPath);
            var entries = await engine.ListEntriesAsync(outPath);
            var files = entries.Where(e => !e.IsDirectory).ToList();
            int count = files.Count;
            long uncompressed = files.Sum(e => e.Size);

            return new RunResult(cfg.Id, ms, compressed, count, uncompressed, valid, stats);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    // ---------------------------------------------------------------- 校验：与基线比对条目指纹

    /// <summary>
    /// 完整性门禁：每个配置的 (条目数, 未压缩总字节) 必须与基线 D 完全一致。
    /// 「更快但丢文件」的配置直接判负 —— 只比耗时是不够的。
    /// </summary>
    static bool MatchesBaselineFingerprint(RunResult baseline, RunResult other) =>
        other.EntryCount == baseline.EntryCount
        && other.UncompressedBytes == baseline.UncompressedBytes;

    // ---------------------------------------------------------------- 报告

    /// <summary>生成报告：同时输出到控制台和（可选）UTF-8 报告文件。</summary>
    static void Report(List<ProfileSummary> summaries, Options opt)
    {
        var console = Console.Out;
        using var buffer = new StringWriter();
        Console.SetOut(buffer);
        try
        {
            WriteReport(summaries, opt);
        }
        finally
        {
            Console.SetOut(console);
        }

        string text = buffer.ToString();
        console.Write(text);

        if (opt.ReportPath is not null)
        {
            File.WriteAllText(opt.ReportPath, text, new UTF8Encoding(false));
            console.WriteLine($"\n已写出报告（UTF-8）：{opt.ReportPath}");
        }
    }

    static void WriteReport(List<ProfileSummary> summaries, Options opt)
    {
        Console.WriteLine();
        Console.WriteLine("###################### 计时结果 ######################");

        foreach (var s in summaries)
        {
            Console.WriteLine();
            Console.WriteLine($"■ 语料：{s.Profile}   degree={s.Degree}   原始 {Fmt.Bytes(s.RawBytes)}   文件 {s.FileCount} 个");
            Console.WriteLine();
            Console.WriteLine($"  {"ID",-3} {"配置",-20} {"最小耗时",10} {"中位耗时",10} {"压缩后(字节)",13} {"压缩率",9} {"相对D",8} {"校验",6}");
            Console.WriteLine($"  {new string('-', 3),-3} {new string('-', 20),-20} {new string('-', 10),10} {new string('-', 10),10} {new string('-', 13),13} {new string('-', 9),9} {new string('-', 8),8} {new string('-', 6),6}");

            double baseMs = s.Configs.First(c => c.Id == "D").MinMs;
            foreach (var c in s.Configs)
            {
                string gate = (c.ArchiveValid && c.MatchesBaseline) ? "OK" : "✗";
                Console.WriteLine(
                    $"  {c.Id,-3} {c.Label,-20} {c.MinMs / 1000,9:F2}s {c.MedianMs / 1000,9:F2}s " +
                    $"{c.CompressedBytes,11} B {c.Ratio,8:F3}x {c.MinMs / baseMs,7:F2}x {gate,6}");
            }

            // 压缩率与耗时的关键对比
            var a = s.Configs.First(c => c.Id == "A");
            var b = s.Configs.First(c => c.Id == "B");
            var cc = s.Configs.First(c => c.Id == "C");
            Console.WriteLine();
            Console.WriteLine($"  压缩率对比：A {a.Ratio:F3}x  |  B {b.Ratio:F3}x  |  C {cc.Ratio:F3}x   " +
                              $"(C 与 A 差 {((cc.Ratio - a.Ratio) / a.Ratio * 100):+0.0;-0.0;0.0}%)");
            Console.WriteLine($"  耗时对比：  A {a.MinMs / 1000:F2}s |  B {b.MinMs / 1000:F2}s |  C {cc.MinMs / 1000:F2}s   " +
                              $"(C 比 A {(cc.MinMs < a.MinMs ? "快" : "慢")} {Math.Abs(cc.MinMs - a.MinMs) / 1000:F2}s)");
            Console.WriteLine($"  B vs A：   B {(b.MinMs < a.MinMs ? "更快" : "更慢")} {Math.Abs(a.MinMs - b.MinMs) / 1000:F2}s，" +
                              $"压缩率 {(b.Ratio > a.Ratio ? "更高" : "更低")} {Math.Abs(b.Ratio / a.Ratio * 100 - 100):0.0}%");

            Console.WriteLine();
            Console.WriteLine("  进度遥测（取最快一轮）：");
            Console.WriteLine($"    {"ID",-3} {"采样数",7} {"最大领先",9} {"平均领先",9} {"倒退",6} {"末值",7} {"File%为空",11}");
            foreach (var c in s.Configs)
            {
                var p = c.Progress;
                Console.WriteLine(
                    $"    {c.Id,-3} {p.SampleCount,7} {p.MaxLeadPercent,8:+0.0;-0.0;0.0}pt {p.AvgLeadPercent,8:+0.0;-0.0;0.0}pt " +
                    $"{(p.NonMonotonic ? "是" : "否"),6} {p.FinalPercent,6:F1}% {p.NullFilePercentRatio,10:P0}");
            }
            Console.WriteLine();
            Console.WriteLine("    读法：正领先 = 进度条跑在实际耗时前面（抢跑）；File%为空 比例高 = 该阶段无单文件进度（画不出底纹）。");
        }

        // ---- 跨 degree 对比：仅当扫描了多个组数时输出（G2 验证：degree=N vs degree=1）
        if (opt.Degrees.Length > 1)
        {
            Console.WriteLine();
            Console.WriteLine("###################### 跨 degree 对比 ######################");
            foreach (var group in summaries.GroupBy(s => s.Profile))
            {
                var rows = group.OrderBy(s => s.Degree).ToList();
                int[] degs = rows.Select(s => s.Degree).ToArray();

                Console.WriteLine();
                Console.WriteLine($"■ 语料：{group.Key}");
                Console.WriteLine();
                Console.WriteLine($"  {"ID",-3} {"配置",-20} {string.Join("", degs.Select(d => ("deg=" + d).PadLeft(10)))}   最快组数");
                Console.WriteLine($"  {new string('-', 3),-3} {new string('-', 20),-20} {string.Join("", degs.Select(_ => new string('-', 10).PadLeft(10)))}   --------");

                foreach (var cfg in Configs.All)
                {
                    double[] ms = rows.Select(s => s.Configs.First(c => c.Id == cfg.Id).MinMs).ToArray();
                    int best = Array.IndexOf(ms, ms.Min());
                    string cells = string.Join("", ms.Select(m => ((m / 1000).ToString("F2") + "s").PadLeft(10)));
                    Console.WriteLine($"  {cfg.Id,-3} {cfg.Label,-20} {cells}   {degs[best]}");
                }

                // B/C 相对最小组数的加速比（degree=1 时为单组基线）
                foreach (var id in new[] { "B", "C" })
                {
                    double baseMs = rows[0].Configs.First(c => c.Id == id).MinMs;
                    var ratios = rows.Select(s => s.Configs.First(c => c.Id == id).MinMs / baseMs).ToArray();
                    string chain = string.Join(" → ", rows.Select((s, i) => "deg=" + s.Degree + " " + ratios[i].ToString("0.00") + "x"));
                    bool speedup = ratios.Skip(1).Any(r => r < 0.98);
                    Console.WriteLine($"  {id} 组数收益：{chain}  → {(speedup ? "存在加速，N-group 有效" : "未见明显加速")}");
                }
            }
        }

        PrintVerdict(summaries);
    }

    static void PrintVerdict(List<ProfileSummary> summaries)
    {
        Console.WriteLine();
        Console.WriteLine("###################### 结论 ######################");
        Console.WriteLine();

        foreach (var s in summaries)
        {
            var d = s.Configs.First(c => c.Id == "D");
            var a = s.Configs.First(c => c.Id == "A");
            var b = s.Configs.First(c => c.Id == "B");
            var c = s.Configs.First(c => c.Id == "C");

            // 自动检测「双重压缩」签名：MT 路径产物与不开 MT 字节级一致
            foreach (var mt in new[] { b, c })
            {
                if (mt.CompressedBytes == d.CompressedBytes && mt.CompressedBytes == a.CompressedBytes)
                    Console.WriteLine($"  ⚠ {mt.Id} 产物与 D/A 字节级完全一致（{mt.CompressedBytes} B）" +
                                      $"→ 疑似 7z mt=on 的压缩成果被丢弃（历史上曾由已删除的 MergeTempZipToWriter " +
                                      $"双重压缩导致）；请核对 CompressGroupWithSevenZip 的产物是否被真正写入最终 ZIP");
            }

            Console.WriteLine($"【{s.Profile} degree={s.Degree}】");
            Console.WriteLine($"  仅多线程(B) vs 仅自适应(A)：耗时 {(b.MinMs < a.MinMs ? "更快" : "更慢")} {Math.Abs(a.MinMs - b.MinMs) / 1000:F2}s" +
                              $"（{(a.MinMs / b.MinMs * 100 - 100):+0.0;-0.0;0.0}%），压缩率 {(b.Ratio / a.Ratio * 100 - 100):+0.0;-0.0;0.0}%");
            Console.WriteLine($"  自适应+多线程(C) vs 仅自适应(A)：耗时 {(c.MinMs < a.MinMs ? "更快" : "更慢")} {Math.Abs(a.MinMs - c.MinMs) / 1000:F2}s" +
                              $"（{(a.MinMs / c.MinMs * 100 - 100):+0.0;-0.0;0.0}%），压缩率 {(c.Ratio / a.Ratio * 100 - 100):+0.0;-0.0;0.0}%");

            // 自动判定
            if (b.MinMs > a.MinMs * 1.02 && b.Ratio < a.Ratio * 0.98)
                Console.WriteLine("  → B 两项都劣于 A：确认「仅多线程」是负收益，应在 UI 上警告。");
            else if (b.MinMs > a.MinMs * 1.02)
                Console.WriteLine("  → B 更慢但压缩率相当：多线程本身有开销，未换来收益。");
            else
                Console.WriteLine("  → B 未劣化于 A：本语料下「仅多线程」至少无害。");

            if (c.MinMs < a.MinMs * 0.95)
                Console.WriteLine($"  → C 比 A 快 {Math.Abs(a.MinMs - c.MinMs) / 1000:F2}s 但压缩率仅差 {Math.Abs(c.Ratio / a.Ratio * 100 - 100):0.0}%：多线程有净收益，推荐同开。");
            else if (c.MinMs > a.MinMs * 1.05)
                Console.WriteLine("  → C 明显慢于 A：两趟临时文件 + 合并的 I/O 成本吃掉了并行收益，不建议开启。");
            else
                Console.WriteLine("  → C 与 A 耗时相当：本场景下多线程无净收益，保持关闭即可。");
            Console.WriteLine();
        }

        Console.WriteLine("提示：媒体语料下自适应几乎零成本（已压缩格式直接 Store），");
        Console.WriteLine("      文本语料下自适应 + 多线程是唯一可能产生净收益的组合。");
        Console.WriteLine("      若 C 的收益不稳定，请优先用 --profile text 与更大 --reps 复测。");
    }

    // ---------------------------------------------------------------- JSON

    /// <summary>
    /// 手写 JSON 输出。file-based app 默认禁用反射式序列化
    /// （JsonSerializer.IsReflectionEnabledByDefault = false），
    /// 而源生 JsonSerializerContext 在单文件脚本里得不偿失，故手工拼装。
    /// </summary>
    static void WriteJson(List<ProfileSummary> summaries, string path)
    {
        static string Num(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);
        static string Str(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        var sb = new StringBuilder();
        sb.AppendLine("[");
        for (int i = 0; i < summaries.Count; i++)
        {
            var s = summaries[i];
            sb.AppendLine("  {");
            sb.AppendLine($"    \"profile\": {Str(s.Profile)},");
            sb.AppendLine($"    \"degree\": {s.Degree},");
            sb.AppendLine($"    \"rawBytes\": {s.RawBytes},");
            sb.AppendLine($"    \"fileCount\": {s.FileCount},");
            sb.AppendLine("    \"configs\": [");
            for (int j = 0; j < s.Configs.Count; j++)
            {
                var c = s.Configs[j];
                sb.AppendLine("      {");
                sb.AppendLine($"        \"id\": {Str(c.Id)},");
                sb.AppendLine($"        \"label\": {Str(c.Label)},");
                sb.AppendLine($"        \"minMs\": {Num(c.MinMs)},");
                sb.AppendLine($"        \"medianMs\": {Num(c.MedianMs)},");
                sb.AppendLine($"        \"samplesMs\": [{string.Join(", ", c.AllSamples.Select(Num))}],");
                sb.AppendLine($"        \"compressedBytes\": {c.CompressedBytes},");
                sb.AppendLine($"        \"ratio\": {Num(c.Ratio)},");
                sb.AppendLine($"        \"entryCount\": {c.EntryCount},");
                sb.AppendLine($"        \"archiveValid\": {(c.ArchiveValid ? "true" : "false")},");
                sb.AppendLine($"        \"matchesBaseline\": {(c.MatchesBaseline ? "true" : "false")},");
                sb.AppendLine($"        \"progressSampleCount\": {c.Progress.SampleCount},");
                sb.AppendLine($"        \"progressEntryStatusCount\": {c.Progress.EntryStatusCount},");
                sb.AppendLine($"        \"progressMaxLeadPt\": {Num(c.Progress.MaxLeadPercent)},");
                sb.AppendLine($"        \"progressNonMonotonic\": {(c.Progress.NonMonotonic ? "true" : "false")},");
                sb.AppendLine($"        \"nullFilePercentRatio\": {Num(c.Progress.NullFilePercentRatio)}");
                sb.AppendLine(j < s.Configs.Count - 1 ? "      }," : "      }");
            }
            sb.AppendLine("    ]");
            sb.AppendLine(i < summaries.Count - 1 ? "  }," : "  }");
        }
        sb.AppendLine("]");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"\n已写出 JSON：{path}");
    }
}

// ============================================================================
// 命令行参数
// ============================================================================

sealed class Options
{
    public string? Corpus { get; set; }
    public string? Profile { get; set; }
    public int Reps { get; set; } = 3;
    public int Level { get; set; } = 5;
    public int TextFiles { get; set; } = 200;
    public int TextSizeKb { get; set; } = 250;
    public int MediaFiles { get; set; } = 100;
    public int MediaSizeKb { get; set; } = 500;
    public string? JsonPath { get; set; }
    public string? ReportPath { get; set; }
    public string? SevenZipDll { get; set; }
    public string WorkDir { get; set; } = Path.Combine(Path.GetTempPath(), "mantiszip-bench-mt");
    public bool ShowHelp { get; set; }
    /// <summary>压缩组数扫描维度。缺省 { 0 } = 引擎自动（单维，保持旧行为）。</summary>
    public int[] Degrees { get; set; } = { 0 };

    public static Options Parse(string[] argv)
    {
        var o = new Options();
        for (int i = 0; i < argv.Length; i++)
        {
            string a = argv[i];
            string Next() => i + 1 < argv.Length ? argv[++i] : throw new ArgumentException($"参数 {a} 缺少值");

            switch (a)
            {
                case "--corpus": o.Corpus = Path.GetFullPath(Next()); break;
                case "--profile": o.Profile = Next(); break;
                case "--reps": o.Reps = int.Parse(Next()); break;
                case "--level": o.Level = int.Parse(Next()); break;
                case "--text-files": o.TextFiles = int.Parse(Next()); break;
                case "--text-size-kb": o.TextSizeKb = int.Parse(Next()); break;
                case "--media-files": o.MediaFiles = int.Parse(Next()); break;
                case "--media-size-kb": o.MediaSizeKb = int.Parse(Next()); break;
                case "--json": o.JsonPath = Path.GetFullPath(Next()); break;
                case "--report": o.ReportPath = Path.GetFullPath(Next()); break;
                case "--7z": o.SevenZipDll = Next(); break;
                case "--degrees": o.Degrees = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray(); break;
                case "--workdir": o.WorkDir = Path.GetFullPath(Next()); break;
                case "-h": case "--help": o.ShowHelp = true; break;
                default:
                    // 允许 `dotnet run x.cs -- args` 中混进的分隔符
                    if (a == "--") continue;
                    throw new ArgumentException($"未知参数：{a}");
            }
        }

        if (o.Profile is not null && o.Profile != "text" && o.Profile != "media" && o.Profile != "mixed" && o.Profile != "real")
            throw new ArgumentException($"--profile 只能是 text / media / mixed / real，收到：{o.Profile}");

        if (o.Profile == "real" && o.Corpus is null)
            throw new ArgumentException("--profile real 必须配合 --corpus <dir> 使用");

        if (o.Corpus is not null && o.Profile is not null && o.Profile != "real")
            throw new ArgumentException("指定 --corpus 时请勿再传 --profile（真实语料只跑一组）");

        if (o.Reps < 1) throw new ArgumentException("--reps 至少为 1");
        if (o.Level is < 1 or > 9) throw new ArgumentException("--level 必须在 1-9 之间");
        if (o.Degrees.Length == 0) throw new ArgumentException("--degrees 至少需要一个值（如 1,2,4,8 或 0）");
        if (o.Degrees.Any(d => d < 0)) throw new ArgumentException("--degrees 不能为负数（0 = 引擎自动）");

        return o;
    }

    public static void PrintHelp()
    {
        Console.WriteLine(@"ZIP 多线程压缩基准测试（MantisZip 诊断脚本）

用法:
  dotnet run scripts/bench-zip-mt.cs [-- 选项]

选项:
  --corpus <dir>        用真实文件夹作为语料（跳过合成）
  --profile <name>      只跑指定语料: text | media | mixed（默认全部）
  --reps <n>            计时轮数，默认 3（取最小值）
  --level <1-9>         压缩级别，默认 5
  --degrees <list>      压缩组数扫描：逗号分隔（如 1,2,4,8），0 = 引擎自动；缺省不扫描
  --text-files <n>      文本文件数，默认 200
  --text-size-kb <n>    单个文本文件 KB，默认 250
  --media-files <n>     媒体文件数，默认 100
  --media-size-kb <n>   单个媒体文件 KB，默认 500
  --json <path>         结果另存为 JSON
  --report <path>       完整报告另存为 UTF-8 文本（终端乱码时用这个）
  --7z <path>           显式指定 7z.dll
  --workdir <dir>       工作目录（语料+输出+MT 临时文件），默认 %TEMP%\mantiszip-bench-mt
  -h, --help            显示帮助

环境要求:
  运行前请关闭杀毒实时扫描、网盘同步、其他压缩软件与 IDE 文件索引，
  否则 I/O 计时不可信。");
    }
}

// ============================================================================
// 格式化辅助
// ============================================================================

static class Fmt
{
    public static string Bytes(long b)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double v = b;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:F2}{units[u]}";
    }
}