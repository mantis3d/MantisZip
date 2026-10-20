using System;

namespace MantisZip.UI.Avalonia.Utils;

/// <summary>
/// 自动关闭倒计时行内编辑的输入解析（spec §10.4）：
/// 有效整数 → 夹紧到 0–60；非法输入 → 回退当前剩余值（fallback 同样夹紧）。
/// 实例级临时修改，不回写 AppSettings（D16）。
/// </summary>
public static class CountdownParser
{
    /// <summary>解析行内输入的秒数：合法整数夹紧 0–60；非法/空回退 fallbackSeconds（同样夹紧）。</summary>
    public static int Parse(string? text, int fallbackSeconds)
    {
        if (!int.TryParse(text?.Trim(), out var value))
            return Math.Clamp(fallbackSeconds, 0, 60);
        return Math.Clamp(value, 0, 60);
    }
}
