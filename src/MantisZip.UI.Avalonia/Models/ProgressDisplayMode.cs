namespace MantisZip.UI.Avalonia.Models;

/// <summary>进度窗口上方内容模式（v6 原型三模式）。</summary>
public enum ProgressContentMode { Simple, Detailed, List }

/// <summary>进度窗口下方信息量分级（v6 原型三档：控制「显示多少信息」，非间距）。</summary>
public enum ProgressInfoDensity { Minimal, Medium, Full }

/// <summary>
/// 进度窗口显示偏好（内容模式 / 信息量分级）的持久化解析：
/// AppSettings 字符串 ↔ 枚举 的双向映射。独立成静态类以便单测（不依赖窗口/文件系统）。
/// 默认值：内容模式 <see cref="ProgressContentMode.Detailed"/>、信息量 <see cref="ProgressInfoDensity.Medium"/>。
/// </summary>
public static class ProgressDisplayModeParser
{
    /// <summary>内容模式 → 持久化字符串。</summary>
    public static string ToSetting(ProgressContentMode mode) => mode switch
    {
        ProgressContentMode.Detailed => "detailed",
        ProgressContentMode.List => "list",
        _ => "simple",
    };

    /// <summary>持久化字符串 → 内容模式（未知/空值回退默认 Detailed）。</summary>
    public static ProgressContentMode ParseContentMode(string? value) => value switch
    {
        "simple" => ProgressContentMode.Simple,
        "list" => ProgressContentMode.List,
        _ => ProgressContentMode.Detailed,
    };

    /// <summary>信息量分级 → 持久化字符串。</summary>
    public static string ToSetting(ProgressInfoDensity density) => density switch
    {
        ProgressInfoDensity.Minimal => "minimal",
        ProgressInfoDensity.Full => "full",
        _ => "medium",
    };

    /// <summary>持久化字符串 → 信息量分级（未知/空值回退默认 Medium）。</summary>
    public static ProgressInfoDensity ParseDensity(string? value) => value switch
    {
        "minimal" => ProgressInfoDensity.Minimal,
        "full" => ProgressInfoDensity.Full,
        _ => ProgressInfoDensity.Medium,
    };
}
