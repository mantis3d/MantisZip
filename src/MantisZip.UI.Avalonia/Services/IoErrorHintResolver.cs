using System.IO;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// <see cref="IOException"/> 的 Win32 错误码 → 可操作提示（磁盘满 / 文件被占用 / 路径过长）。
/// 仅作 tooltip 附加提示；不扩展 <c>EntryFailureReason</c> 枚举（spec D5）。
/// </summary>
public static class IoErrorHintResolver
{
    private const int ErrorHandleDiskFull = 39;      // ERROR_HANDLE_DISK_FULL
    private const int ErrorDiskFull = 112;           // ERROR_DISK_FULL
    private const int ErrorSharingViolation = 32;    // ERROR_SHARING_VIOLATION
    private const int ErrorLockViolation = 33;       // ERROR_LOCK_VIOLATION
    private const int ErrorFilenameExcedRange = 206; // ERROR_FILENAME_EXCED_RANGE

    /// <summary>从 HResult 低 16 位（Win32 错误码）解析可操作提示；未命中返回 null。</summary>
    public static string? FromHResult(int hresult) => (hresult & 0xFFFF) switch
    {
        ErrorDiskFull or ErrorHandleDiskFull => LocalizationManager.T("Error_Hint_DiskFull"),
        ErrorSharingViolation or ErrorLockViolation => LocalizationManager.T("Error_Hint_FileInUse"),
        ErrorFilenameExcedRange => LocalizationManager.T("Error_Hint_PathTooLong"),
        _ => null,
    };

    /// <summary>异常为 <see cref="IOException"/> 时按 HResult 解析提示；否则返回 null。</summary>
    public static string? TryGetHint(Exception? ex) =>
        ex is IOException ? FromHResult(ex.HResult) : null;
}
