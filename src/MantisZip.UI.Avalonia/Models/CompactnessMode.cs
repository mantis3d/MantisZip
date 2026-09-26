namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 紧凑度模式枚举，用于控制全局间距/控件高度/边距。
/// </summary>
public enum CompactnessMode
{
    /// <summary>紧凑 — 最小间距，适合信息密度高场景</summary>
    Compact = 0,

    /// <summary>正常 — Avalonia Fluent 默认尺寸</summary>
    Normal = 1,

    /// <summary>松散 — 大间距，适合触摸屏或偏好宽敞布局</summary>
    Loose = 2,
}
