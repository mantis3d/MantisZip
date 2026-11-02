using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 「元数据卡」取数服务：把**当前选中**的那个条目取出、解析成卡片文字行。
///
/// <para>
/// <b>只服务选中项</b>（v0.5.6 决策）：卡片布局里每张卡都读一遍条目代价太大（等于再叠一遍缩略图
/// 的 I/O），所以只在用户选中某个条目时取一次，结果显示在那张卡上。改选下一个条目时旧卡的文字被清掉。
/// </para>
/// <para>
/// <b>为什么先落临时文件再解析</b>：Core 的格式解析器（<see cref="FlacParser"/>、
/// <see cref="VideoParser"/>、<see cref="FontParser"/>、<see cref="PeParser"/> 等）全都只接受
/// <c>filePath</c>，没有流或 byte[] 重载——它们是在「预览已经落盘」的前提上写的。给这些解析器加
/// 重载是更大的改动面，而选中项一次只处理一个文件，落盘一次的代价可以接受。
/// </para>
/// </summary>
internal static class MetadataCardService
{
    /// <summary>
    /// 允许解析的最大条目体积。超过就不出元数据卡（回退大图标）——防止在压缩包里点到一个
    /// 2 GB 的视频就把内存/临时盘吃满；卡片的文字价值不值得这个代价。
    /// </summary>
    internal const long MaxEntryBytes = 64L * 1024 * 1024;

    private static readonly object TempGate = new();
    private static string? _lastTempFile;

    /// <summary>
    /// 取并解析条目的元数据，返回卡片文本行；没有可展示元数据时返回空集合。
    /// <para>异常一律吞掉并记一行日志：卡片缺一段文字不该让整个列表报错。</para>
    /// </summary>
    public static async Task<IReadOnlyList<string>> LoadLinesAsync(
        string archivePath,
        ArchiveItemModel entry,
        ArchiveFormat format,
        string? password,
        CancellationToken ct)
    {
        var kind = MetadataCardFormatter.Classify(entry.Name);
        if (kind == MetadataCardKind.None)
            return [];

        if (entry.Size > MaxEntryBytes)
        {
            App.DebugLog($"[META] skip '{entry.Name}': {entry.Size} bytes > {MaxEntryBytes}");
            return [];
        }

        string? tempFile = null;
        try
        {
            tempFile = await ExtractToTempAsync(archivePath, entry, format, password, ct);
            if (tempFile == null)
                return [];

            var info = Parse(kind, tempFile, Path.GetExtension(entry.Name).ToLowerInvariant());
            return MetadataCardFormatter.BuildLines(kind, info, MetadataCardLabels.FromLocalization());
        }
        catch (OperationCanceledException)
        {
            // 改选下一个条目会取消这次取数，属正常流程
            return [];
        }
        catch (Exception ex)
        {
            App.DebugLog($"[META] failed for '{entry.Name}': {ex.GetType().Name}: {ex.Message}");
            return [];
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    /// <summary>按格式族挑解析器。所有解析器都是「给路径、返回 <see cref="FileFormatInfo"/> 或 null」。</summary>
    private static FileFormatInfo? Parse(MetadataCardKind kind, string filePath, string ext) => kind switch
    {
        MetadataCardKind.Audio => ext switch
        {
            ".flac" => FlacParser.Parse(filePath),
            ".wav" => RiffParser.Parse(filePath),
            ".mp3" => Id3v2Parser.Parse(filePath),
            _ => null,
        },
        MetadataCardKind.Video => VideoParser.Parse(filePath),
        MetadataCardKind.Font => FontParser.Parse(filePath),
        MetadataCardKind.Executable => PeParser.Parse(filePath),
        _ => null,
    };

    /// <summary>
    /// 把条目抽到临时文件（独立于预览的临时文件，避免与 <see cref="PreviewService"/> 抢同一个路径）。
    /// </summary>
    private static async Task<string?> ExtractToTempAsync(
        string archivePath,
        ArchiveItemModel entry,
        ArchiveFormat format,
        string? password,
        CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "MantisZip", "MetaCard");
        var tempFile = Path.Combine(
            tempDir,
            string.Concat("meta_", Hash64(entry.FullPath).ToString("x16", CultureInfo.InvariantCulture), Path.GetExtension(entry.Name)));

        Directory.CreateDirectory(tempDir);

        string? previous;
        lock (TempGate)
        {
            previous = _lastTempFile;
            _lastTempFile = tempFile;
        }

        // 换了一个条目就把上一个临时文件删掉：这里只保留「最新一张卡」的解析素材
        if (previous != null && !string.Equals(previous, tempFile, StringComparison.OrdinalIgnoreCase))
            TryDelete(previous);

        await ArchiveEntryExtractor.ExtractEntryAsync(archivePath, entry.FullPath, tempFile, format, password, ct);
        return File.Exists(tempFile) ? tempFile : null;
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // 解析器可能还持有句柄（Windows 上延迟释放），留给下次 ExtractToTempAsync 覆盖时再试
        }
        catch (UnauthorizedAccessException)
        {
            // 同上：临时文件删不掉不影响功能
        }
    }

    /// <summary>FNV-1a 64 位：把条目全路径压成稳定的文件名，避免非法字符与超长路径。</summary>
    private static ulong Hash64(string value)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offsetBasis;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }

        return hash;
    }
}
