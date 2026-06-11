using MantisZip.Core.Abstractions;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 格式检测辅助方法。
/// </summary>
public static class ArchiveFormatHelper
{
    /// <summary>
    /// 根据扩展名判断是否为支持的压缩包格式。
    /// </summary>
    public static bool IsArchiveFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".zip" or ".7z" or ".rar" or ".tar" or ".tgz" or ".gz" or ".iso";
    }

    /// <summary>
    /// 获取压缩包格式。
    /// </summary>
    public static ArchiveFormat GetFormat(string path) =>
        ArchiveEngineFactory.GetFormatByExtension(path);
}
