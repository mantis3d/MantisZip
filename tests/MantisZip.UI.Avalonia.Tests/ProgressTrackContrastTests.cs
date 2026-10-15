// ============================================================================
// 进度条轨道对比度守卫测试 —— 主题色值级回归绊线
//
// 目的：缺陷修复后（进度 0% 时进度条轨道色 ThemeProgressBgBrush 与所在面板
// 背景色 ThemeSplitterBg 完全相同/仅差 4，导致进度条不可见），锁定两主题的
// 轨道色必须与面板色保持足够对比（任一 RGB 通道差 ≥ 16）。
//
// 方法：正则解析 ThemeLight.axaml / ThemeDark.axaml 中两个资源的十六进制颜色，
// 计算 RGB 三通道最大差值。解析不到资源定义时响亮失败（不会静默通过）。
// ============================================================================
using System.Text.RegularExpressions;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 进度轨道（ThemeProgressBgBrush）与面板背景（ThemeSplitterBg）对比度守卫。
/// </summary>
public class ProgressTrackContrastTests
{
    /// <summary>单通道最小色差（低于此值 0% 进度时轨道难以辨认）。</summary>
    private const int MinChannelDelta = 16;

    /// <summary>从测试程序集输出目录向上遍历，找到包含 src 子目录的目录作为仓库根。</summary>
    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException(
            $"无法定位仓库根（祖先目录链中未找到 'src' 目录）。Base directory: {AppContext.BaseDirectory}");
    }

    /// <summary>读取主题 XAML 全文。</summary>
    private static string ReadTheme(string fileName)
    {
        var path = Path.Combine(GetRepoRoot(), "src", "MantisZip.UI.Avalonia", "Themes", fileName);
        Assert.True(File.Exists(path), $"主题文件不存在：{path}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// 用正则从主题 XAML 提取指定资源的十六进制颜色（#RRGGBB 或 #AARRGGBB）。
    /// 匹配不到时以可读消息失败（资源被改名/换形态时守卫需同步更新）。
    /// </summary>
    private static string ExtractHexColor(string themeXaml, string resourceKey, string pattern)
    {
        var match = Regex.Match(themeXaml, pattern, RegexOptions.CultureInvariant);
        Assert.True(match.Success,
            $"未找到资源 {resourceKey} 的颜色定义（正则未匹配）。" +
            "资源形态可能已重构，守卫测试需同步更新。");
        return match.Groups[1].Value;
    }

    /// <summary>解析 #RRGGBB / #AARRGGBB 为 (R, G, B)（8 位时跳过 alpha 通道）。</summary>
    private static (int R, int G, int B) ParseHexColor(string hex)
    {
        var h = hex.TrimStart('#');
        Assert.True(h.Length == 6 || h.Length == 8,
            $"颜色值格式应为 6 或 8 位十六进制，实际：{hex}");
        if (h.Length == 8) h = h[2..]; // 跳过 AA
        return (
            Convert.ToInt32(h[0..2], 16),
            Convert.ToInt32(h[2..4], 16),
            Convert.ToInt32(h[4..6], 16));
    }

    [Theory]
    [InlineData("ThemeLight.axaml")]
    [InlineData("ThemeDark.axaml")]
    public void ProgressTrackColor_ContrastsWithPanelBackground(string themeFile)
    {
        var xaml = ReadTheme(themeFile);

        // 轨道：SolidColorBrush 内联 Color 属性
        var trackHex = ExtractHexColor(xaml, "ThemeProgressBgBrush",
            @"<SolidColorBrush\s+x:Key=""ThemeProgressBgBrush""\s+Color=""(#[0-9A-Fa-f]{6,8})""");

        // 面板：Color 资源定义（同 key 的 SolidColorBrush 引用 StaticResource，不匹配本正则）
        var panelHex = ExtractHexColor(xaml, "ThemeSplitterBg",
            @"<Color\s+x:Key=""ThemeSplitterBg"">(#[0-9A-Fa-f]{6,8})</Color>");

        var track = ParseHexColor(trackHex);
        var panel = ParseHexColor(panelHex);

        var delta = Math.Max(
            Math.Abs(track.R - panel.R),
            Math.Max(Math.Abs(track.G - panel.G), Math.Abs(track.B - panel.B)));

        Assert.True(delta >= MinChannelDelta,
            $"{themeFile}: 进度轨道色 {trackHex} 与面板色 {panelHex} 单通道最大差 {delta} " +
            $"< {MinChannelDelta}——0% 进度时进度条与面板同色不可见（缺陷回归）。");
    }
}
