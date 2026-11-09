using System;
using System.IO;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Markup.Xaml.Styling;
using global::Avalonia.Threading;
using MantisZip.UI.Avalonia.Controls;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 内嵌文件选择器「地址区两行布局」（2026-11-05）的回归锁。
///
/// 背景：新增「新建文件夹」后地址行有 6 个方形按钮（<c>ControlHeightMd</c> 见方 + 2px 间距），
/// 在内嵌 620px 列里把面包屑挤到 ≈400px、窗口拉到 <c>MinWidth 900</c> 时只剩 ≈300px，
/// 路径被折成 <c>E:\ … \Controls</c>。改为「按钮行 + 面包屑整宽行」后路径宽度翻倍。
///
/// 这两个用例钉的是**布局关系**而不是像素：①面包屑必须占到控件宽度的 80% 以上
/// （旧布局在 620px 下只有 ≈65%）；②导航按钮必须整行都在面包屑**上方**，
/// 且「新建文件夹」与「添加收藏」在行尾（放在按钮行右端是用户明确要求的位置）。
/// </summary>
public class CustomFilePickerAddressLayoutTests
{
    private const double PickerWidth = 620;

    private static (Window Window, CustomFilePickerControl Picker, string Dir) Show()
    {
        EnsureTheme();

        var dir = Path.Combine(Path.GetTempPath(), "mz-addrlayout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var picker = new CustomFilePickerControl();
        var window = new Window
        {
            Width = PickerWidth,
            Height = 560,
            Content = picker,
        };
        window.Show();
        picker.Initialize(initialPath: dir);
        Pump(window);

        return (window, picker, dir);
    }

    private static void EnsureTheme()
    {
        var app = Application.Current!;
        if (app.Styles.OfType<global::Avalonia.Themes.Fluent.FluentTheme>().Any()) return;

        app.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
        app.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://MantisZip.UI.Avalonia/"))
        {
            Source = new Uri("avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml"),
        });
    }

    private static void Pump(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static void Cleanup(Window window, string dir)
    {
        window.Close();
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响断言结果
        }
    }

    [AvaloniaFact]
    public void Breadcrumb_TakesNearlyFullWidth()
    {
        var (window, picker, dir) = Show();

        var crumbs = picker.FindControl<PathBreadcrumb>("PathCrumbs");
        Assert.NotNull(crumbs);

        // 面包屑独占一行：除了控件自身的左右内边距，不应再被任何按钮挤掉
        Assert.True(
            crumbs!.Bounds.Width >= picker.Bounds.Width * 0.8,
            $"面包屑宽度 {crumbs.Bounds.Width:F1} 不足选择器宽度 {picker.Bounds.Width:F1} 的 80%（按钮又挤回来了？）");

        Cleanup(window, dir);
    }

    [AvaloniaFact]
    public void NavButtons_OnRowAboveBreadcrumb_AndActionsAtRowEnd()
    {
        var (window, picker, dir) = Show();

        var crumbs = picker.FindControl<PathBreadcrumb>("PathCrumbs")!;
        var up = picker.FindControl<Button>("UpButton")!;
        var browse = picker.FindControl<Button>("SystemBrowseButton")!;
        var newFolder = picker.FindControl<Button>("NewFolderButton")!;
        var favorite = picker.FindControl<Button>("AddFavoriteButton")!;

        var crumbsTop = crumbs.TranslatePoint(default, picker)!.Value.Y;
        var upTop = up.TranslatePoint(default, picker)!.Value.Y;
        var browseTop = browse.TranslatePoint(default, picker)!.Value.Y;
        var newTop = newFolder.TranslatePoint(default, picker)!.Value.Y;
        var favTop = favorite.TranslatePoint(default, picker)!.Value.Y;

        // ① 整行按钮都在面包屑上方（旧布局里是同一行，这里必红）
        Assert.True(upTop + up.Bounds.Height <= crumbsTop + 0.5, "「向上」按钮不在面包屑上方的按钮行里");
        Assert.True(browseTop + browse.Bounds.Height <= crumbsTop + 0.5, "「系统浏览」按钮不在面包屑上方的按钮行里");

        // ② 新建 / 收藏与导航按钮同一行（用户要求放按钮行右端，不是面包屑行）
        Assert.True(Math.Abs(newTop - browseTop) < 1, "「新建文件夹」不在按钮行里");
        Assert.True(Math.Abs(favTop - browseTop) < 1, "「添加收藏」不在按钮行里");

        // ③ 右端：两个动作按钮都在导航按钮右侧
        var upX = up.TranslatePoint(default, picker)!.Value.X;
        var newX = newFolder.TranslatePoint(default, picker)!.Value.X;
        var favX = favorite.TranslatePoint(default, picker)!.Value.X;
        Assert.True(newX > upX, "「新建文件夹」不在按钮行右端");
        Assert.True(favX > newX, "「添加收藏」应在「新建文件夹」右侧（行末）");

        Cleanup(window, dir);
    }
}
