namespace MantisZip.Core.Models;

/// <summary>
/// 自适应压缩级别枚举。
/// </summary>
public enum AdaptiveLevel
{
    /// <summary>0 — 不压缩。</summary>
    Store,
    /// <summary>级别 3 — 快速压缩。</summary>
    Fast,
    /// <summary>级别 5 — 标准压缩。</summary>
    Normal,
    /// <summary>级别 9 — 最大压缩。</summary>
    Max,
    /// <summary>跟随全局级别。</summary>
    Global,
    /// <summary>全局级别 +1（不超过 9）。</summary>
    GlobalPlusOne,
    /// <summary>全局级别 -1（不低于 0）。</summary>
    GlobalMinusOne,
    /// <summary>用户自定义 1-9 级别。</summary>
    Custom,
}

/// <summary>
/// 自适应压缩级别模式。
/// </summary>
public enum AdaptiveCompressionMode
{
    /// <summary>禁用（始终使用选定级别）。</summary>
    Disabled,
    /// <summary>仅对已知格式自动降级（扩展名查表）— 默认推荐。</summary>
    StoreForCompressed,
    /// <summary>智能检测（大文件魔数 + 采样试压）。</summary>
    SmartDetect,
}

/// <summary>
/// 自定义格式级别覆盖规则。
/// 引用 FormatCatalog 中的格式 ID，不直接写扩展名。
/// </summary>
public class AdaptiveOverrideRule
{
    /// <summary>规则名称，如 "图片类"、"视频类"。</summary>
    public string Name { get; set; } = "";

    /// <summary>引用的格式 ID 列表，如 ["Jpeg", "Png", "WebP"]。</summary>
    public List<string> FormatIds { get; set; } = new();

    /// <summary>对此类格式应用的压缩级别。</summary>
    public AdaptiveLevel Level { get; set; } = AdaptiveLevel.Store;

    /// <summary>是否启用此规则。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>仅当 Level = Custom 时使用的自定义级别值（1-9）。</summary>
    public int? CustomLevel { get; set; }
}
