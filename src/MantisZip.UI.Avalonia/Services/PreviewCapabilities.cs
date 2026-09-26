using System;
using System.Collections.Generic;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 预览能力标记（[Flags]）。新增预览类型时在 <see cref="PreviewCapabilities"/> 注册能力即可，
/// 工具栏按钮/控件区域据此显隐，无需再改 ViewModel 属性。
/// </summary>
[Flags]
public enum PreviewCapability
{
    None = 0,
    /// <summary>缩放控制（放大/缩小/适应视口）。</summary>
    Zoom = 1 << 0,
    /// <summary>透明背景棋盘格切换（🏁）。</summary>
    Transparency = 1 << 1,
    /// <summary>压平 Alpha（🎨，仅静态图语义；动画帧不做全帧压平）。</summary>
    FlattenAlpha = 1 << 2,
    /// <summary>动画播放控制（播放/暂停/上帧/下帧/帧输入）。</summary>
    AnimationControls = 1 << 3,
}

/// <summary>
/// 预览类型 → 能力注册表（对齐 MetadataRegistry 的静态注册模式）。
/// 能力影响工具栏按钮显隐（HasZoomControls/HasTransparencyControls/HasFlattenAlphaControls/HasAnimationControls）。
/// </summary>
public static class PreviewCapabilities
{
    private static readonly Dictionary<PreviewType, PreviewCapability> _capabilities = new();

    static PreviewCapabilities()
    {
        Register(PreviewType.Image,
            PreviewCapability.Zoom | PreviewCapability.Transparency | PreviewCapability.FlattenAlpha);
        Register(PreviewType.AnimatedImage,
            PreviewCapability.Zoom | PreviewCapability.Transparency | PreviewCapability.AnimationControls);
        Register(PreviewType.Svg,
            PreviewCapability.Transparency | PreviewCapability.FlattenAlpha);
        Register(PreviewType.IcoGallery,
            PreviewCapability.Transparency);
    }

    private static void Register(PreviewType type, PreviewCapability capabilities)
    {
        _capabilities[type] = capabilities;
    }

    /// <summary>查询预览类型的能力集合；未注册类型返回 <see cref="PreviewCapability.None"/>。</summary>
    public static PreviewCapability For(PreviewType type)
    {
        return _capabilities.TryGetValue(type, out var caps) ? caps : PreviewCapability.None;
    }
}
