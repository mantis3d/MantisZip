using MantisZip.Core.Engines;
using SharpSevenZip;

namespace MantisZip.Core.Utils;

/// <summary>
/// <see cref="SharpSevenZipExtractor"/> 的统一创建入口。
/// </summary>
/// <remarks>
/// 为什么要统一（issue #36 的补丁）：SharpSevenZip 在不传格式时会「逐个格式试探打开」，
/// 对「大 PE 桩 + 内嵌包」的自解压 exe 会失败并抛
/// <c>Invalid archive: open/read error! ... decided it is TAR by mistake.</c>
/// （2026-10-31 实测：真 PE 桩 329216B 前缀必失败，64KB 桩 / 零字节前缀正常，
/// 说明失败与桩结构相关，与归档本身无关）。探测到 SFX 时把内嵌载荷格式显式传给 7z.dll 即可正常打开。
/// <para>
/// 另外两点是这里必须收口的原因：
/// </para>
/// <list type="number">
/// <item>RAR v4 与 RAR5 是两套格式串（<c>Rar4</c> / <c>Rar</c>），交叉使用报同一个 Invalid archive；</item>
/// <item>构造 extractor 前必须先配 7z.dll 路径（issue #52），漏一处就会按默认路径加载库、
/// 之后再也改不回来 —— 统一收口后调用方不必各自记得。</item>
/// </list>
/// </remarks>
public static class SevenZipExtractorFactory
{
    /// <summary>
    /// 创建 <see cref="SharpSevenZipExtractor"/>：配好 7z.dll 路径，并在文件是自解压包（SFX）时
    /// 显式指定内嵌格式；普通压缩包保持 SharpSevenZip 的自动探测。
    /// </summary>
    /// <param name="archivePath">归档路径（SFX 时就是那个 .exe）。</param>
    /// <param name="password">密码；<c>null</c> / 空串按「无密码」处理（避免往构造函数里传 null）。</param>
    public static SharpSevenZipExtractor Create(string archivePath, string? password = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(archivePath);

        SevenZipEngine.EnsureLibraryPath();

        var hint = TryResolveFormatHint(archivePath);
        bool hasPassword = !string.IsNullOrEmpty(password);

        // 加密分支同样是带 format 的重载：少传就把 SFX 又送回试探路径。
        if (hint is { } format)
        {
            return hasPassword
                ? new SharpSevenZipExtractor(archivePath, password!, format)
                : new SharpSevenZipExtractor(archivePath, format);
        }

        return hasPassword
            ? new SharpSevenZipExtractor(archivePath, password!)
            : new SharpSevenZipExtractor(archivePath);
    }

    /// <summary>
    /// 自解压包对应的 7z.dll 格式提示；非 SFX（或探测失败）返回 <c>null</c>。
    /// </summary>
    public static InArchiveFormat? TryResolveFormatHint(string archivePath)
    {
        if (string.IsNullOrEmpty(archivePath))
            return null;

        try
        {
            if (!SfxProbe.TryDetectPayload(archivePath, out var kind))
                return null;

            return kind switch
            {
                SfxPayloadKind.Zip => InArchiveFormat.Zip,
                SfxPayloadKind.SevenZip => InArchiveFormat.SevenZip,
                SfxPayloadKind.Rar4 => InArchiveFormat.Rar4,
                SfxPayloadKind.Rar5 => InArchiveFormat.Rar,
                _ => null,
            };
        }
        catch (Exception ex)
        {
            // 探测失败不影响正常打开：退回 SharpSevenZip 的自动探测（普通包照旧可用）。
            CoreLog.Trace("SevenZipExtractorFactory: SFX 探测失败 {0}: {1}", archivePath, ex.Message);
            return null;
        }
    }
}
