using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 封装 Core 的 ArchiveEngineFactory，提供压缩包浏览服务。
/// </summary>
public class ArchiveService
{
    /// <summary>
    /// 判断异常是否为密码相关（加密包未提供密码 / 密码错误）。
    /// 浏览加载与解压设置窗口的逐包校验共用此启发式，避免分类逻辑分叉
    /// ——加密文件名的 7z 包无密码时 ListEntriesAsync 抛密码类异常，不能误报为「损坏」。
    /// </summary>
    /// <param name="ex">打开/列出条目时抛出的异常。</param>
    /// <param name="archivePath">
    /// 压缩包路径（可选）。传入时对含密码关键词但无损坏信号的**歧义异常**做头部结构探测兜底
    /// （见 <see cref="ArchiveHeaderProbe"/>）——SharpSevenZip 对「7z/RAR 加密头」与「7z/RAR 损坏」
    /// 抛同一条消息，仅靠文本无法区分，结构探测可把明确损坏者判为非密码相关。
    /// </param>
    /// <remarks>
    /// 判定顺序：
    /// 1. 消息含明确损坏信号 → 非密码相关（损坏优先）；
    /// 2. 消息不含密码关键词 → 非密码相关；
    /// 3. 否则视为歧义：传入路径时做结构探测，结构明确损坏 → 非密码相关，否则 → 密码相关。
    /// </remarks>
    public static bool IsPasswordRelatedError(Exception ex, string? archivePath = null)
    {
        var message = ex.Message;

        // 1. 损坏/无效信号优先排除——即使消息同时含 password/encrypted 字样
        if (HasCorruptionSignal(message))
            return false;

        // 2. 不含密码关键词 → 与密码无关
        if (!message.Contains("password", StringComparison.OrdinalIgnoreCase)
            && !message.Contains("encrypted", StringComparison.OrdinalIgnoreCase)
            && !message.Contains("密码", StringComparison.OrdinalIgnoreCase))
            return false;

        // 3. 歧义异常（含密码关键词但无损坏信号）：结构探测兜底（方案 B）
        if (!string.IsNullOrEmpty(archivePath)
            && ArchiveHeaderProbe.Probe(archivePath) == HeaderStructure.Broken)
            return false;

        return true;
    }

    /// <summary>
    /// 检测消息是否含明确的损坏/无效信号（7-Zip / SharpCompress 常见错误措辞）。
    /// </summary>
    private static bool HasCorruptionSignal(string message)
    {
        return message.Contains("data error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("crc", StringComparison.OrdinalIgnoreCase)
            || message.Contains("corrupt", StringComparison.OrdinalIgnoreCase)
            || message.Contains("central directory", StringComparison.OrdinalIgnoreCase)
            || message.Contains("too short", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not archive", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cannot open the file as archive", StringComparison.OrdinalIgnoreCase)
            || message.Contains("header error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("文件头损坏", StringComparison.OrdinalIgnoreCase);
    }

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
                return ArchiveLoadResult.Failure(LocalizationManager.T("Status_UnsupportedFormat", Path.GetExtension(archivePath)));
            }

            var items = await engine.ListEntriesAsync(archivePath, password, cancellationToken);
            var itemsList = items.ToList();

            // 注意：条目列出成功即视为加载成功——即使含加密条目（ZIP/RAR/EncryptHeaders=false 的 7z
            // 在无密码时也能列出文件名）。是否提示密码、取消后是否仍打开仅浏览，
            // 由 MainWindowViewModel 的密码解析流程决定（对齐 WPF ResolvePasswordAsync 语义）。
            // IsPasswordRequired 仅在 ListEntriesAsync 本身抛出密码类异常时出现（如 EncryptHeaders=true 的 7z）。

            var models = itemsList.Select(ArchiveItemModel.FromCore).ToList();

            // Load file type icons (folder vs file)
            foreach (var model in models)
            {
                if (model.IsDirectory)
                {
                    model.IconSource = IconService.GetFolderIcon();
                }
                else
                {
                    var ext = Path.GetExtension(model.Name);
                    model.IconSource = IconService.GetFileIcon(ext);
                }
            }

            return ArchiveLoadResult.Success(models, itemsList);
        }
        catch (OperationCanceledException)
        {
            return ArchiveLoadResult.Cancelled();
        }
        catch (Exception ex)
        {
            // 传入 archivePath 以启用头部结构探测：歧义消息（7z/RAR 加密头 vs 损坏同消息）
            // 由结构探测兜底，明确损坏者不弹密码框。
            if (IsPasswordRelatedError(ex, archivePath))
            {
                return ArchiveLoadResult.PasswordRequired();
            }

            // 打开失败：本地化整包错误，不再裸露引擎英文原文。
            // 头部结构明确损坏（7z/RAR）→ 直接判「文件损坏」；否则走 WholeArchiveErrorLocalizer 映射。
            if (ArchiveHeaderProbe.Probe(archivePath) == HeaderStructure.Broken)
            {
                return ArchiveLoadResult.Failure(LocalizationManager.T("Error_Whole_Corrupted"));
            }

            var (message, _) = WholeArchiveErrorLocalizer.ToUserText(ex);
            return ArchiveLoadResult.Failure(message);
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
        ErrorMessage = LocalizationManager.T("Error_ArchiveEncrypted")
    };

    public static ArchiveLoadResult Cancelled() => new()
    {
        IsSuccess = false,
        IsCancelled = true
    };
}
