using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;

namespace MantisZip.Core.Services;

/// <summary>
/// 压缩请求参数，由调用方构造后传入 <see cref="CompressService.CompressAsync"/>。
/// 调用方负责：密码匹配（PasswordManager）、ProgressWindow 生命周期、结果处理。
/// </summary>
public class CompressRequest
{
    /// <summary>源文件/目录路径列表</summary>
    public List<string> SourcePaths { get; init; } = new();

    /// <summary>输出模式：Manual / Separate / Combined</summary>
    public CompressOutputMode Mode { get; init; }

    /// <summary>压缩格式："zip" / "7z" / "tar.gz"</summary>
    public string Format { get; init; } = "zip";

    /// <summary>压缩级别 1–9，默认 5</summary>
    public int CompressionLevel { get; init; } = 5;

    /// <summary>加密密码（调用方已完成密码库匹配），null 表示不加密</summary>
    public string? Password { get; init; }

    /// <summary>分卷大小（字节），0 表示不分卷</summary>
    public long SplitSize { get; init; }

    /// <summary>原始注释文本（Service 内部按 <see cref="CommentDistribution"/> 策略分配）</summary>
    public string? Comment { get; init; }

    /// <summary>多压缩包时注释的分配方式</summary>
    public CommentDistribution CommentDistribution { get; init; }

    /// <summary>是否加密压缩包</summary>
    public bool Encrypt { get; init; }

    /// <summary>Manual/Combined 模式：由调用方预计算的输出路径</summary>
    public string? OutputPath { get; init; }

    /// <summary>Separate 模式：是否保留源文件扩展名（如 "file.txt" → "file.txt.zip"）</summary>
    public bool KeepOriginalExtension { get; init; }

    /// <summary>压缩单文件夹时是否保留外层目录根，仅 SevenZipEngine 有效</summary>
    public bool PreserveDirectoryRoot { get; init; } = true;
}

/// <summary>
/// 压缩结果统计
/// </summary>
public class CompressResult
{
    /// <summary>成功压缩的数量</summary>
    public int Succeeded { get; init; }

    /// <summary>失败的数量</summary>
    public int Failed { get; init; }

    /// <summary>跳过（Cancel 或无效源文件）的数量</summary>
    public int Skipped { get; init; }
}

/// <summary>
/// 统一的压缩服务，处理所有压缩入口的公共逻辑。
/// GUI (CompressSettingsWindow) 和 CLI (App.Cli) 均通过此服务执行压缩。
/// 调用方负责创建/关闭 ProgressWindow、密码匹配、冲突弹窗实现。
/// </summary>
public static class CompressService
{
    /// <summary>
    /// 预计算压缩输出路径列表。用于调用方在启动前显示批处理文件列表。
    /// Separate 模式：每个源文件对应一个输出路径。
    /// Manual/Combined 模式：单个输出路径。
    /// </summary>
    public static List<string> GetOutputPaths(CompressRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Mode switch
        {
            CompressOutputMode.Separate => request.SourcePaths
                .Select(p => ComputeSeparateOutputPath(request, p))
                .ToList(),
            CompressOutputMode.Manual or CompressOutputMode.Combined =>
                !string.IsNullOrEmpty(request.OutputPath)
                    ? new List<string> { request.OutputPath }
                    : request.SourcePaths.ToList(), // fallback
            _ => throw new ArgumentOutOfRangeException(nameof(request.Mode))
        };
    }

    /// <summary>
    /// 执行压缩操作。根据 <paramref name="request"/> 中的 <see cref="CompressOutputMode"/>
    /// 自动选择循环（Separate）或单次（Manual/Combined）执行路径。
    /// </summary>
    /// <param name="request">压缩参数</param>
    /// <param name="conflictResolver">文件冲突回调，null 表示直接覆盖</param>
    /// <param name="progress">整体进度报告</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>压缩结果统计</returns>
    public static async Task<CompressResult> CompressAsync(
        CompressRequest request,
        CompressConflictResolver? conflictResolver,
        IProgress<ArchiveProgress> progress,
        CancellationToken ct,
        Action<int, BatchItemStatus>? onItemStatus = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);

        CoreLog.Info($"CompressService.CompressAsync: mode={request.Mode}, format={request.Format}, sources={request.SourcePaths.Count}");

        return request.Mode switch
        {
            CompressOutputMode.Separate => await CompressSeparateAsync(request, conflictResolver, progress, ct, onItemStatus),
            CompressOutputMode.Manual => await CompressSingleAsync(request, conflictResolver, progress, ct),
            CompressOutputMode.Combined => await CompressSingleAsync(request, conflictResolver, progress, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Mode), request.Mode, null)
        };
    }

    /// <summary>
    /// Separate 模式：遍历 SourcePaths，每个源文件独立计算输出路径并压缩。
    /// </summary>
    private static async Task<CompressResult> CompressSeparateAsync(
        CompressRequest request,
        CompressConflictResolver? conflictResolver,
        IProgress<ArchiveProgress> progress,
        CancellationToken ct,
        Action<int, BatchItemStatus>? onItemStatus = null)
    {
        int succeeded = 0, failed = 0, skipped = 0;
        int total = request.SourcePaths.Count;

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            var sourcePath = request.SourcePaths[i];
            CoreLog.Info($"CompressService: processing item {i + 1}/{total}: {sourcePath}");

            // 1. 验证源文件/目录存在
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                CoreLog.Info($"CompressService: source not found, skipping: {sourcePath}");
                skipped++;
                ReportOverallProgress(progress, i + 1, total, sourcePath);
                onItemStatus?.Invoke(i, BatchItemStatus.Skipped);
                continue;
            }

            // 2. 计算输出路径
            var outputPath = ComputeSeparateOutputPath(request, sourcePath);

            // 3. 获取引擎
            var engine = ArchiveEngineFactory.GetEngineByExtension(outputPath, new ZipEngine());

            // 4. 冲突检测与解决
            var resolution = ResolveConflict(outputPath, engine, conflictResolver);
            CoreLog.Info($"CompressService: conflict resolution for {outputPath}: {resolution.Action}");

            if (resolution.Action == CompressConflictAction.Cancel)
            {
                CoreLog.Info($"CompressService: cancelled by user, skipping: {outputPath}");
                skipped++;
                ReportOverallProgress(progress, i + 1, total, sourcePath);
                onItemStatus?.Invoke(i, BatchItemStatus.Skipped);
                continue;
            }

            if (resolution.Action == CompressConflictAction.Rename)
            {
                outputPath = ComputeRenamedPath(outputPath, resolution.CustomName);
                engine = ArchiveEngineFactory.GetEngineByExtension(outputPath, new ZipEngine());
                CoreLog.Info($"CompressService: renamed to: {outputPath}");
            }

            // 5. 注释分配
            var comment = GetCommentForIndex(request, i);

            // 6. 构造 ArchiveOptions
            bool isAdd = resolution.Action == CompressConflictAction.Add;
            var options = BuildOptions(request, comment, isAdd);

            // 7. 执行
            try
            {
                if (isAdd)
                {
                    CoreLog.Info($"CompressService: adding to existing archive: {outputPath}");
                    await engine.AddToArchiveAsync(outputPath, new[] { sourcePath }, options, progress, ct);
                }
                else
                {
                    CoreLog.Info($"CompressService: compressing to: {outputPath}");
                    await engine.CompressAsync(new[] { sourcePath }, outputPath, options, progress, ct);
                }

                succeeded++;
                ReportOverallProgress(progress, i + 1, total, sourcePath);
                onItemStatus?.Invoke(i, BatchItemStatus.Completed);
                CoreLog.Info($"CompressService: item {i + 1}/{total} succeeded");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CoreLog.Error($"CompressService: item {i + 1}/{total} failed", ex);
                failed++;
                ReportOverallProgress(progress, i + 1, total, sourcePath);
                onItemStatus?.Invoke(i, BatchItemStatus.Failed);
            }
        }

        CoreLog.Info($"CompressService.CompressSeparateAsync: succeeded={succeeded}, failed={failed}, skipped={skipped}");
        return new CompressResult { Succeeded = succeeded, Failed = failed, Skipped = skipped };
    }

    /// <summary>
    /// Manual/Combined 模式：单次压缩，所有 SourcePaths 作为整体传入引擎。
    /// </summary>
    private static async Task<CompressResult> CompressSingleAsync(
        CompressRequest request,
        CompressConflictResolver? conflictResolver,
        IProgress<ArchiveProgress> progress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var outputPath = request.OutputPath
            ?? throw new InvalidOperationException("OutputPath is required for Manual/Combined mode.");

        CoreLog.Info($"CompressService: single mode, output: {outputPath}");

        var engine = ArchiveEngineFactory.GetEngineByExtension(outputPath, new ZipEngine());

        // 冲突检测
        var resolution = ResolveConflict(outputPath, engine, conflictResolver);
        CoreLog.Info($"CompressService: conflict resolution for {outputPath}: {resolution.Action}");

        if (resolution.Action == CompressConflictAction.Cancel)
        {
            CoreLog.Info("CompressService: cancelled by user");
            return new CompressResult { Succeeded = 0, Failed = 0, Skipped = 1 };
        }

        var resolvedOutputPath = outputPath;
        if (resolution.Action == CompressConflictAction.Rename)
        {
            resolvedOutputPath = ComputeRenamedPath(outputPath, resolution.CustomName);
            engine = ArchiveEngineFactory.GetEngineByExtension(resolvedOutputPath, new ZipEngine());
            CoreLog.Info($"CompressService: renamed to: {resolvedOutputPath}");
        }

        var comment = GetCommentForIndex(request, 0);
        bool isAdd = resolution.Action == CompressConflictAction.Add;
        var options = BuildOptions(request, comment, isAdd);

        var sourceArray = request.SourcePaths.ToArray();

        try
        {
            if (isAdd)
            {
                CoreLog.Info($"CompressService: adding to existing archive: {resolvedOutputPath}");
                await engine.AddToArchiveAsync(resolvedOutputPath, sourceArray, options, progress, ct);
            }
            else
            {
                CoreLog.Info($"CompressService: compressing to: {resolvedOutputPath}");
                await engine.CompressAsync(sourceArray, resolvedOutputPath, options, progress, ct);
            }

            ReportOverallProgress(progress, 1, 1, Path.GetFileName(resolvedOutputPath));

            CoreLog.Info("CompressService: single mode succeeded");
            return new CompressResult { Succeeded = 1, Failed = 0, Skipped = 0 };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CoreLog.Error("CompressService: single mode failed", ex);
            return new CompressResult { Succeeded = 0, Failed = 1, Skipped = 0 };
        }
    }

    /// <summary>
    /// Separate 模式：为单个源文件计算输出路径。
    /// </summary>
    private static string ComputeSeparateOutputPath(CompressRequest request, string sourcePath)
    {
        // 父目录
        string parent;
        if (Directory.Exists(sourcePath))
        {
            // 对目录要去掉末尾分隔符再取父目录
            parent = Path.GetDirectoryName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                     ?? ".";
        }
        else
        {
            parent = Path.GetDirectoryName(sourcePath) ?? ".";
        }

        // 基础文件名
        var baseName = request.KeepOriginalExtension
            ? Path.GetFileName(sourcePath)
            : Path.GetFileNameWithoutExtension(sourcePath);

        // 扩展名
        var ext = string.Equals(request.Format, "tar.gz", StringComparison.OrdinalIgnoreCase)
            ? ".tar.gz"
            : "." + request.Format;

        return Path.Combine(parent, baseName + ext);
    }

    /// <summary>
    /// 根据冲突处理结果计算新的输出路径。
    /// <paramref name="customName"/> 不为 null/空时作为文件名使用，否则自动生成唯一路径。
    /// </summary>
    private static string ComputeRenamedPath(string outputPath, string? customName)
    {
        if (!string.IsNullOrEmpty(customName))
        {
            var dir = Path.GetDirectoryName(outputPath) ?? ".";
            return Path.Combine(dir, customName);
        }

        return PathHelper.GetUniquePath(outputPath);
    }

    /// <summary>
    /// 检测文件冲突并调用回调。
    /// 文件不存在 → 直接返回 Overwrite（无需处理）；
    /// 文件存在且 resolver 为 null → 返回 Overwrite（静默覆盖）；
    /// 文件存在且 resolver 非 null → 构造 CompressConflictInfo 并调用回调。
    /// </summary>
    private static CompressConflictResolution ResolveConflict(
        string outputPath,
        IArchiveEngine engine,
        CompressConflictResolver? conflictResolver)
    {
        if (!File.Exists(outputPath))
            return new CompressConflictResolution(CompressConflictAction.Overwrite, null);

        if (conflictResolver == null)
            return new CompressConflictResolution(CompressConflictAction.Overwrite, null);

        var format = ArchiveEngineFactory.GetFormatByExtension(outputPath);
        var canAdd = engine.CanAdd(format);

        // SuggestedName 为不含路径的唯一文件名建议，用于对话框预填
        var suggestedName = Path.GetFileName(PathHelper.GetUniquePath(outputPath));

        var info = new CompressConflictInfo(outputPath, canAdd, suggestedName);
        return conflictResolver(info);
    }

    /// <summary>
    /// 从 CompressRequest 构造 ArchiveOptions。
    /// Add 分支忽略 SplitSize。
    /// CommentDistribution 已由 Service 解析为具体注释文本，options 中设为 AllSame。
    /// </summary>
    private static ArchiveOptions BuildOptions(CompressRequest request, string? resolvedComment, bool isAdd)
    {
        return new ArchiveOptions
        {
            CompressionLevel = request.CompressionLevel,
            Encrypt = request.Encrypt,
            Password = request.Password,
            SplitSize = isAdd ? 0 : request.SplitSize,
            Comment = resolvedComment,
            CommentDistribution = CommentDistribution.AllSame, // 已由 Service 解析完成
            PreserveDirectoryRoot = request.PreserveDirectoryRoot,
        };
    }

    /// <summary>
    /// 根据注释分配策略获取当前索引对应的注释文本。
    /// </summary>
    private static string? GetCommentForIndex(CompressRequest request, int index)
    {
        return request.CommentDistribution switch
        {
            CommentDistribution.AllSame => request.Comment,
            CommentDistribution.FirstOnly => index == 0 ? request.Comment : null,
            CommentDistribution.PerLine when request.Comment != null => GetLineByIndex(request.Comment, index),
            _ => request.Comment
        };
    }

    /// <summary>
    /// 按换行符分割文本，取第 index 行（跳过空行）。
    /// </summary>
    private static string? GetLineByIndex(string text, int index)
    {
        var lines = text.Split('\n')
                        .Select(l => l.Trim())
                        .Where(l => l.Length > 0)
                        .ToArray();
        return index < lines.Length ? lines[index] : null;
    }

    /// <summary>
    /// 报告整体进度。
    /// </summary>
    private static void ReportOverallProgress(IProgress<ArchiveProgress> progress, int current, int total, string? currentFile)
    {
        progress.Report(new ArchiveProgress
        {
            PercentComplete = total > 0 ? (double)current / total * 100 : 100,
            FilePercentComplete = null,
            CurrentFile = currentFile ?? string.Empty,
            ProcessedFiles = current,
            TotalFiles = total,
        });
    }
}
