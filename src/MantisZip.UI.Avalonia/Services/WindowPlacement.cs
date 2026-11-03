using Avalonia.Controls;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 窗口尺寸 / 分栏宽度的持久化小工具（2026-10-30）。
/// </summary>
/// <remarks>
/// 与 <c>AppSettings.ProgressWindowWidth</c> 同一套路：**打开时恢复、关闭时写回**，不做位置记忆
/// （位置在多显示器插拔后容易落到屏幕外，收益低风险高）。
/// 三处统一在这里做，是因为「非法值回落」的判据必须一致：
/// 用户手改 settings.json 写成 NaN / ∞ / 0 / 负数时，窗口既不能变成 0×0 也不能抛异常，
/// 一律回落到 XAML 里声明的默认尺寸。
/// </remarks>
internal static class WindowPlacement
{
    /// <summary>
    /// 把持久化的宽高应用到窗口：非法值跳过（保持 XAML 默认），合法值不小于窗口自身的 MinWidth/MinHeight。
    /// </summary>
    public static void Apply(Window window, double savedWidth, double savedHeight)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (IsUsable(savedWidth)) window.Width = Math.Max(savedWidth, window.MinWidth);
        if (IsUsable(savedHeight)) window.Height = Math.Max(savedHeight, window.MinHeight);
    }

    /// <summary>把持久化的分栏宽度应用到 <paramref name="grid"/> 的第一列（像素列）。</summary>
    public static void ApplyLeftColumn(Control grid, double savedWidth, double fallback)
    {
        if (grid is not Grid root) return;
        if (root.ColumnDefinitions.Count == 0) return;

        var width = IsUsable(savedWidth) ? savedWidth : fallback;
        if (!IsUsable(width)) return;
        root.ColumnDefinitions[0].Width = new GridLength(width, GridUnitType.Pixel);
    }

    /// <summary>取分栏第一列的像素宽度（非像素列或非法值回落 <paramref name="fallback"/>）。</summary>
    public static double CaptureLeftColumn(Control grid, double fallback)
    {
        if (grid is not Grid root) return fallback;
        if (root.ColumnDefinitions.Count == 0) return fallback;

        var length = root.ColumnDefinitions[0].Width;
        return length.IsAbsolute && IsUsable(length.Value) ? length.Value : fallback;
    }

    /// <summary>关闭时写回用：当前值合法就用它，否则用 <paramref name="fallback"/>（= 默认尺寸）。</summary>
    public static double Capture(double current, double fallback) => IsUsable(current) ? current : fallback;

    /// <summary>NaN / ∞ / ≤0 都视为「没存过」。</summary>
    public static bool IsUsable(double value) => double.IsFinite(value) && value > 0;
}
