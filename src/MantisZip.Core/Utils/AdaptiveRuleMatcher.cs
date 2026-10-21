using MantisZip.Core.Models;
using MantisZip.Core.Services;

namespace MantisZip.Core.Utils;

/// <summary>
/// 自适应压缩规则匹配器。
/// 优先级：用户自定义规则 → 内置分类（ClassifyByExtension）。
/// </summary>
public static class AdaptiveRuleMatcher
{
    /// <summary>
    /// 为单个文件解析最终压缩级别。
    /// </summary>
    /// <param name="filePath">文件路径（或文件名）。</param>
    /// <param name="globalLevel">用户在压缩对话框选定的全局压缩级别（0-9）。</param>
    /// <param name="adaptiveCompression">自适应压缩开关。</param>
    /// <param name="adaptiveSmartDetect">魔数检测开关。</param>
    /// <param name="rules">用户自定义覆盖规则列表，可为 null。</param>
    /// <param name="customFormats">用户自定义格式列表（供规则按 FormatId 查扩展名），可为 null。</param>
    /// <param name="customStoreFormatIds">用户自定义「仅存储」格式 ID 列表，可为 null。</param>
    /// <returns>该文件应使用的压缩级别（0-9）。</returns>
    public static int ResolveLevel(
        string filePath,
        int globalLevel,
        bool adaptiveCompression,
        bool adaptiveSmartDetect = false,
        List<AdaptiveOverrideRule>? rules = null,
        List<FormatDefinition>? customFormats = null,
        HashSet<string>? customStoreFormatIds = null)
    {
        CoreLog.Trace("AdaptiveRuleMatcher.ResolveLevel: file={0}, globalLevel={1}, adaptive={2}, smartDetect={3}, rules={4}",
            filePath, globalLevel, adaptiveCompression, adaptiveSmartDetect, rules?.Count ?? 0);

        if (!adaptiveCompression)
        {
            CoreLog.Trace("AdaptiveRuleMatcher.ResolveLevel: disabled → return globalLevel={0}", globalLevel);
            return globalLevel;
        }

        var ext = Path.GetExtension(filePath);

        // 1. 检查用户自定义规则（优先级最高）
        if (rules != null)
        {
            foreach (var rule in rules)
            {
                if (!rule.Enabled) continue;
                foreach (var formatId in rule.FormatIds)
                {
                    var def = FormatCatalog.GetById(formatId, customFormats);
                    if (def == null) continue;
                    if (def.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    {
                        var resolved = ResolveAdaptiveLevel(rule.Level, rule.CustomLevel, globalLevel);
                        CoreLog.Trace("AdaptiveRuleMatcher.ResolveLevel: rule '{0}' matched (format={1}, ext={2}, level={3}) → {4}",
                            rule.Name, formatId, ext, rule.Level, resolved);
                        return resolved;
                    }
                }
            }
        }

        // 2. 用户自定义「仅存储格式」列表
        if (ZipEntryClassifier.IsCustomStoreFormat(ext, customStoreFormatIds, customFormats))
        {
            CoreLog.Trace("AdaptiveRuleMatcher.ResolveLevel: custom store format (ext={0}) → Store", ext);
            return 0;
        }

        // 3. SSOT：扩展名判定（内容已是压缩格式）
        if (ZipEntryClassifier.IsCompressed(ext))
        {
            CoreLog.Trace("AdaptiveRuleMatcher.ResolveLevel: compressed extension (ext={0}) → Store", ext);
            return 0;
        }

        // 4. 魔数检测（扩展名骗人 / 无扩展名时的兜底）
        if (adaptiveSmartDetect && ZipEntryClassifier.IsCompressedByMagic(filePath))
        {
            CoreLog.Trace("AdaptiveRuleMatcher.ResolveLevel: magic detected compressed content → Store");
            return 0;
        }

        CoreLog.Trace("AdaptiveRuleMatcher.ResolveLevel: → globalLevel={0}", globalLevel);
        return globalLevel;
    }

    /// <summary>
    /// 解析 AdaptiveLevel 枚举到实际压缩级别。
    /// </summary>
    /// <param name="level">自适应级别枚举值。</param>
    /// <param name="customLevel">Custom 模式下的自定义级别值。</param>
    /// <param name="globalLevel">全局压缩级别。</param>
    /// <returns>实际压缩级别（0-9）。</returns>
    public static int ResolveAdaptiveLevel(AdaptiveLevel level, int? customLevel, int globalLevel)
    {
        return level switch
        {
            AdaptiveLevel.Store => 0,
            AdaptiveLevel.Fast => 3,
            AdaptiveLevel.Normal => 5,
            AdaptiveLevel.Max => 9,
            AdaptiveLevel.Global => globalLevel,
            AdaptiveLevel.GlobalPlusOne => Math.Min(9, globalLevel + 1),
            AdaptiveLevel.GlobalMinusOne => Math.Max(0, globalLevel - 1),
            AdaptiveLevel.Custom => customLevel ?? globalLevel,
            _ => globalLevel,
        };
    }

    /// <summary>
    /// 计算多数文件类型的推荐级别（用于非 per-entry 场景）。
    /// 统计已压缩类别与可压缩类别的数量，多数决定级别。
    /// </summary>
    /// <param name="filePaths">文件路径列表。</param>
    /// <param name="globalLevel">全局压缩级别。</param>
    /// <returns>推荐级别：0（多数已压缩）或 globalLevel（多数可压缩）。</returns>
    public static int ComputeMajorityLevel(List<string> filePaths, int globalLevel)
    {
        int storeCount = 0;
        int normalCount = 0;

        foreach (var path in filePaths)
        {
            // 与 ResolveLevel 同口径（SSOT 扩展名判定），不再用 CompressionCoefficients 分类
            if (ZipEntryClassifier.IsCompressed(Path.GetExtension(path)))
                storeCount++;
            else
                normalCount++;
        }

        var result = storeCount > normalCount ? 0 : globalLevel;
        CoreLog.Trace("AdaptiveRuleMatcher.ComputeMajorityLevel: files={0}, store={1}, normal={2} → {3}",
            filePaths.Count, storeCount, normalCount, result);
        return result;
    }
}
