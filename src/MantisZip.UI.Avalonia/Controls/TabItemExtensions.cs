using Avalonia;
using Avalonia.Controls;

namespace MantisZip.UI.Avalonia.Controls;

/// <summary>
/// TabItem 附加属性：为 TabItem.compactTab 类样式提供标题图标（Geometry 资源键）。
/// 设置 TabIcon 后，标题渲染为「图标 + 文字」；未设置（null）时回退为纯文字标题。
/// </summary>
public static class TabItemExtensions
{
    /// <summary>附加属性：tab 标题图标资源键（AppIcons.axaml 中的 Geometry Key，如 "IconCompress"）。</summary>
    public static readonly AttachedProperty<string?> TabIconProperty =
        AvaloniaProperty.RegisterAttached<TabItem, string?>("TabIcon", typeof(TabItemExtensions));

    /// <summary>设置 tab 标题图标资源键。</summary>
    public static void SetTabIcon(TabItem element, string? value) => element.SetValue(TabIconProperty, value);

    /// <summary>获取 tab 标题图标资源键。</summary>
    public static string? GetTabIcon(TabItem element) => element.GetValue(TabIconProperty);
}