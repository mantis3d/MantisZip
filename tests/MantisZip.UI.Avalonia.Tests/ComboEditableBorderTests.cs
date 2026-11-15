using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Controls;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 可编辑 ComboBox 外框完整性的回归绊线（2026-11-15 实机缺陷）。
///
/// 症状：<see cref="TemplatePresetPicker"/> 里可编辑下拉的外框左半截消失，鼠标划上去又正常。
/// 根因：App.axaml 的「非热态 TextBox」样式把 TextBox 模板里的 Border#PART_BorderElement
/// 涂成不透明的 ThemeSurfaceBgBrush；Fluent 的 ComboBox 可编辑模式内部恰好嵌着一个占满
/// 左侧的 TextBox（PART_EditableTextBox），它的白底正好盖住 ComboBox 自己画的 1px 边框。
/// hover 时那条样式的 :not(:pointerover) 不再命中 → 白底让位 → 边框重现。
///
/// 修法：在该样式的选择器上加 :not(#PART_EditableTextBox)，排除可编辑下拉的内嵌 TextBox。
/// 这层错了编译器不管、别的测试也覆盖不到：无头宿主默认不加载 App.axaml 的全局样式，
/// 普通控件测试渲染出来一律「正常」，缺陷被完全掩盖（所以这里必须手动并入 App 样式）。
///
/// ⚠ App 样式搬出来后挂在**本用例自己的 Window** 上，不塞进 Application.Current.Styles：
/// 同一进程里其它用例（AboutWindowUpdateTabTests、PathBreadcrumbSuggestionClickTests 等）会往全局
/// Styles 里塞 FluentTheme / 局部 Style，共用一个全局样式表会让结果随执行顺序漂移；
/// Window 级 Style 只作用于该窗口的树，天然隔离，也不会干扰别人。
/// </summary>
public sealed class ComboEditableBorderTests
{
    private const string AppAxamlRelPath = "src/MantisZip.UI.Avalonia/App.axaml";

    /// <summary>
    /// 源码级绊线：非热态 TextBox 边框样式的选择器必须排除 PART_EditableTextBox。
    /// 比渲染断言更直白 —— 有人「顺手清理」选择器里的 :not(#...) 时，立刻能看到为什么不能删。
    /// </summary>
    [Fact]
    public void AppAxaml_NonHotTextBoxBorderStyle_ExcludesEditableComboTextBox()
    {
        var text = File.ReadAllText(SourcePath(AppAxamlRelPath));

        const string prefix = "Selector=\"";
        var styleStart = text.IndexOf(
            "Selector=\"TextBox:not(:pointerover):not(:focus-within):not(:disabled)",
            StringComparison.Ordinal);
        Assert.True(styleStart >= 0, "App.axaml 里找不到「非热态 TextBox」边框样式，选择器被改名了吗？");

        var selectorEnd = text.IndexOf('"', styleStart + prefix.Length);
        Assert.True(selectorEnd > styleStart, "选择器没有闭合引号。");
        var selector = text[(styleStart + prefix.Length)..selectorEnd];

        Assert.Contains("PART_BorderElement", selector, StringComparison.Ordinal);
        Assert.Contains(":not(#PART_EditableTextBox)", selector, StringComparison.Ordinal);
    }

    /// <summary>
    /// 运行期绊线：并入真实 App.axaml 样式后物化可编辑下拉，内层 TextBox 的 Border
    /// 不得再是不透明底色，且外框上边左半段 / 左边上半段要能扫到边框像素
    ///（缺陷复现时这两段整段纯白，实测缺 98%/100%）。
    /// </summary>
    [AvaloniaFact]
    public void EditableComboBox_InnerTextBox_DoesNotCoverItsFrame()
    {
        var host = Application.Current!;
        var addedApplicationStyles = new List<IStyle>();
        var addedDictionaries = new List<IResourceProvider>();

        try
        {
            // 1) Fluent 主题 + 主题/图标资源字典（样式里的 DynamicResource 全指向这里）。
            //    只补宿主缺的那部分，缺了就插到最前，避免被别的用例塞进来的样式压住。
            if (!host.Styles.OfType<FluentTheme>().Any())
            {
                var fluent = new FluentTheme();
                host.Styles.Insert(0, fluent);
                addedApplicationStyles.Add(fluent);
            }

            foreach (var source in new[]
                     {
                         "avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml",
                         "avares://MantisZip.UI.Avalonia/Resources/Icons/AppIcons.axaml",
                     })
            {
                var include = new ResourceInclude(new Uri("avares://MantisZip.UI.Avalonia/"))
                {
                    Source = new Uri(source),
                };
                host.Resources.MergedDictionaries.Add(include);
                addedDictionaries.Add(include);
            }

            // 2) 真实 App.axaml 的全局样式（无头宿主不跑 App.Initialize，必须手动搬过来）。
            //    搬出来的 Style 先摘掉 owner，再挂到本用例自己的 Window 上。
            var realApp = new MantisZip.UI.Avalonia.App();
            realApp.Initialize();
            var appStyles = new List<IStyle>();
            foreach (var style in realApp.Styles.ToList())
            {
                realApp.Styles.Remove(style);
                appStyles.Add(style);
            }

            // 3) 物化控件（96 DPI = 用户报缺陷时的 100% 缩放）
            var picker = new TemplatePresetPicker
            {
                Width = 180,
                Presets = [new MantisZip.Core.Utils.FileNameTemplatePreset("日期", "{name}_{date}")],
                PresetName = "日期",
                WatermarkKey = "Compress_FileNameTemplate_PresetName",
            };
            var window = new Window
            {
                Width = 240,
                Height = 70,
                Content = new Border { Padding = new Thickness(10), Child = picker },
            };
            foreach (var style in appStyles)
            {
                window.Styles.Add(style);
            }

            window.Show();
            Pump(window);

            var combo = picker.GetVisualDescendants().OfType<ComboBox>().Single();
            var innerTextBox = combo.GetVisualDescendants().OfType<TextBox>().Single();
            var innerBorder = innerTextBox.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Name == "PART_BorderElement");
            var frameBorder = combo.GetVisualDescendants().OfType<Border>()
                .First(b => b.Name == "Background");

            // 直接成因：内层 Border 不能是不透明底色
            var innerBackground = innerBorder.Background;
            Assert.False(
                innerBackground is ISolidColorBrush { Color.A: > 0 } && innerBackground.Opacity > 0,
                $"可编辑下拉的内嵌 TextBox 底色不能是不透明画刷（实际 {innerBackground}）。" +
                "App.axaml 的非热态 TextBox 样式必须排除 PART_EditableTextBox。");

            // ComboBox 自己的外框必须还在（否则内层无底也「看不到框」）
            Assert.True(frameBorder.BorderThickness.Left > 0, "ComboBox 外框 BorderThickness 为 0。");
            Assert.NotNull(frameBorder.BorderBrush);

            // 4) 像素核对：上边左半段 / 左边上半段必须扫得到边框
            var origin = combo.TranslatePoint(new Point(0, 0), window)!.Value;
            var width = (int)Math.Ceiling(window.Bounds.Width) + 2;
            var height = (int)Math.Ceiling(window.Bounds.Height) + 2;
            var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
            bitmap.Render(window);
            var stride = width * 4;
            var buffer = new byte[stride * height];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(
                buffer,
                System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), buffer.Length, stride);
            }
            finally
            {
                handle.Free();
            }

            var left = (int)Math.Round(origin.X);
            var top = (int)Math.Round(origin.Y);
            var right = left + (int)Math.Round(combo.Bounds.Width);
            var bottom = top + (int)Math.Round(combo.Bounds.Height);
            var halfWidth = (right - left) / 2;
            var halfHeight = (bottom - top) / 2;

            var missingTop = 0;
            for (var x = left + 2; x < left + halfWidth; x++)
            {
                if (!HasInkNearColumn(x, top)) missingTop++;
            }

            var missingLeft = 0;
            for (var y = top + 2; y < top + halfHeight; y++)
            {
                if (!HasInkNearRow(left, y)) missingLeft++;
            }

            // 缺陷复现时缺 ~98%/100%；修好后只剩圆角处零星无像素（实测 0~3 个）
            Assert.True(
                missingTop <= halfWidth / 4,
                $"外框上边左半段缺 {missingTop}/{halfWidth} 像素，内嵌 TextBox 又把边框盖住了。");
            Assert.True(
                missingLeft <= halfHeight / 4,
                $"外框左边上半段缺 {missingLeft}/{halfHeight} 像素，内嵌 TextBox 又把边框盖住了。");

            window.Close();

            bool HasInkNearColumn(int x, int y)
            {
                for (var dy = -3; dy <= 3; dy++)
                {
                    var yy = y + dy;
                    if (yy < 0 || yy >= height) continue;
                    if (Luminance(x, yy) < 245) return true;
                }
                return false;
            }

            bool HasInkNearRow(int x, int y)
            {
                for (var dx = -3; dx <= 3; dx++)
                {
                    var xx = x + dx;
                    if (xx < 0 || xx >= width) continue;
                    if (Luminance(xx, y) < 245) return true;
                }
                return false;
            }

            int Luminance(int x, int y)
            {
                var index = y * stride + x * 4;
                return (buffer[index] + buffer[index + 1] + buffer[index + 2]) / 3;
            }
        }
        finally
        {
            foreach (var dictionary in addedDictionaries)
            {
                host.Resources.MergedDictionaries.Remove(dictionary);
            }

            foreach (var style in addedApplicationStyles)
            {
                host.Styles.Remove(style);
            }
        }
    }

    private static string SourcePath(string relativePath)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine(
            dir ?? throw new InvalidOperationException("无法定位仓库根。"),
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void Pump(Window window)
    {
        for (var i = 0; i < 12; i++)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}