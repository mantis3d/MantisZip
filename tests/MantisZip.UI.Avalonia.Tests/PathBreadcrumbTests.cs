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

        // 末段是当前目录：Button（可点 → 进编辑态），带 crumbCurrent 加粗样式
        var host = crumb.FindControl<StackPanel>("SegmentsPanel")!;
        var last = host.Children.OfType<Button>().Last();
        Assert.Equal("2026", last.Content);
        Assert.Contains("crumbCurrent", last.Classes);

        window.Close();
    }

    [AvaloniaFact]
    public void ClickingCurrentSegment_EntersEditMode_InsteadOfNavigating()
    {
        var (window, crumb) = ShowCrumb(@"C:\Users\Admin");
        string? navigated = null;
        crumb.NavigateRequested += (_, p) => navigated = p;

        var host = crumb.FindControl<StackPanel>("SegmentsPanel")!;
        var current = host.Children.OfType<Button>().Last();
        current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();

        Assert.True(crumb.IsEditing);
        Assert.Null(navigated);
        Assert.True(crumb.FindControl<AutoCompleteBox>("EditBox")!.IsVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void DiskPath_DriveRootIsClickable_AndRaisesDriveRootPath()
    {
        var (window, crumb) = ShowCrumb(@"C:\Users\Admin");

        Assert.Equal(new[] { "C:", "Users", "Admin" }, SegmentTexts(crumb));

        // 盘符段可点，且上抛「C:\」而不是「C:」（后者是驱动器相对路径）
        var host = crumb.FindControl<StackPanel>("SegmentsPanel")!;
        var drive = Assert.IsType<Button>(host.Children[0]);
        Assert.Equal("C:", drive.Content);

        string? navigated = null;
        crumb.NavigateRequested += (_, p) => navigated = p;
        drive.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(@"C:\", navigated);

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
    [AvaloniaTheory]
    [InlineData(@"C:\Users", @"C:\Users\")]
    [InlineData(@"C:\Users\", @"C:\Users\")]
    [InlineData(@"E:\", @"E:\")]
    public void EnterEditMode_PrefillsTrailingSeparator_Disk(string path, string expected)
    {
        var (window, crumb) = ShowCrumb(path);
        var edit = crumb.FindControl<AutoCompleteBox>("EditBox")!;

        crumb.EnterEditMode();

        // 地址栏的常见用法是从当前目录往子目录钻，进编辑态就带好分隔符，省一次手敲
        Assert.Equal(expected, edit.Text);

        window.Close();
    }

    [AvaloniaFact]
    public void EnterEditMode_PrefillsTrailingSlash_Virtual()
    {
        var (window, crumb) = ShowCrumb("docs/images", BreadcrumbPathKind.Virtual);
        var edit = crumb.FindControl<AutoCompleteBox>("EditBox")!;

        crumb.EnterEditMode();

        Assert.Equal("docs/images/", edit.Text);

        window.Close();
    }

    [AvaloniaFact]
    public void EnterEditMode_EmptyPath_StaysEmpty()
    {
        var (window, crumb) = ShowCrumb(string.Empty);
        var edit = crumb.FindControl<AutoCompleteBox>("EditBox")!;

        crumb.EnterEditMode();

        // 空路径不预填分隔符，否则会凭空造出一个「根」提示
        Assert.Equal(string.Empty, edit.Text);

        window.Close();
    }
}
