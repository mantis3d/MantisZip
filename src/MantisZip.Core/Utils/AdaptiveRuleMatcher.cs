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
    /// <param name="mode">自适应压缩模式。</param>
    /// <param name="rules">用户自定义覆盖规则列表，可为 null。</param>
    /// <param name="customFormats">用户自定义格式列表，可为 null。</param>
    /// <returns>该文件应使用的压缩级别（0-9）。</returns>
    public static int ResolveLevel(
        string filePath,
        int globalLevel,
        AdaptiveCompressionMode mode,
        List<AdaptiveOverrideRule>? rules = null,
        List<FormatDefinition>? customFormats = null)
    {
        CoreLog.Trace("AdaptiveRuleMatcher.ResolveLevel: file={0}, globalLevel={1}, mode={2}, rules={3}",
            filePath, globalLevel, mode, rules?.Count ?? 0);

        if (mode == AdaptiveCompressionMode.Disabled)
        {
            CoreLog.Trace("AdaptiveRuleMatcher.ResolveLevel: disabled → return globalLevel={0}", globalLevel);
            return globalLevel;
        }

        // 1. 检查用户自定义规则（优先级最高）
        if (rules != null)
        {
            var ext = Path.GetExtension(filePath);
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

        // 2. 内置分类
        var category = CompressionCoefficients.ClassifyByExtension(filePath);
        CoreLog.Trace("AdaptiveRuleMatcher.ResolveLevel: category={0} for {1}", category, filePath);

        if (mode == AdaptiveCompressionMode.StoreForCompressed)
        {
            // 仅对已知的已压缩格式降级为 Store
            if (category is "image_lossy" or "media" or "archive")
            {
                CoreLog.Trace("AdaptiveRuleMatcher.ResolveLevel: StoreForCompressed → Store (category={0})", category);
                return 0; // Store
            }
            CoreLog.Trace("AdaptiveRuleMatcher.ResolveLevel: StoreForCompressed → globalLevel={0} (category={1})", globalLevel, category);
            return globalLevel;
        }

        // SmartDetect: 大文件走魔数检测（这里简化为扩展名分类）
        if (category is "image_lossy" or "media" or "archive")
        {
            CoreLog.Trace("AdaptiveRuleMatcher.ResolveLevel: SmartDetect → Store (category={0})", category);
            return 0;
        }
        CoreLog.Trace("AdaptiveRuleMatcher.ResolveLevel: SmartDetect → globalLevel={0} (category={1})", globalLevel, category);
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
            var category = CompressionCoefficients.ClassifyByExtension(path);
            if (category is "image_lossy" or "media" or "archive")
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
