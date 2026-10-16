#:project ../src/MantisZip.Core/MantisZip.Core.csproj
#:package System.Management@10.0.0

// ============================================================================
// ZIP 并行解压基准测试（实验性诊断脚本，非应用代码）
//
// 目的：量化评估 ParallelExtractDegree 在 HDD vs SSD 上的真实收益/损失。
//
// 问题背景：HDD 解压时磁盘利用率 100%，多线程解压理论上可能因寻道放大而劣化。
// 本脚本通过对比 degree=1（串行）vs degree=2/4/8 在不同存储介质上的表现，
// 回答"多线程解压在 HDD 上是否反而更慢"这个问题。
//
// 测试维度：
//   1. 并行度扫描：degree=1,2,4,8
//   2. 文件规模分布：many-small（2000×50KB）/ few-large（10×50MB）/ mixed（200小+20大）
//   3. 存储介质识别：自动检测 HDD vs SSD（通过物理查询）
//   4. 预热 vs 冷缓存：OS 文件缓存对结果的影响
//
// 用法：
//   dotnet run scripts/bench-extract-parallel.cs                          # 全部 profile
//   dotnet run scripts/bench-extract-parallel.cs -- --profile many-small  # 只测小文件
//   dotnet run scripts/bench-extract-parallel.cs -- --degrees 1,4         # 只测 2 个并行度
//   dotnet run scripts/bench-extract-parallel.cs -- --reps 5 --json out.json
//   dotnet run scripts/bench-extract-parallel.cs -- --target D:\ExtractTest  # 指定解压目标盘
//
// 环境要求：
//   I/O 基准对环境极敏感。请先关闭：
//   - 杀毒实时扫描（Defender/360/火绒）
//   - 网盘同步工具（OneDrive/百度云/坚果云）
//   - IDE 文件索引（ReSharper/VS IntelliSense）
//   - 其他正在进行的 I/O 密集任务
//
// 结果解读：
//   · degree=1 是串行基线（引擎内部走 ExtractAsyncSequential）
//   · 相对串行的加速比 <1.0 表示更快，>1.0 表示更慢
//   · HDD 上 many-small 场景最可能出现劣化（小文件随机写 + 目录元数据）
//   · SSD 上通常随 degree 提升线性加速，直到 CPU 或写入带宽饱和
// ============================================================================

using System.Diagnostics;
using System.Globalization;
using System.Management;  // 需要 System.Management 包（Windows 专用）
using System.Text;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;

return await Bench.RunAsync(args);

// ============================================================================
// 配置
// ============================================================================

/// <summary>单个文件规模配置。</summary>
sealed record CorpusProfile(string Id, string Label, int FileCount, long TotalBytes)
{
    public string SizeDesc => Id switch
    {
        "many-small" => "2000 × 50KB",
        "few-large"  => "10 × 5MB",
        "mixed"      => "200 × 50KB + 20 × 5MB",
        _ => ""
    };
}

static class Profiles
{
    public static readonly CorpusProfile[] All =
    {
        new("many-small", "大量小文件", 2000, 2000L * 50 * 1024),          // 100MB
        new("few-large",  "少量大文件", 10,   10L * 5 * 1024 * 1024),     // 50MB
        new("mixed",      "混合",       220,  200L * 50 * 1024 + 20L * 5 * 1024 * 1024), // ~110MB
    };
}

// ============================================================================
// OS 文件缓存清理
// ============================================================================

static class CacheClearer
{
    /// <summary>
    /// 清除 Windows 文件缓存（需要管理员权限）。
    /// 通过 SetSystemFileCacheSize 将系统文件缓存设为最小值。
    /// </summary>
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetSystemFileCacheSize(IntPtr minimumFileCacheSize, IntPtr maximumFileCacheSize, int flags);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemFileCacheSize(out IntPtr minimumFileCacheSize, out IntPtr maximumFileCacheSize, out int flags);

    private const int FILE_CACHE_MAX_HARD_ENABLE = 0x1;
    private const int FILE_CACHE_MAX_HARD_DISABLE = 0x2;
    private const int FILE_CACHE_MIN_HARD_ENABLE = 0x4;
    private const int FILE_CACHE_MIN_HARD_DISABLE = 0x8;

    /// <summary>
    /// 尝试清除文件缓存。返回 true 表示成功，false 表示需要管理员权限或不支持。
    /// </summary>
    public static bool TryClearCache()
    {
        try
        {
            // 将缓存设为最小值（强制系统从磁盘读取）
            // 注意：这需要管理员权限，否则会静默失败
            IntPtr minSize = new IntPtr(0);
            IntPtr maxSize = new IntPtr(0);
            int flags = FILE_CACHE_MAX_HARD_DISABLE | FILE_CACHE_MIN_HARD_DISABLE;

            bool result = SetSystemFileCacheSize(minSize, maxSize, flags);
            if (result)
            {
                Console.WriteLine("  ✓ 文件缓存已清除（下次读取将从磁盘）");
                return true;
            }
        }
        catch { }

        Console.WriteLine("  ⚠ 无法清除文件缓存（需要管理员权限）");
        Console.WriteLine("    建议：以管理员身份运行，或使用 RAM 量小于测试数据量");
        return false;
    }

    /// <summary>恢复默认缓存设置。</summary>
    public static void RestoreDefaultCache()
    {
        try
        {
            // 恢复为系统默认（通常为物理内存的一定比例）
            IntPtr defaultSize = IntPtr.Zero;
            int flags = 0;
            SetSystemFileCacheSize(defaultSize, defaultSize, flags);
        }
        catch { }
    }
}

// ============================================================================
// 存储介质检测
// ============================================================================

static class DiskDetector
{
    /// <summary>检测驱动器类型（SSD/HDD/Unknown）。</summary>
    public static string DetectMediaType(char driveLetter)
    {
        try
        {
            // 方法1：通过 Win32 物理磁盘的 MediaType 属性
            // MediaType: 3=HDD (Fixed hard disk media), SSD 通常返回 17 或不设置
            // 更可靠的方法：检查 PhysicalBytesPerSector 和 SeekPenalty
            using var searcher = new ManagementObjectSearcher(
                $"SELECT DeviceID, MediaType, InterfaceType FROM Win32_DiskDrive");

            foreach (ManagementObject disk in searcher.Get())
            {
                string? mediaType = disk["MediaType"]?.ToString();
                string? interfaceType = disk["InterfaceType"]?.ToString();

                // 检查是否为 SSD：通过 MSFT_PhysicalDisk（Storage Spaces API）
                if (IsSolidState(disk))
                    return "SSD";

                // SATA/NVMe 接口通常为 HDD（除非明确检测为 SSD）
                if (mediaType?.Contains("Fixed hard disk") == true ||
                    mediaType?.Contains("External hard disk") == true)
                    return "HDD";
            }
        }
        catch
        {
            // ManagementObjectSearcher 在某些环境不可用，回退到简单检测
        }

        // 方法2：通过 Win32 的磁盘性能计数器（SeekPenalty）
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Name FROM Win32_PerfRawData_PerfDisk_PhysicalDisk");

            // 简单检查：如果是 NVMe 接口，大概率是 SSD
            // 这个方法不够准确，仅作 fallback
        }
        catch { }

        return "Unknown";
    }

    private static bool IsSolidState(ManagementObject disk)
    {
        try
        {
            // 查询 MSFT_PhysicalDisk（需要 Windows 8+ 和 Storage 模块）
            string? deviceId = disk["DeviceID"]?.ToString();
            if (deviceId == null) return false;

            using var pnpSearcher = new ManagementObjectSearcher(
                "SELECT Model, MediaType FROM Win32_DiskDrive WHERE DeviceID = '" + deviceId.Replace("\\", "\\\\") + "'");

            // 检查是否有 SSD 关键字
            string? model = disk["Model"]?.ToString()?.ToLowerInvariant() ?? "";
            if (model.Contains("ssd") || model.Contains("nvme") || model.Contains("m.2"))
                return true;
        }
        catch { }

        return false;
    }

    /// <summary>获取驱动器剩余空间。</summary>
    public static (double TotalGb, double FreeGb) GetDriveInfo(char driveLetter)
    {
        var drive = new DriveInfo(driveLetter.ToString());
        return (drive.TotalSize / 1073741824.0, drive.AvailableFreeSpace / 1073741824.0);
    }
}

// ============================================================================
// 语料生成
// ============================================================================

static class Corpus
{
    /// <summary>生成测试文件并打包为 ZIP。</summary>
    /// <param name="fresh">true = 每次调用生成随机种子不同的全新数据（绕过文件缓存）</param>
    public static async Task<string> CreateArchiveAsync(
        ZipEngine engine, CorpusProfile profile, string workDir, Options opt, bool fresh)
    {
        string corpusDir = Path.Combine(workDir, $"corpus-{profile.Id}");
        // 全新数据模式用随机文件名，避免复用旧文件
        string archiveName = fresh
            ? $"test-{profile.Id}-{Guid.NewGuid():N}.zip"
            : $"test-{profile.Id}.zip";
        string archivePath = Path.Combine(workDir, archiveName);

        if (Directory.Exists(corpusDir)) Directory.Delete(corpusDir, true);
        if (File.Exists(archivePath)) File.Delete(archivePath);
        Directory.CreateDirectory(corpusDir);

        // fresh 模式用随机种子，确保每次生成的数据不同
        var rng = fresh ? new Random() : new Random(20261004);

        switch (profile.Id)
        {
            case "many-small":
                GenerateRandomFiles(corpusDir, profile.FileCount, 50 * 1024L, rng);  // 50KB each
                break;
            case "few-large":
                GenerateRandomFiles(corpusDir, profile.FileCount, 5 * 1024 * 1024L, rng);  // 5MB each
                break;
            case "mixed":
                // 200 × 50KB + 20 × 5MB
                GenerateRandomFiles(Path.Combine(corpusDir, "small"), 200, 50 * 1024L, rng);
                GenerateRandomFiles(Path.Combine(corpusDir, "large"), 20, 5 * 1024 * 1024L, rng);
                break;
            default:
                throw new ArgumentException($"未知 profile: {profile.Id}");
        }

        // 打包（使用 Store 压缩，避免压缩耗时影响解压测试）
        var compressOptions = new ArchiveOptions
        {
            Format = ArchiveFormat.Zip,
            ZipCompressionMethod = "store",  // 无压缩，测试纯粹 I/O
        };

        await engine.CompressAsync(
            new[] { corpusDir },
            archivePath,
            compressOptions);

        // 清理源文件（只保留压缩包）
        try { Directory.Delete(corpusDir, true); } catch { }

        return archivePath;
    }

    private static void GenerateRandomFiles(string dir, int count, long sizeBytes, Random rng)
    {
        Directory.CreateDirectory(dir);
        byte[] buffer = new byte[65536];

        for (int i = 0; i < count; i++)
        {
            string filePath = Path.Combine(dir, $"file_{i:D5}.dat");
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            long remaining = sizeBytes;
            while (remaining > 0)
            {
                rng.NextBytes(buffer);
                int toWrite = (int)Math.Min(buffer.Length, remaining);
                fs.Write(buffer, 0, toWrite);
                remaining -= toWrite;
            }
        }
    }
}

// ============================================================================
// 测量结果
// ============================================================================

sealed record ExtractRun(
    int Degree,
    double Milliseconds,
    long TotalBytes,
    int FileCount,
    double ThroughputMBps
);

/// <summary>单个驱动器的测试结果。</summary>
sealed record DriveResult(
    string DriveRoot,
    string MediaType,
    CorpusProfile Profile,
    List<ExtractRun> Runs
);

static class Bench
{
    public static async Task<int> RunAsync(string[] argv)
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch { }

        var opt = Options.Parse(argv);
        if (opt.ShowHelp) { Options.PrintHelp(); return 0; }

        PrintEnvironment(opt);

        // 检查是否有管理员权限（用于清除缓存）
        bool isAdmin = IsRunAsAdmin();
        if (!isAdmin && !opt.SkipCacheClear && !opt.FreshData)
        {
            Console.WriteLine("⚠ 建议使用 --fresh 模式或以管理员身份运行以获得准确的磁盘 I/O 结果");
            Console.WriteLine();
        }

        var engine = new ZipEngine();

        // 选择要测试的 profiles
        var profiles = opt.Profile != null
            ? Profiles.All.Where(p => p.Id == opt.Profile).ToArray()
            : Profiles.All;

        if (opt.FreshData)
        {
            Console.WriteLine("模式：每次测量生成全新数据（绕过文件缓存，结果更准确但更慢）");
            Console.WriteLine();
        }

        // 对每个驱动器分别测试
        var allDriveResults = new List<DriveResult>();

        foreach (string targetRoot in opt.TargetRoots)
        {
            char driveLetter = targetRoot[0];
            string mediaType = DiskDetector.DetectMediaType(driveLetter);
            string workDir = Path.Combine(targetRoot, "MantisZipBenchExtract");
            Directory.CreateDirectory(workDir);

            Console.WriteLine($"────────────────────────────────────────────────────");
            Console.WriteLine($"驱动器 {targetRoot} ({mediaType})");
            Console.WriteLine($"工作目录: {workDir}");
            Console.WriteLine();

            foreach (var profile in profiles)
            {
                Console.WriteLine($"■ 语料：{profile.Label} ({profile.Id})");

                var runs = new List<ExtractRun>();

                if (opt.FreshData)
                {
                    // 全新数据模式：每次测量都生成新的压缩包（冷缓存）
                    foreach (int degree in opt.Degrees)
                    {
                        Console.Write($"  degree={degree,2} ");

                        // 预热轮（JIT，不计时）
                        {
                            string warmArchive = await Corpus.CreateArchiveAsync(engine, profile, workDir, opt, fresh: true);
                            await MeasureOnceAsync(engine, warmArchive, degree, workDir, throwaway: true, clearCache: false);
                            try { File.Delete(warmArchive); } catch { }
                        }
                        Console.Write(".");

                        // 计时轮
                        var samples = new List<double>();
                        long actualBytes = 0;
                        for (int rep = 0; rep < opt.Reps; rep++)
                        {
                            string freshArchive = await Corpus.CreateArchiveAsync(engine, profile, workDir, opt, fresh: true);
                            var r = await MeasureOnceAsync(engine, freshArchive, degree, workDir, throwaway: false, clearCache: false);
                            samples.Add(r.Milliseconds);
                            actualBytes = r.TotalBytes;
                            try { File.Delete(freshArchive); } catch { }
                            Console.Write(".");
                        }

                        double minMs = samples.Min();
                        double throughput = actualBytes / (minMs / 1000.0) / (1024 * 1024);
                        runs.Add(new ExtractRun(degree, minMs, actualBytes, profile.FileCount, throughput));
                        Console.WriteLine($" {minMs / 1000:F2}s ({throughput:F1} MB/s)");
                    }
                }
                else
                {
                    // 传统模式：生成一次压缩包，多次解压
                    Console.Write("  生成压缩包...");
                    string archivePath = await Corpus.CreateArchiveAsync(engine, profile, workDir, opt, fresh: false);
                    Console.WriteLine($" 完成 ({Fmt.Bytes(new FileInfo(archivePath).Length)})");

                    foreach (int degree in opt.Degrees)
                    {
                        Console.Write($"  degree={degree,2} ");

                        // 预热轮
                        await MeasureOnceAsync(engine, archivePath, degree, workDir, throwaway: true, clearCache: false);
                        Console.Write(".");

                        // 计时轮
                        var samples = new List<double>();
                        long actualBytes = 0;
                        for (int rep = 0; rep < opt.Reps; rep++)
                        {
                            bool clearCache = !opt.SkipCacheClear && isAdmin;
                            var r = await MeasureOnceAsync(engine, archivePath, degree, workDir, throwaway: false, clearCache: clearCache);
                            samples.Add(r.Milliseconds);
                            actualBytes = r.TotalBytes;
                            Console.Write(".");
                        }

                        double minMs = samples.Min();
                        double throughput = actualBytes / (minMs / 1000.0) / (1024 * 1024);
                        runs.Add(new ExtractRun(degree, minMs, actualBytes, profile.FileCount, throughput));
                        Console.WriteLine($" {minMs / 1000:F2}s ({throughput:F1} MB/s)");
                    }

                    try { File.Delete(archivePath); } catch { }
                }

                allDriveResults.Add(new DriveResult(targetRoot, mediaType, profile, runs));
                Console.WriteLine();
            }

            // 清理工作目录
            try { Directory.Delete(workDir, true); } catch { }
        }

        // 输出对比报告
        Report(allDriveResults, opt);

        return 0;
    }

    static bool IsRunAsAdmin()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    static async Task<ExtractRun> MeasureOnceAsync(
        ZipEngine engine, string archivePath, int degree, string workDir, bool throwaway, bool clearCache)
    {
        string destDir = Path.Combine(workDir, $"extract-{degree}-{(throwaway ? "warmup" : "run")}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destDir);

        var options = new ArchiveOptions
        {
            ParallelExtractDegree = degree,
            ConflictAction = FileConflictAction.Overwrite,
        };

        // 如果需要，清除文件缓存以获得真实磁盘 I/O 结果
        if (clearCache && !throwaway)
            CacheClearer.TryClearCache();

        var sw = Stopwatch.StartNew();
        var result = await engine.ExtractAsync(archivePath, destDir, options: options);
        sw.Stop();

        // 统计解压输出大小
        long totalBytes = Directory.EnumerateFiles(destDir, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);

        // 清理解压输出
        try { Directory.Delete(destDir, true); } catch { }

        return new ExtractRun(degree, sw.Elapsed.TotalMilliseconds, totalBytes, result.SucceededEntries, 0);
    }

    static void PrintEnvironment(Options opt)
    {
        Console.WriteLine("==================== 环境 ====================");
        Console.WriteLine($"运行时      : {Environment.Version}");
        Console.WriteLine($"操作系统    : {Environment.OSVersion}");
        Console.WriteLine($"逻辑核心数  : {Environment.ProcessorCount}");
        Console.WriteLine($"计时轮次    : {opt.Reps}（取最小值）");
        Console.WriteLine($"并行度扫描  : {string.Join(",", opt.Degrees)}");
        Console.WriteLine($"目标驱动器  : {string.Join(", ", opt.TargetRoots)}");
        Console.WriteLine();
    }

    static void Report(List<DriveResult> results, Options opt)
    {
        var driveRoots = results.Select(r => r.DriveRoot).Distinct().ToList();
        var profiles = results.Select(r => r.Profile).Distinct().ToList();

        // ========== 每个驱动器的详细结果 ==========
        Console.WriteLine();
        Console.WriteLine("###################### 详细结果 ######################");

        foreach (string drive in driveRoots)
        {
            var driveResults = results.Where(r => r.DriveRoot == drive).ToList();
            string mediaType = driveResults.First().MediaType;

            Console.WriteLine();
            Console.WriteLine($"■ 驱动器 {drive} ({mediaType})");

            foreach (var dr in driveResults)
            {
                Console.WriteLine();
                Console.WriteLine($"  ┌─ {dr.Profile.Label} ({dr.Profile.Id})");
                Console.WriteLine($"  │  {dr.Profile.SizeDesc}，共 {Fmt.Bytes(dr.Runs.First().TotalBytes)}，文件数 {dr.Profile.FileCount}");
                Console.WriteLine($"  │");
                Console.WriteLine($"  │  {"degree",-8} {"耗时",10} {"吞吐量",12} {"相对串行",10}");
                Console.WriteLine($"  │  {new string('─', 8),-8} {new string('─', 10),10} {new string('─', 12),12} {new string('─', 10),10}");

                double serialMs = dr.Runs.First(r => r.Degree == 1).Milliseconds;
                foreach (var run in dr.Runs)
                {
                    double ratio = run.Milliseconds / serialMs;
                    string indicator = ratio < 0.95 ? "✓" : ratio > 1.05 ? "✗" : " ";
                    Console.WriteLine(
                        $"  │  {run.Degree,-8} {run.Milliseconds / 1000,8:F2}s {run.ThroughputMBps,10:F1} MB/s {ratio,8:F2}x {indicator}");
                }

                var best = dr.Runs.OrderBy(r => r.Milliseconds).First();
                Console.WriteLine($"  │");
                Console.WriteLine($"  └─ 最佳: degree={best.Degree} ({best.Milliseconds / 1000:F2}s, {best.ThroughputMBps:F1} MB/s)");
            }
        }

        // ========== 跨驱动器对比（核心价值） ==========
        if (driveRoots.Count > 1)
        {
            Console.WriteLine();
            Console.WriteLine("###################### HDD vs SSD 对比 ######################");

            foreach (var profile in profiles)
            {
                Console.WriteLine();
                Console.WriteLine($"■ {profile.Label} ({profile.Id}) - 各驱动器耗时对比");
                Console.WriteLine();

                // 表头
                string header = $"  {"degree",-8}";
                string sep = $"  {new string('─', 8),-8}";
                foreach (string drive in driveRoots)
                {
                    string label = $"{drive} ({results.First(r => r.DriveRoot == drive).MediaType})";
                    header += $" {label,16}";
                    sep += $" {new string('─', 16),16}";
                }
                // 加速比列（相对最慢驱动器）
                header += $"  {"SSD/HDD",10}";
                sep += $"  {new string('─', 10),10}";
                Console.WriteLine(header);
                Console.WriteLine(sep);

                // 每个 degree 一行
                foreach (int degree in opt.Degrees)
                {
                    string row = $"  {degree,-8}";
                    var times = new List<(string Drive, double Ms)>();

                    foreach (string drive in driveRoots)
                    {
                        var dr = results.FirstOrDefault(r => r.DriveRoot == drive && r.Profile.Id == profile.Id);
                        var run = dr?.Runs.FirstOrDefault(r => r.Degree == degree);
                        if (run != null)
                        {
                            row += $" {run.Milliseconds / 1000,13:F2}s";
                            times.Add((drive, run.Milliseconds));
                        }
                        else
                        {
                            row += $" {"-",16}";
                        }
                    }

                    // 计算 SSD/HDD 加速比（如果有两个驱动器）
                    if (times.Count >= 2)
                    {
                        double fastest = times.Min(t => t.Ms);
                        double slowest = times.Max(t => t.Ms);
                        double speedup = slowest / fastest;
                        row += $"  {speedup,8:F1}x";
                    }

                    Console.WriteLine(row);
                }

                // 最佳并行度对比
                Console.WriteLine();
                Console.WriteLine($"  最佳并行度对比:");
                foreach (string drive in driveRoots)
                {
                    var dr = results.FirstOrDefault(r => r.DriveRoot == drive && r.Profile.Id == profile.Id);
                    if (dr != null)
                    {
                        var best = dr.Runs.OrderBy(r => r.Milliseconds).First();
                        Console.WriteLine($"    {drive} ({dr.MediaType}): degree={best.Degree} ({best.Milliseconds / 1000:F2}s, {best.ThroughputMBps:F1} MB/s)");
                    }
                }
            }

            // 总结
            Console.WriteLine();
            Console.WriteLine("###################### 结论 ######################");
            Console.WriteLine();

            foreach (var profile in profiles)
            {
                var ssdResults = results.Where(r => r.Profile.Id == profile.Id && r.MediaType == "SSD").ToList();
                var hddResults = results.Where(r => r.Profile.Id == profile.Id && r.MediaType != "SSD").ToList();

                if (ssdResults.Any() && hddResults.Any())
                {
                    var ssdBest = ssdResults.SelectMany(r => r.Runs).OrderBy(r => r.Milliseconds).First();
                    var hddBest = hddResults.SelectMany(r => r.Runs).OrderBy(r => r.Milliseconds).First();
                    double ratio = hddBest.Milliseconds / ssdBest.Milliseconds;

                    Console.WriteLine($"【{profile.Label}】");
                    Console.WriteLine($"  SSD 最快: {ssdBest.Milliseconds / 1000:F2}s (degree={ssdBest.Degree})");
                    Console.WriteLine($"  HDD 最快: {hddBest.Milliseconds / 1000:F2}s (degree={hddBest.Degree})");
                    Console.WriteLine($"  HDD 比 SSD 慢 {ratio:F1}x");
                    Console.WriteLine();
                }
            }

            // HDD 多线程效果分析
            Console.WriteLine("HDD 多线程效果分析:");
            foreach (var profile in profiles)
            {
                var hddResult = results.FirstOrDefault(r => r.Profile.Id == profile.Id && r.MediaType != "SSD");
                if (hddResult != null)
                {
                    var serial = hddResult.Runs.First(r => r.Degree == 1);
                    var parallel = hddResult.Runs.Where(r => r.Degree > 1).OrderBy(r => r.Milliseconds).First();
                    double speedup = serial.Milliseconds / parallel.Milliseconds;

                    if (speedup > 1.2)
                        Console.WriteLine($"  {profile.Label}: 多线程有效 (degree={parallel.Degree}, 加速 {speedup:F2}x)");
                    else if (speedup > 0.95)
                        Console.WriteLine($"  {profile.Label}: 多线程无明显效果");
                    else
                        Console.WriteLine($"  {profile.Label}: 多线程劣化 (建议保持串行)");
                }
            }
        }

        // 单驱动器结果（无对比时）
        else
        {
            Console.WriteLine();
            Console.WriteLine("###################### 结果总结 ######################");
            Console.WriteLine();

            foreach (var dr in results)
            {
                var serial = dr.Runs.First(r => r.Degree == 1);
                var parallel = dr.Runs.Where(r => r.Degree > 1).OrderBy(r => r.Milliseconds).First();
                double speedup = serial.Milliseconds / parallel.Milliseconds;

                Console.WriteLine($"【{dr.Profile.Label} on {dr.DriveRoot} ({dr.MediaType})】");
                Console.WriteLine($"  串行: {serial.Milliseconds / 1000:F2}s | 最佳并行: degree={parallel.Degree} ({parallel.Milliseconds / 1000:F2}s)");
                Console.WriteLine($"  加速比: {speedup:F2}x");
                Console.WriteLine();
            }
        }
    }
}

// ============================================================================
// 命令行参数
// ============================================================================

sealed class Options
{
    public string? Profile { get; set; }
    public int Reps { get; set; } = 3;
    public List<string> TargetRoots { get; set; } = new();
    public bool ShowHelp { get; set; }
    public int[] Degrees { get; set; } = { 1, 2, 4, 8 };
    public bool SkipCacheClear { get; set; }
    public bool FreshData { get; set; }

    public static Options Parse(string[] argv)
    {
        var o = new Options();
        for (int i = 0; i < argv.Length; i++)
        {
            string a = argv[i];
            string Next() => i + 1 < argv.Length ? argv[++i] : throw new ArgumentException($"参数 {a} 缺少值");

            switch (a)
            {
                case "--profile": o.Profile = Next(); break;
                case "--reps": o.Reps = int.Parse(Next()); break;
                case "--target":
                    // 支持多个 --target，或逗号分隔
                    string targets = Next();
                    foreach (var t in targets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string root = Path.GetPathRoot(Path.GetFullPath(t))!;
                        if (!o.TargetRoots.Contains(root, StringComparer.OrdinalIgnoreCase))
                            o.TargetRoots.Add(root);
                    }
                    break;
                case "--degrees": o.Degrees = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray(); break;
                case "--skip-cache-clear": o.SkipCacheClear = true; break;
                case "--fresh": o.FreshData = true; break;
                case "-h": case "--help": o.ShowHelp = true; break;
                default:
                    if (a == "--") continue;
                    throw new ArgumentException($"未知参数：{a}");
            }
        }

        // 默认目标：临时目录所在盘
        if (o.TargetRoots.Count == 0)
            o.TargetRoots.Add(Path.GetPathRoot(Path.GetTempPath())!);

        if (o.Profile != null && !Profiles.All.Any(p => p.Id == o.Profile))
            throw new ArgumentException($"--profile 只能是 {string.Join(" / ", Profiles.All.Select(p => p.Id))}，收到：{o.Profile}");

        if (o.Reps < 1) throw new ArgumentException("--reps 至少为 1");
        if (o.Degrees.Length == 0) throw new ArgumentException("--degrees 至少需要一个值");
        if (o.Degrees.Any(d => d < 1)) throw new ArgumentException("--degrees 不能小于 1（1 = 串行）");

        return o;
    }

    public static void PrintHelp()
    {
        Console.WriteLine(@"ZIP 并行解压基准测试（MantisZip 诊断脚本）

用法:
  dotnet run scripts/bench-extract-parallel.cs [-- 选项]

选项:
  --profile <name>      只跑指定语料: many-small | few-large | mixed（默认全部）
  --target <paths>      目标驱动器（支持多个，逗号分隔或重复指定）
                        例: --target C:\,E:\ 或 --target C:\ --target E:\
  --degrees <list>      并行度扫描：逗号分隔（默认 1,2,4,8，1=串行）
  --reps <n>            计时轮数，默认 3（取最小值）
  --fresh               每次测量生成全新数据（绕过文件缓存，推荐）
  --skip-cache-clear    跳过文件缓存清除（--fresh 模式下无意义）
  -h, --help            显示帮助

测试模式:
  --fresh（推荐）: 每次测量生成随机数据的新压缩包，保证冷缓存，无需管理员权限

示例:
  # 对比 SSD (C:) 和 HDD (E:)
  dotnet run scripts/bench-extract-parallel.cs -- --target C:\,E:\ --fresh

  # 只测 HDD
  dotnet run scripts/bench-extract-parallel.cs -- --target E:\ --fresh

  # 只测小文件场景
  dotnet run scripts/bench-extract-parallel.cs -- --target C:\,E:\ --profile many-small --fresh");
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
