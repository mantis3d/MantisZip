using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MantisZip.Core.Abstractions;

namespace CorruptProbe;

/// <summary>
/// MantisZip 损坏压缩包探针。
/// 对语料库根目录中的每个样本，走生产同款引擎分发
/// （ArchiveEngineFactory.GetEngineByExtension），依次调用
/// ListEntriesAsync / TestArchiveAsync / ExtractAsync（解压到临时目录），
/// 记录真实行为（条目数 / 异常 / 超时）并输出 markdown 报告。
/// </summary>
internal static class Program
{
    private const int TimeoutSeconds = 60;
    private const int SummaryCellMaxChars = 250;
    private const int ExceptionMessageMaxChars = 300;

    private static readonly string[] ArchiveExtensions =
        [".zip", ".7z", ".rar", ".tar", ".tgz", ".gz", ".iso"];

    /// <summary>数字分卷后缀 .001-.099</summary>
    private static readonly Regex NumericVolumeRegex = new(@"\.(\d{3})$", RegexOptions.Compiled);

    /// <summary>ZIP 分卷后缀 .z01-.z99</summary>
    private static readonly Regex ZipVolumeRegex = new(@"\.z(\d{2})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static async Task<int> Main(string[] args)
    {
        // MantisZip 启动时注册代码页编码（ZIP 中文文件名检测依赖），探针必须保持一致，
        // 否则 SharpCompress 的编码探测可能抛异常。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        string? corpus = null;
        string? outPath = null;
        string[]? only = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--corpus" when i + 1 < args.Length:
                    corpus = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    outPath = args[++i];
                    break;
                case "--only" when i + 1 < args.Length:
                    // 可选：仅探测指定文件名（逗号分隔，精确匹配、忽略大小写）。冒烟测试用。
                    only = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
                default:
                    return Usage($"unexpected or incomplete argument: {args[i]}");
            }
        }

        if (corpus is null || outPath is null) return Usage("--corpus and --out are required");
        if (!Directory.Exists(corpus)) return Usage($"corpus directory not found: {corpus}");

        await RunAsync(corpus, outPath, only);
        return 0;
    }

    private static int Usage(string error)
    {
        Console.Error.WriteLine($"CorruptProbe: {error}");
        Console.Error.WriteLine("Usage: CorruptProbe --corpus <dir> --out <report.md> [--only name1,name2,...]");
        return 2;
    }

    private static async Task RunAsync(string corpus, string outPath, string[]? only)
    {
        // 只扫语料库根目录（TopDirectoryOnly），source\ 等子目录天然跳过
        var candidates = Directory.EnumerateFiles(corpus, "*", SearchOption.TopDirectoryOnly)
            .Where(p => IsProbeCandidate(Path.GetFileName(p)))
            .ToList();

        if (only is { Length: > 0 })
        {
            var requested = new HashSet<string>(only, StringComparer.OrdinalIgnoreCase);
            foreach (var name in requested.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (!candidates.Any(p => string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase)))
                    Console.Error.WriteLine($"WARNING: --only entry not found among corpus candidates: {name}");
            }

            candidates = candidates.Where(p => requested.Contains(Path.GetFileName(p))).ToList();
        }

        candidates.Sort(StringComparer.OrdinalIgnoreCase);

        Console.WriteLine($"Corpus: {corpus}");
        Console.WriteLine($"Probing {candidates.Count} file(s); per-operation timeout {TimeoutSeconds}s");
        Console.WriteLine();

        var rows = new List<ProbeRow>();
        var rawExceptions = new List<RawException>();

        for (var i = 0; i < candidates.Count; i++)
        {
            var path = candidates[i];
            var fileName = Path.GetFileName(path);

            foreach (var password in PasswordsFor(fileName))
            {
                var passwordLabel = password ?? "(none)";
                Console.Write($"[{i + 1}/{candidates.Count}] {fileName} [pw={passwordLabel}] ... ");

                ProbeRow row;
                try
                {
                    row = await ProbeOneAsync(path, fileName, password, rawExceptions);
                }
                catch (Exception ex)
                {
                    // 保险丝：任何未预期的异常只影响该行，绝不中断整轮探测
                    rawExceptions.Add(new RawException(ex.GetType().Name, FullExceptionText(ex), fileName, passwordLabel, "Outer"));
                    row = new ProbeRow(
                        fileName,
                        "?",
                        passwordLabel,
                        $"EXC: {FormatExceptionMessage(ex, ExceptionMessageMaxChars)}",
                        "EXC: see ListEntries",
                        "EXC: see ListEntries");
                }

                rows.Add(row);
                Console.WriteLine($"List={row.List} | Test={row.Test} | Extract={row.Extract}");
            }
        }

        WriteReport(outPath, corpus, rows, rawExceptions);
    }

    private static async Task<ProbeRow> ProbeOneAsync(string path, string fileName, string? password, List<RawException> rawExceptions)
    {
        var passwordLabel = password ?? "(none)";

        // 生产同款分发：扩展名优先 + 魔数兜底
        var engine = ArchiveEngineFactory.GetEngineByExtension(path);
        var engineName = engine?.GetType().Name ?? "null";

        if (engine is null)
        {
            return new ProbeRow(fileName, engineName, passwordLabel,
                "SKIP: no engine", "SKIP: no engine", "SKIP: no engine");
        }

        // 1) 条目列表
        var listOutcome = await InvokeAsync(ct => engine.ListEntriesAsync(path, password, ct));
        var listCell = listOutcome.IsSuccess
            ? FormatListResult(listOutcome.Value!)
            : DescribeFailure(listOutcome, rawExceptions, fileName, passwordLabel, nameof(IArchiveEngine.ListEntriesAsync));

        // 2) 完整性测试
        var testOutcome = await InvokeAsync(ct => engine.TestArchiveAsync(path, password, progress: null, ct));
        var testCell = testOutcome.IsSuccess
            ? (testOutcome.Value ? "true" : "false")
            : DescribeFailure(testOutcome, rawExceptions, fileName, passwordLabel, nameof(IArchiveEngine.TestArchiveAsync));

        // 3) 解压到全新临时目录；无论成败都在 finally 里清理
        var tempDir = Path.Combine(Path.GetTempPath(), "mzprobe", Guid.NewGuid().ToString("N"));
        string extractCell;
        try
        {
            Directory.CreateDirectory(tempDir);
            var extractOutcome = await InvokeAsync(ct => engine.ExtractAsync(path, tempDir, password, progress: null, ct, options: null));
            if (extractOutcome.IsSuccess)
            {
                var result = extractOutcome.Value!;
                var (fileCount, bytes) = DirectoryStats(tempDir);
                extractCell = $"OK: {fileCount} files, {FormatBytes(bytes)}";
                if (result.FailedEntries > 0) extractCell += $"; failed={result.FailedEntries}";
                if (result.SkippedEntries > 0) extractCell += $"; skipped={result.SkippedEntries}";
                if (result.OverwrittenEntries > 0) extractCell += $"; overwritten={result.OverwrittenEntries}";
            }
            else
            {
                extractCell = DescribeFailure(extractOutcome, rawExceptions, fileName, passwordLabel, nameof(IArchiveEngine.ExtractAsync));
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
            catch (Exception cleanupEx)
            {
                Console.Error.WriteLine($"WARNING: temp dir cleanup failed ({tempDir}): {cleanupEx.GetType().Name}: {cleanupEx.Message}");
            }
        }

        return new ProbeRow(fileName, engineName, passwordLabel, listCell, testCell, extractCell);
    }

    /// <summary>per-operation 60s 超时包装：成功返回结果，超时/异常分别归类。</summary>
    private static async Task<OpOutcome<T>> InvokeAsync<T>(Func<CancellationToken, Task<T>> operation)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        try
        {
            var value = await operation(cts.Token);
            return new OpOutcome<T>(true, false, value, null);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return new OpOutcome<T>(false, true, default, null);
        }
        catch (Exception ex)
        {
            return new OpOutcome<T>(false, false, default, ex);
        }
    }

    private static string DescribeFailure<T>(
        OpOutcome<T> outcome,
        List<RawException> rawExceptions,
        string fileName,
        string passwordLabel,
        string operation)
    {
        if (outcome.IsTimeout) return $"TIMEOUT ({TimeoutSeconds}s)";

        var ex = outcome.Error!;
        rawExceptions.Add(new RawException(ex.GetType().Name, FullExceptionText(ex), fileName, passwordLabel, operation));
        return $"EXC: {FormatExceptionMessage(ex, ExceptionMessageMaxChars)}";
    }

    private static string?[] PasswordsFor(string fileName) =>
        fileName.StartsWith("enc-", StringComparison.OrdinalIgnoreCase)
            ? [null, "wrongpw", "mantis"]
            : [null, "mantis"];

    private static bool IsProbeCandidate(string fileName)
    {
        if (fileName.Equals("README.txt", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return false;

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ArchiveExtensions.Contains(ext)) return true;

        // 分卷：.001-.099 或 .z01-.z99
        var numericMatch = NumericVolumeRegex.Match(fileName);
        if (numericMatch.Success && int.TryParse(numericMatch.Groups[1].Value, out var numeric) && numeric is >= 1 and <= 99)
            return true;

        var zipMatch = ZipVolumeRegex.Match(fileName);
        if (zipMatch.Success && int.TryParse(zipMatch.Groups[1].Value, out var zip) && zip is >= 1 and <= 99)
            return true;

        return false;
    }

    private static string FormatListResult(IReadOnlyList<ArchiveItem> entries)
    {
        var keys = entries.Take(3).Select(e => string.IsNullOrEmpty(e.FullPath) ? e.Name : e.FullPath);
        return $"OK: {entries.Count} entries [{string.Join(", ", keys)}]";
    }

    private static (int FileCount, long Bytes) DirectoryStats(string directory)
    {
        if (!Directory.Exists(directory)) return (0, 0);

        var count = 0;
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            count++;
            try { bytes += new FileInfo(file).Length; }
            catch { /* 统计期间的访问竞争可忽略大小 */ }
        }

        return (count, bytes);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        return (bytes / (1024.0 * 1024 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>异常 → `TypeName: message`（消息截断到 maxMessageLength，换行压平）。</summary>
    private static string FormatExceptionMessage(Exception ex, int maxMessageLength)
    {
        var message = NormalizeWhitespace(ex.Message);
        if (message.Length > maxMessageLength) message = message[..maxMessageLength] + "...";
        return $"{ex.GetType().Name}: {message}";
    }

    /// <summary>完整异常链文本（TypeName: message | INNER ...），消息不截断。</summary>
    private static string FullExceptionText(Exception ex)
    {
        var sb = new StringBuilder();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (sb.Length > 0) sb.Append(" | INNER ");
            sb.Append(e.GetType().Name).Append(": ").Append(NormalizeWhitespace(e.Message));
        }
        return sb.ToString();
    }

    private static string NormalizeWhitespace(string? text) =>
        WhitespaceRegex.Replace((text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' '), " ").Trim();

    /// <summary>markdown 表格单元格：压平换行、转义 |、限制 250 字符。</summary>
    private static string Cell(string text)
    {
        var value = NormalizeWhitespace(text).Replace("|", "\\|");
        if (value.Length > SummaryCellMaxChars) value = value[..(SummaryCellMaxChars - 3)] + "...";
        return value;
    }

    private static void WriteReport(string outPath, string corpus, IReadOnlyList<ProbeRow> rows, IReadOnlyList<RawException> rawExceptions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Corrupt Archives Probe Results");
        sb.AppendLine($"Generated: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine($"Corpus: `{corpus}`");
        sb.AppendLine();
        sb.AppendLine("## Summary table");
        sb.AppendLine();
        sb.AppendLine("| File | Engine | Password | ListEntries | TestArchive | Extract |");
        sb.AppendLine("|------|--------|----------|-------------|-------------|---------|");

        foreach (var row in rows)
        {
            sb.Append("| ").Append(Cell(row.File))
              .Append(" | ").Append(Cell(row.Engine))
              .Append(" | ").Append(Cell(row.Password))
              .Append(" | ").Append(Cell(row.List))
              .Append(" | ").Append(Cell(row.Test))
              .Append(" | ").Append(Cell(row.Extract))
              .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## Raw exceptions");
        sb.AppendLine();

        if (rawExceptions.Count == 0)
        {
            sb.AppendLine("_(none observed)_");
        }
        else
        {
            var groups = rawExceptions
                .GroupBy(r => r.FullText, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.First().TypeKey, StringComparer.Ordinal)
                .ToList();

            var index = 0;
            foreach (var group in groups)
            {
                index++;
                var first = group.First();
                var seenAt = group
                    .Select(g => $"{g.File} [pw={g.Password}] {g.Operation}")
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(s => s, StringComparer.Ordinal);

                sb.AppendLine($"### {index}. {first.TypeKey} — {group.Count()} occurrence(s)");
                sb.AppendLine();
                sb.AppendLine($"- Full: {first.FullText}");
                sb.AppendLine($"- Seen at: {string.Join("; ", seenAt)}");
                sb.AppendLine();
            }
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine();
        Console.WriteLine($"Report written: {outPath} ({rows.Count} rows, {rawExceptions.Count} exception records)");
    }

    private sealed record ProbeRow(string File, string Engine, string Password, string List, string Test, string Extract);

    private sealed record RawException(string TypeKey, string FullText, string File, string Password, string Operation);

    private sealed record OpOutcome<T>(bool IsSuccess, bool IsTimeout, T? Value, Exception? Error);
}
