using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 日志隐私脱敏模式
/// </summary>
public enum LogPrivacyMode
{
    /// <summary>不脱敏</summary>
    Off,
    /// <summary>仅保留文件名/目录名，去掉路径前缀</summary>
    FilenameOnly,
    /// <summary>替换为 [PATH_1] 等顺序标记</summary>
    Full
}

/// <summary>
/// 日志路径脱敏工具。
/// 在日志写入前用正则匹配 Windows 路径并做脱敏替换。
/// </summary>
public static class LogRedactor
{
    // 编译一次，避免每次 log 调用重复编译
    // 分支1: 驱动器路径 (支持空格，末尾以空白/引号/行尾自然截断)
    // 分支2: UNC 路径  \\server\share\path
    private static readonly Regex _pathRegex = new(
        @"[A-Za-z]:(?:\\[^\\""<>|]+)+\\?|\\\\[^\\""<>|]+(?:\\[^\\""<>|]+)+\\?",
        RegexOptions.Compiled);

    private static readonly ConcurrentDictionary<string, int> _pathIds = new();
    private static readonly object _pathLock = new();
    private const int MaxCachedPaths = 10000;

    /// <summary>
    /// 对日志消息中的 Windows 路径做脱敏处理。
    /// </summary>
    /// <param name="msg">原始日志消息</param>
    /// <param name="mode">脱敏模式</param>
    /// <returns>脱敏后的消息</returns>
    public static string RedactPaths(string msg, LogPrivacyMode mode)
    {
        if (mode == LogPrivacyMode.Off || string.IsNullOrEmpty(msg))
            return msg;

        return _pathRegex.Replace(msg, match =>
        {
            var path = match.Value.TrimEnd('\\');
            if (string.IsNullOrEmpty(path))
                return match.Value;

            switch (mode)
            {
                case LogPrivacyMode.FilenameOnly:
                    var name = Path.GetFileName(path);
                    return name.Length > 0 ? name : "[DIR]";

                case LogPrivacyMode.Full:
                    // 双检锁模式：先无锁检查，不存在时加锁再确认
                    if (!_pathIds.TryGetValue(path, out var id))
                    {
                        lock (_pathLock)
                        {
                            if (!_pathIds.TryGetValue(path, out id))
                            {
                                // 上限保护：超限时清空重建（在锁内安全执行）
                                if (_pathIds.Count >= MaxCachedPaths)
                                {
                                    _pathIds.Clear();
                                }
                                id = _pathIds.Count + 1;
                                _pathIds.TryAdd(path, id);
                            }
                        }
                    }
                    return $"[PATH_{id}]";

                default:
                    return match.Value;
            }
        });
    }

    /// <summary>
    /// 解析字符串模式为枚举。配置值 "off"/"filename"/"full" 映射到对应枚举。
    /// 其他值返回 Off。
    /// </summary>
    public static LogPrivacyMode ParseMode(string mode) => mode switch
    {
        "filename" => LogPrivacyMode.FilenameOnly,
        "full" => LogPrivacyMode.Full,
        _ => LogPrivacyMode.Off
    };

    /// <summary>
    /// 清空路径 ID 映射（测试或重置时调用）
    /// </summary>
    public static void Reset() => _pathIds.Clear();
}
