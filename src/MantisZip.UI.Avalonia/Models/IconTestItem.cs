using CommunityToolkit.Mvvm.ComponentModel;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 图标测试条目，记录程序中每个图标的使用位置、当前状态和映射信息。
/// 用于 emoji→PathIcon 替换计划的测试验证。
/// </summary>
public partial class IconTestItem : ObservableObject
{
    /// <summary>图标分类（菜单/工具栏/对话框/代码动态/C# 动态）</summary>
    public string Category { get; set; } = "";

    /// <summary>语义名称（如"打开/浏览"、"搜索"）</summary>
    public string SemanticName { get; set; } = "";

    /// <summary>当前 emoji 字符（如 "📂"），已替换为 PathIcon 的显示为 null</summary>
    public string? EmojiChar { get; set; }

    /// <summary>PathIcon 资源键（如 "IconFolder"），尚为 emoji 的显示为 null</summary>
    public string? ResourceKey { get; set; }

    /// <summary>替换状态：已替换 / 待替换 / 已定义未使用</summary>
    public IconStatus Status { get; set; }

    /// <summary>使用位置（文件路径:行号）</summary>
    public string Location { get; set; } = "";

    /// <summary>使用位置的简短文件路径（仅文件名:行号）</summary>
    public string ShortLocation
    {
        get
        {
            var parts = Location.Split('\\', '/');
            var file = parts.Length > 0 ? parts[^1] : Location;
            if (file.Contains(':'))
            {
                var fileParts = file.Split(':');
                return $"{Path.GetFileName(fileParts[0])}:{fileParts[1]}";
            }
            return Path.GetFileName(file);
        }
    }

    /// <summary>备注</summary>
    public string Notes { get; set; } = "";

    /// <summary>图标显示文本（PathIcon 优先，否则显示 emoji）</summary>
    public string DisplayChar => EmojiChar ?? "";

    /// <summary>状态显示文字</summary>
    public string StatusText => Status switch
    {
        IconStatus.Converted => "✅ 已替换",
        IconStatus.Pending => "⏳ 待替换",
        IconStatus.Defined => "📦 已定义",
        IconStatus.Unused => "📭 未使用",
        _ => ""
    };
}

public enum IconStatus
{
    /// <summary>已替换为 PathIcon</summary>
    Converted,
    /// <summary>仍为 emoji，待替换</summary>
    Pending,
    /// <summary>已在 AppIcons.axaml 中定义</summary>
    Defined,
    /// <summary>已在 AppIcons.axaml 中定义但未使用</summary>
    Unused
}
