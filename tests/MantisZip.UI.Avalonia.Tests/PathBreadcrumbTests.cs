using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Controls;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 面包屑地址栏（<see cref="PathBreadcrumb"/>）的无头测试：分段解析、折叠、空路径态、点击上抛、编辑态 Enter/Esc。
/// 说明：控件不自带主题资源依赖，测试里直接放进无头窗口 Show + 布局即可拿到视觉树。
/// </summary>
public class PathBreadcrumbTests
{
    private static (Window Window, PathBreadcrumb Crumb) ShowCrumb(
        string path,
        BreadcrumbPathKind kind = BreadcrumbPathKind.Disk,
        int maxSegments = 6)
    {
        var crumb = new PathBreadcrumb
        {
            PathKind = kind,
            Path = path,
            MaxSegments = maxSegments,
            EditPlaceholderKey = "Breadcrumb_EditPlaceholder",
            ArchiveRootTooltipKey = "Breadcrumb_ArchiveRoot",
        };
        var window = new Window { Width = 800, Height = 200, Content = crumb };
        window.Show();
        window.UpdateLayout();
        return (window, crumb);
    }

    /// <summary>把段区里的每个子元素转成可断言的文本（根段显示为 &lt;root&gt;，省略号为 …）。</summary>
    private static List<string> SegmentTexts(PathBreadcrumb crumb)
    {
        var host = crumb.FindControl<StackPanel>("SegmentsPanel")!;
        var list = new List<string>();
        foreach (var child in host.Children)
        {
            switch (child)
            {
                case Button b:
                    list.Add(b.Content?.ToString() ?? string.Empty);
                    break;
                case TextBlock tb when tb.Classes.Contains("crumbSep"):
                    break; // 分隔符不计入段
                case TextBlock tb:
                    list.Add(tb.Text ?? string.Empty);
                    break;
                case StackPanel:
                    list.Add("<root>");
                    break;
            }
        }
        return list;
    }

    [AvaloniaFact]
    public void VirtualPath_BuildsRootPlusSegments_WithLastSegmentAsCurrent()
    {
        var (window, crumb) = ShowCrumb("docs/images/2026", BreadcrumbPathKind.Virtual);

        Assert.Equal(new[] { "<root>", "docs", "images", "2026" }, SegmentTexts(crumb));

        // 末段是当前目录：TextBlock（不可点）而不是 Button
        var host = crumb.FindControl<StackPanel>("SegmentsPanel")!;
        var last = host.Children.OfType<TextBlock>().Last(tb => !tb.Classes.Contains("crumbSep"));
        Assert.Equal("2026", last.Text);
        Assert.Contains("crumbCurrent", last.Classes);

        window.Close();
    }

    [AvaloniaFact]
    public void DiskPath_BuildsDriveRootPlusSegments_DriveRootIsNotClickable()
    {
        var (window, crumb) = ShowCrumb(@"C:\Users\Admin");

        Assert.Equal(new[] { "C:", "Users", "Admin" }, SegmentTexts(crumb));

        // 盘符根段不可点击 → 渲染为 TextBlock
        var host = crumb.FindControl<StackPanel>("SegmentsPanel")!;
        var first = host.Children[0];
        Assert.IsType<TextBlock>(first);

        window.Close();
    }

    [AvaloniaFact]
    public void EmptyDiskPath_ShowsPlaceholderAndHidesSegments()
    {
        var (window, crumb) = ShowCrumb(string.Empty);

        var hint = crumb.FindControl<TextBlock>("EmptyHint")!;
        Assert.True(hint.IsVisible);
        Assert.False(string.IsNullOrWhiteSpace(hint.Text));

        window.Close();
    }

    [AvaloniaFact]
    public void ClickingIntermediateSegment_RaisesNavigateRequested_WithThatPath()
    {
        var (window, crumb) = ShowCrumb(@"C:\Users\Admin\Downloads");
        string? navigated = null;
        crumb.NavigateRequested += (_, p) => navigated = p;

        var host = crumb.FindControl<StackPanel>("SegmentsPanel")!;
        var usersButton = host.Children.OfType<Button>().First(b => (string?)b.Content == "Users");
        usersButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(@"C:\Users", navigated);

        window.Close();
    }

    [AvaloniaFact]
    public void TooManySegments_FoldsMiddleIntoSingleEllipsis()
    {
        var (window, crumb) = ShowCrumb(@"C:\a\b\c\d\e\f\g\h", maxSegments: 6);

        var texts = SegmentTexts(crumb);
        Assert.Equal(6, texts.Count);
        Assert.Equal("C:", texts[0]);
        Assert.Equal("…", texts[1]);
        // 末尾保留 4 段（MaxSegments - 2）
        Assert.Equal(new[] { "f", "g", "h" }, texts.Skip(3));

        window.Close();
    }

    [AvaloniaFact]
    public void EditMode_EnterRaisesNavigateRequested_AndEscDoesNot()
    {
        var (window, crumb) = ShowCrumb(@"C:\Users");
        var edit = crumb.FindControl<AutoCompleteBox>("EditBox")!;

        // Enter 提交
        string? navigated = null;
        crumb.NavigateRequested += (_, p) => navigated = p;
        crumb.EnterEditMode();
        Assert.True(edit.IsVisible);
        edit.Text = @"C:\Users\Admin";
        edit.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Equal(@"C:\Users\Admin", navigated);
        Assert.False(edit.IsVisible);

        // Esc 取消：不上抛
        navigated = null;
        crumb.EnterEditMode();
        edit.Text = @"C:\Windows";
        edit.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.Null(navigated);
        Assert.False(edit.IsVisible);

        window.Close();
    }
}
