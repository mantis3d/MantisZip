namespace MantisZip.Core.Models;

/// <summary>
/// 格式定义 — 描述一种文件格式的扩展名和魔数。
/// 内置格式从 FileFormat 枚举自动生成，不可修改删除。
/// 用户自定义格式可设置扩展名 + 魔数 hex。
/// </summary>
public class FormatDefinition
{
    /// <summary>格式 ID，如 "Jpeg"、"Png"。</summary>
    public string Id { get; set; } = "";

    /// <summary>显示名称，如 "JPEG 图片"、"PNG 图片"。</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>支持的扩展名列表（含点、小写），如 [".jpg", ".jpeg"]。</summary>
    public List<string> Extensions { get; set; } = new();

    /// <summary>魔数 hex 字符串（如 "FFD8FFE0"），仅自定义格式需要。</summary>
    public string? MagicHex { get; set; }

    /// <summary>是否为内置格式。内置格式不可修改或删除。</summary>
    public bool IsBuiltIn { get; set; } = true;
}
