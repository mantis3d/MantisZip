using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 封装 Core 的 ArchiveEngineFactory，提供压缩包浏览服务。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class ArchiveService
{
    /// <summary>
    /// 打开压缩包并列出所有条目。
    /// </summary>
    public async Task<ArchiveLoadResult> LoadArchiveAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            if (engine == null)
            {
                return ArchiveLoadResult.Failure($"不支持的文件格式: {Path.GetExtension(archivePath)}");
            }

            var items = await engine.ListEntriesAsync(archivePath, password, cancellationToken);
            var itemsList = items.ToList();
            var models = itemsList.Select(ArchiveItemModel.FromCore).ToList();

            // Load file type icons
            foreach (var model in models)
            {
                var ext = Path.GetExtension(model.Name);
                model.IconSource = IconService.GetFileIcon(ext);
            }

            return ArchiveLoadResult.Success(models, itemsList);
        }
        catch (OperationCanceledException)
        {
            return ArchiveLoadResult.Cancelled();
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (message.Contains("password", StringComparison.OrdinalIgnoreCase)
                || message.Contains("encrypted", StringComparison.OrdinalIgnoreCase)
                || message.Contains("密码", StringComparison.OrdinalIgnoreCase))
            {
                return ArchiveLoadResult.PasswordRequired();
            }

            return ArchiveLoadResult.Failure($"无法打开压缩包: {ex.Message}");
        }
    }
}

/// <summary>
/// 打开压缩包的结果。
/// </summary>
public class ArchiveLoadResult
{
    public bool IsSuccess { get; private init; }
    public bool IsPasswordRequired { get; private init; }
    public bool IsCancelled { get; private init; }
    public string? ErrorMessage { get; private init; }
    public IReadOnlyList<ArchiveItemModel>? Entries { get; private init; }

    /// <summary>
    /// 原始 ArchiveItem 列表（用于文件夹树构建等需要原始数据的场景）。
    /// </summary>
    public IReadOnlyList<ArchiveItem>? RawItems { get; private init; }

    public static ArchiveLoadResult Success(List<ArchiveItemModel> entries, IReadOnlyList<ArchiveItem>? rawItems = null) => new()
    {
        IsSuccess = true,
        Entries = entries,
        RawItems = rawItems
    };

    public static ArchiveLoadResult Failure(string message) => new()
    {
        IsSuccess = false,
        ErrorMessage = message
    };

    public static ArchiveLoadResult PasswordRequired() => new()
    {
        IsSuccess = false,
        IsPasswordRequired = true,
        ErrorMessage = "此压缩包已加密，请输入密码"
    };

    public static ArchiveLoadResult Cancelled() => new()
    {
        IsSuccess = false,
        IsCancelled = true
    };
}
