using System;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using MantisZip.UI.Avalonia.Controls;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>真实指针命中测试：点在段按钮上的像素位置，事件必须落到按钮而不是空白承接层。</summary>
public class PathBreadcrumbHitTestTests
{
    private static (Window Window, PathBreadcrumb Crumb) Show(string path)
    {
        var crumb = new PathBreadcrumb { PathKind = BreadcrumbPathKind.Disk, Path = path };
        var window = new Window { Width = 800, Height = 60, Content = crumb };
        window.Show();
        window.UpdateLayout();
        return (window, crumb);
    }

    [AvaloniaFact]
    public void ClickingDriveSegmentPixels_RaisesNavigateRequested()
    {
        var (window, crumb) = Show(@"E:\AI\IndexTTS2-SonicVale");
        string? navigated = null;
        crumb.NavigateRequested += (_, p) => navigated = p;

        var drive = crumb.FindControl<StackPanel>("SegmentsPanel")!.Children.OfType<Button>().First();
        var center = drive.TranslatePoint(new Point(drive.Bounds.Width / 2, drive.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        window.UpdateLayout();

        Assert.Equal(@"E:\", navigated);
        Assert.False(crumb.IsEditing);
        window.Close();
    }

    [AvaloniaFact]
    public void ClickingCurrentSegmentPixels_EntersEditMode()
    {
        var (window, crumb) = Show(@"E:\AI\IndexTTS2-SonicVale");
        string? navigated = null;
        crumb.NavigateRequested += (_, p) => navigated = p;

        var current = crumb.FindControl<StackPanel>("SegmentsPanel")!.Children.OfType<Button>().Last();
        var center = current.TranslatePoint(new Point(current.Bounds.Width / 2, current.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        window.UpdateLayout();

        Assert.True(crumb.IsEditing);
        Assert.Null(navigated);
        window.Close();
    }
}