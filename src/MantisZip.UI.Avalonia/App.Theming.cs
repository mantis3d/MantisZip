using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Views;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace MantisZip.UI.Avalonia;

/// <summary>
/// App 的 主题 / 字体 / 紧凑度应用部分（自 App.axaml.cs 拆分，纯移动）。
/// </summary>
public partial class App : Application
{
    // ════════════════════════════════════════════════════════════════
    //  Theme
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// 应用主题（三态）：System=跟随系统，Light=亮色，Dark=暗色。
    /// 替换主题 ResourceDictionary 并设置 RequestedThemeVariant 供 FluentTheme 使用。
    /// 启动优化（startup-theme-dedup-trace）：同主题跳过 Clear+重解析（App.axaml 已静态合并
    /// ThemeLight + AppIcons(85KB)，启动时重复解析两次为纯浪费）；换肤时先 Load 新字典成功后
    /// 再替换，图标字典复用缓存实例（异常时旧字典完整保留，优于旧实现的 Clear 先于 Load）。
    /// </summary>
    private void ApplyTheme()
    {
        if (PlatformSettings is not IPlatformSettings ps) return;
        try
        {
            var settings = AppSettings.Load();
            bool isDark = settings.Theme switch
            {
                "Light" => false,
                "Dark" => true,
                _ => ps.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark,
            };
            DebugLog($"[Theme] ApplyTheme theme={settings.Theme} dark={isDark}");

            var targetUri = new Uri(isDark ? DarkThemeUri : LightThemeUri);

            // ── 同主题（启动默认亮色 / 重复调用）：跳过全部字典操作，只切 Fluent 变体 ──
            if (_loadedThemeUri == targetUri)
            {
                RequestedThemeVariant = isDark
                    ? global::Avalonia.Styling.ThemeVariant.Dark
                    : global::Avalonia.Styling.ThemeVariant.Light;
                return;
            }

            // ── 换肤：先 Load 新主题（失败则进入 catch 保留旧字典），图标复用缓存不重解析 ──
            var themeProvider = AvaloniaXamlLoader.Load(targetUri) as IResourceProvider;

            _cachedIconProvider ??= Resources.MergedDictionaries.Count >= 2
                ? Resources.MergedDictionaries[1]
                : AvaloniaXamlLoader.Load(new Uri(IconResourcesUri)) as IResourceProvider;

            Resources.MergedDictionaries.Clear();
            if (themeProvider != null) Resources.MergedDictionaries.Add(themeProvider);
            if (_cachedIconProvider != null) Resources.MergedDictionaries.Add(_cachedIconProvider);

            _loadedThemeUri = targetUri;

            // ── Set theme variant for FluentTheme ──
            RequestedThemeVariant = isDark
                ? global::Avalonia.Styling.ThemeVariant.Dark
                : global::Avalonia.Styling.ThemeVariant.Light;
        }
        catch (Exception ex)
        {
            DebugLog($"[Theme] ApplyTheme ERROR: {ex.Message}");
        }
    }

    /// <summary>
    /// 供 SettingsWindow 保存设置后立即刷新主题（无需重启）。
    /// </summary>
    internal static void RefreshTheme()
    {
        if (Current is App app)
            app.ApplyTheme();
    }

    /// <summary>
    /// 从 AppSettings 读取全局界面字体设置，更新 AppGlobalFont 资源及所有已打开窗口。
    /// </summary>
    private void ApplyAppFontFamily()
    {
        try
        {
            var settings = AppSettings.Load();
            var fontName = settings.AppFontFamily;

            if (string.IsNullOrEmpty(fontName))
            {
                // 默认字体：重建资源确保 DynamicResource 重求值，清除窗口本地值让 Style 接管
                Resources.Remove("AppGlobalFont");
                Resources.Add("AppGlobalFont", FontFamily.Default);
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var w in desktop.Windows)
                        w.ClearValue(TextBlock.FontFamilyProperty);
                }
            }
            else
            {
                // 指定字体：更新资源 + 设置窗口本地值，确保立即可见
                var font = new FontFamily(fontName);
                Resources["AppGlobalFont"] = font;
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var w in desktop.Windows)
                        w.FontFamily = font;
                }
            }

            DebugLog($"[Font] Applied global font: {(string.IsNullOrEmpty(fontName) ? "default" : fontName)}");
        }
        catch (Exception ex)
        {
            DebugLog($"[Font] Failed to apply global font: {ex.Message}");
        }
    }

    /// <summary>
    /// 供 SettingsWindow 保存设置后立即刷新全局字体（无需重启）。
    /// </summary>
    internal static void RefreshAppFontFamily()
    {
        if (Current is App app)
            app.ApplyAppFontFamily();
    }

    /// <summary>
    /// 应用紧凑度模式，覆盖 12 个间距/控件高度资源的运行时值。
    /// 使用 DynamicResource 的自动更新机制即时生效（无需重启）。
    /// </summary>
    private void ApplyCompactness(CompactnessMode mode)
    {
        DebugLog($"[Compactness] Applying mode: {mode}");

        // 三档间距数值
        static double GetSpacing(string key, CompactnessMode m) => (key, m) switch
        {
            ("SpacingXxs", CompactnessMode.Compact) => 2,
            ("SpacingXxs", CompactnessMode.Normal) => 4,
            ("SpacingXxs", CompactnessMode.Loose) => 6,

            ("SpacingXs", CompactnessMode.Compact) => 4,
            ("SpacingXs", CompactnessMode.Normal) => 8,
            ("SpacingXs", CompactnessMode.Loose) => 12,

            ("SpacingSm", CompactnessMode.Compact) => 8,
            ("SpacingSm", CompactnessMode.Normal) => 12,
            ("SpacingSm", CompactnessMode.Loose) => 16,

            ("SpacingMd", CompactnessMode.Compact) => 12,
            ("SpacingMd", CompactnessMode.Normal) => 16,
            ("SpacingMd", CompactnessMode.Loose) => 24,

            ("SpacingLg", CompactnessMode.Compact) => 16,
            ("SpacingLg", CompactnessMode.Normal) => 24,
            ("SpacingLg", CompactnessMode.Loose) => 32,

            ("SpacingXl", CompactnessMode.Compact) => 24,
            ("SpacingXl", CompactnessMode.Normal) => 32,
            ("SpacingXl", CompactnessMode.Loose) => 48,

            ("ControlHeightSm", CompactnessMode.Compact) => 22,
            ("ControlHeightSm", CompactnessMode.Normal) => 26,
            ("ControlHeightSm", CompactnessMode.Loose) => 30,

            ("ControlHeightMd", CompactnessMode.Compact) => 28,
            ("ControlHeightMd", CompactnessMode.Normal) => 32,
            ("ControlHeightMd", CompactnessMode.Loose) => 38,

            ("ControlHeightLg", CompactnessMode.Compact) => 42,
            ("ControlHeightLg", CompactnessMode.Normal) => 48,
            ("ControlHeightLg", CompactnessMode.Loose) => 54,

            ("ControlMinHeight", CompactnessMode.Compact) => 32,
            ("ControlMinHeight", CompactnessMode.Normal) => 40,
            ("ControlMinHeight", CompactnessMode.Loose) => 48,

            ("BorderRadius", CompactnessMode.Compact) => 4,
            ("BorderRadius", CompactnessMode.Normal) => 6,
            ("BorderRadius", CompactnessMode.Loose) => 8,

            ("DialogPadding", CompactnessMode.Compact) => 12,
            ("DialogPadding", CompactnessMode.Normal) => 16,
            ("DialogPadding", CompactnessMode.Loose) => 24,

            _ => 0,
        };

        var keys = new[]
        {
            "SpacingXxs", "SpacingXs", "SpacingSm", "SpacingMd", "SpacingLg", "SpacingXl",
            "ControlHeightSm", "ControlHeightMd", "ControlHeightLg", "ControlMinHeight",
        };

        foreach (var key in keys)
            Resources[key] = GetSpacing(key, mode);

        // Typed resources — double primitive cannot be used directly on
        // CornerRadius / Thickness properties via DynamicResource.
        Resources["BorderRadius"] = new CornerRadius(GetSpacing("BorderRadius", mode));
        Resources["DialogPadding"] = new Thickness(GetSpacing("DialogPadding", mode));
        // Thickness variants of spacing keys (for Margin / Padding)
        foreach (var k in new[] { "SpacingXxs", "SpacingXs", "SpacingSm", "SpacingMd", "SpacingLg", "SpacingXl" })
            Resources[k + "Thk"] = new Thickness(GetSpacing(k, mode));

        // TextControlPadding: use smaller horizontal padding (SpacingXxs) and
        // larger vertical padding (SpacingXs) so switching to Loose mode increases
        // vertical breathing room without shrinking the editable width too much.
        // Reduce TextControlPadding to half to keep TextBox padding visually aligned
        // with other controls while preserving increased vertical breathing room.
        Resources["TextControlPadding"] = new Thickness(
            GetSpacing("SpacingXxs", mode) * 0.5, // left/right (half)
            GetSpacing("SpacingXs", mode) * 0.5   // top/bottom (half)
        );
    }

    /// <summary>
    /// 供 SettingsWindow 保存设置后立即刷新紧凑度（无需重启）。
    /// </summary>
    internal static void RefreshCompactness()
    {
        if (Current is App app)
        {
            var settings = AppSettings.Load();
            var mode = settings.CompactnessMode switch
            {
                "Compact" => CompactnessMode.Compact,
                "Loose" => CompactnessMode.Loose,
                _ => CompactnessMode.Normal,
            };
            app.ApplyCompactness(mode);
        }
    }

}
