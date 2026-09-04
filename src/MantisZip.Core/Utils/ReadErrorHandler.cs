using System.IO;
using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 7z 原生压缩路径（SharpSevenZip.ComressDirectory / CompressFilesEncrypted）是单次原生调用，
/// 无法像 SharpCompress ZipWriter / TarWriter 那样逐文件恢复读取错误。
/// 因此本工具在调用前对将要传给 7z.dll 的文件集做一次统一的读权限预检：
/// 文件不可读（被占用/权限不足）时按 重试/跳过/中止 处理 —— 跳过则从文件集中剔除，
/// 中止或无 ErrorResolver 则抛异常使整个压缩失败。行为对齐 ZipEngine/TarGzEngine 的
/// 带重试读文件逻辑，保证不可读文件不会导致整个压缩直接中止。
/// </summary>
public static class ReadErrorHandler
{
    /// <summary>
    /// 逐文件预检可读性，返回可安全压缩的文件子集。
    /// 不可读且用户选择跳过（Skip）的文件被剔除；重试（Retry）重新探测；
    /// 中止（Abort）或无 ErrorResolver 时抛原异常。返回的文件均通过读打开校验。
    /// </summary>
    /// <param name="filePaths">待预检的完整文件路径集合。</param>
    /// <param name="options">归档选项（携带 ErrorResolver），可为 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static List<string> FilterUnreadableFiles(
        IEnumerable<string> filePaths,
        ArchiveOptions? options,
        CancellationToken cancellationToken)
    {
        var result = new List<string>();
        foreach (var path in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 目录条目无法以读方式打开文件（OpenRead 抛 UnauthorizedAccessException），
            // 且目录本身也不需要读权限校验 —— 只对真正的文件做可读性预检，目录原样保留。
            if (File.Exists(path) && !EnsureReadable(path, options))
                continue;
            result.Add(path);
        }
        return result;
    }

    /// <summary>
    /// 校验单个文件是否可读（共享读模式打开）。不可读时按 <see cref="ArchiveOptions.ErrorResolver"/>
    /// 决定 重试/跳过/中止；无 ErrorResolver 时自动重试 3 次后抛异常。
    /// </summary>
    private static bool EnsureReadable(string fullPath, ArchiveOptions? options)
    {
        int retries = 3;
        while (true)
        {
            try
            {
                // 共享读：与 ReadFileWithRetry 同款语义，源文件可被编辑器以写权限持有时不冲突
                using var fs = SharedReadStream.OpenRead(fullPath);
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                retries--;
                if (options?.ErrorResolver == null)
                {
                    if (retries <= 0) throw;
                    continue;
                }

                var action = options.ErrorResolver(new FileErrorInfo
                {
                    FilePath = fullPath,
                    ErrorMessage = ex.Message,
                    RetriesRemaining = retries
                });

                if (action == FileErrorAction.Retry) continue;
                if (action == FileErrorAction.Skip) return false;
                throw; // Abort 或其它 → 中止整个压缩
            }
        }
    }
}
