using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
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
        double width = 800)
    {
        var crumb = new PathBreadcrumb
        {
            PathKind = kind,
            Path = path,
            EditPlaceholderKey = "Breadcrumb_EditPlaceholder",
            ArchiveRootTooltipKey = "Breadcrumb_ArchiveRoot",
        };
        var window = new Window { Width = width, Height = 200, Content = crumb };
        window.Show();
        window.UpdateLayout();

        // 宽度折叠依赖外壳实际 Bounds（首次布局前是 0）：把那一轮的 Dispatcher 任务泵完，
        // 保证 SizeChanged → RefreshSegments 已经跑过，断言看到的才是最终结果
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
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
                    // 末段（当前目录）的 Content 是 TextBlock：它要支持 TextTrimming 自省略，
                    // 所以不再直接塞字符串（2026-11-25 改）
                    list.Add(b.Content switch
                    {
                        TextBlock tbContent => tbContent.Text ?? string.Empty,
                        { } content => content.ToString() ?? string.Empty,
                        null => string.Empty,
                    });
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
        // 末段的 Content 是 TextBlock（支持 TextTrimming 自省略），文字在 .Text 上
        Assert.Equal("2026", Assert.IsType<TextBlock>(last.Content).Text);
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
        Assert.Equal("C:", Assert.IsType<TextBlock>(drive.Content).Text);

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
        var usersButton = host.Children.OfType<Button>()
            .First(b => Assert.IsType<TextBlock>(b.Content).Text == "Users");
        usersButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(@"C:\Users", navigated);

        window.Close();
    }

    /// <summary>
    /// 折叠判据是**实际像素宽度**而不是段数（2026-11-25 改）。
    /// 反例锁：<c>C:\a\b\c\d\e\f\g\h</c>（9 段但极短）在 800px 窗口下以前会被
    /// <c>MaxSegments = 6</c> 白折成 6 段，现在必须全显示。
    /// </summary>
    [AvaloniaFact]
    public void DeepButNarrowPath_IsNotFolded()
    {
        var (window, crumb) = ShowCrumb(@"C:\a\b\c\d\e\f\g\h", width: 800);

        var texts = SegmentTexts(crumb);
        Assert.DoesNotContain("…", texts);          // 放得下就不允许出现省略号
        Assert.Equal(9, texts.Count);               // C: + a..h 共 9 段

        window.Close();
    }

    /// <summary>
    /// 另一半反例：段数少但很长时必须折叠。
    /// <c>C:\VeryLong…\AnotherVeryLong…\YetAnotherLong…</c> 只有 4 段，却远超 400px 窗口。
    /// </summary>
    [AvaloniaFact]
    public void WideButShallowPath_IsFolded()
    {
        var (window, crumb) = ShowCrumb(
            @"C:\VeryLongFolderNameForTesting\AnotherVeryLongFolderName\YetAnotherLongFolderName",
            width: 400);

        var texts = SegmentTexts(crumb);
        Assert.Contains("…", texts);
        Assert.Equal("C:", texts[0]);               // 首段永远保留
        Assert.Equal("YetAnotherLongFolderName", texts[^1]);   // 末段（当前目录）优先保留

        window.Close();
    }

    /// <summary>折叠后的 <c>…</c> 提示必须说明「隐藏了几段」，而不是某个具体段的路径。</summary>
    [AvaloniaFact]
    public void FoldedEllipsis_TooltipReportsHiddenSegmentCount()
    {
        var (window, crumb) = ShowCrumb(
            @"C:\VeryLongFolderNameForTesting\AnotherVeryLongFolderName\YetAnotherLongFolderName",
            width: 400);

        var texts = SegmentTexts(crumb);
        var host = crumb.FindControl<StackPanel>("SegmentsPanel")!;
        var dots = host.Children.OfType<TextBlock>().First(t => t.Text == "…");
        var tip = ToolTip.GetTip(dots) as string;

        // 期望值由「总段数 - 可见段数（… 本身不算）」推出，而不是写死 2 —
        // 折叠几段取决于字体宽度，写死会在字体/紧凑度档位变化时假红。
        const int totalSegments = 4;   // C: + 3 层目录
        var hidden = totalSegments - (texts.Count - 1);

        Assert.False(string.IsNullOrWhiteSpace(tip));
        Assert.Contains(hidden.ToString(), tip);
        // 不能退化成「复述第一个被隐藏段的路径」
        Assert.DoesNotContain("VeryLongFolderNameForTesting", tip);

        window.Close();
    }

    /// <summary>
    /// 续上一条的另一半：窗口从宽拉窄后必须**重新折叠**。
    /// 旧实现只在 <c>Path</c> 变化时重算，拉窄后停在旧结论、被 <c>ClipToBounds</c> 硬切。
    /// </summary>
    [AvaloniaFact]
    public void NarrowingWindow_FoldsPathThatNoLongerFits()
    {
        var (window, crumb) = ShowCrumb(
            @"C:\VeryLongFolderNameForTesting\AnotherVeryLongFolderName\YetAnotherLongFolderName",
            width: 1200);
        Assert.DoesNotContain("…", SegmentTexts(crumb));

        window.Width = 300;
        window.UpdateLayout();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.Contains("…", SegmentTexts(crumb));

        window.Close();
    }

    /// <summary>
    /// 末段自己就比可用宽度还宽时，用 <see cref="TextTrimming.CharacterEllipsis"/> 自省略，
    /// 而不是让外层 <c>ClipToBounds</c> 把字硬切掉（旧版会把 <c>GGGG…HHHH</c> 截成半个词且无省略号）。
    /// </summary>
    [AvaloniaFact]
    public void OverlongCurrentSegment_TrimsItselfInsteadOfBeingClipped()
    {
        var (window, crumb) = ShowCrumb(
            @"C:\AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            width: 300);

        var host = crumb.FindControl<StackPanel>("SegmentsPanel")!;
        var last = host.Children.OfType<Button>().Last();
        var label = Assert.IsType<TextBlock>(last.Content);
        Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);

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

    /// <summary>
    /// 段名里的下划线必须原样显示。反例锁：<c>C:\my_folder_name</c> 的祖先段此前用
    /// <c>Button.Content = "my_folder_name"</c>（字符串），而 <c>Button</c> 的 <c>ContentPresenter</c>
    /// 默认 <c>RecognizesAccessKey = true</c>，会把首个 <c>_</c> 当助记符前缀吃掉 ⇒ 渲染成 <c>myfolder_name</c>。
    /// 现在 Content 一律是 <see cref="TextBlock"/>，助记符解析被绕开。
    /// </summary>
    [AvaloniaFact]
    public void SegmentLabel_WithUnderscores_IsRenderedVerbatim()
    {
        var (window, crumb) = ShowCrumb(@"C:\my_folder_name\another_one");

        Assert.Equal(new[] { "C:", "my_folder_name", "another_one" }, SegmentTexts(crumb));

        // 断言的是「渲染出来的文字」：拿 TextBlock.TextLayout 的实际 run 文本，
        // 而不是 TextBlock.Text —— 后者无论是否被 AccessText 处理都原样，测不出这个缺陷。
        var host = crumb.FindControl<StackPanel>("SegmentsPanel")!;
        var labels = host.Children.OfType<Button>().Select(b => Assert.IsType<TextBlock>(b.Content)).ToList();
        Assert.Equal(3, labels.Count);
        foreach (var tb in labels)
        {
#pragma warning disable CS0618 // TextLayout 是 public 但官方标注「不用于外部」
            var rendered = string.Concat(tb.TextLayout!.TextLines.SelectMany(l => l.TextRuns).OfType<global::Avalonia.Media.TextFormatting.TextRun>().Select(r => r.Text));
#pragma warning restore CS0618
            Assert.Equal(tb.Text, rendered);
        }
        // 带下划线的两段必须是原样（盘符段 C: 本来就没有下划线，不参与）
        Assert.Contains(labels, tb => tb.Text == "my_folder_name");
        Assert.Contains(labels, tb => tb.Text == "another_one");

        window.Close();
    }
}
