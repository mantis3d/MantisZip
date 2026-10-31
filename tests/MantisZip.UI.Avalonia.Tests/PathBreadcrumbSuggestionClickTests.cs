using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using global::Avalonia.Markup.Xaml.Styling;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Controls;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 面包屑编辑态「点候选下拉」的行为回归（用户实测反馈「下拉列表点了没反应」）。
///
/// 关键事实（2026-10-29 真机 + 日志确认）：
/// ① 候选列表在 <see cref="Popup"/> 里，不在窗口可视树内，所以要顺着 <c>Popup.Child</c> 找候选项；
/// ② AutoCompleteBox 在**按下**瞬间就关下拉并把内容摘出可视树，所以提交必须挂在 PointerPressed 上，
///    挂在 PointerReleased 上永远收不到；
/// ③ 打字时 AutoCompleteBox 会自动把首个候选设为 <c>SelectedItem</c>，因此**不能**在 SelectionChanged 里提交，
///    否则输到一半就被自动导航走（实测输入 <c>E:</c> 就跳走）。
/// </summary>
public sealed class PathBreadcrumbSuggestionClickTests
{
    private static readonly string[] Suggestions =
    [
        @"C:\Users\Admin",
        @"C:\Users\Public",
    ];

    [AvaloniaFact]
    public void PressingSuggestion_RaisesNavigateRequested_WithPickedPath()
    {
        // AutoCompleteBox 的下拉依赖控件模板，模板来自主题：无头测试里必须自己挂上，否则 Popup 不会实例化
        var app = Application.Current!;
        var fluent = new global::Avalonia.Themes.Fluent.FluentTheme();
        app.Styles.Add(fluent);
        var themeResources = new ResourceInclude(new Uri("avares://MantisZip.UI.Avalonia/"))
        {
            Source = new Uri("avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml"),
        };
        app.Resources.MergedDictionaries.Add(themeResources);

        try
        {
            var crumb = new PathBreadcrumb
            {
                PathKind = BreadcrumbPathKind.Disk,
                Path = @"C:\Users",
                EditCompletionProvider = _ => Suggestions,
            };

            string? navigated = null;
            crumb.NavigateRequested += (_, path) => navigated = path;

            var window = new Window { Content = crumb, Width = 600, Height = 240 };
            window.Show();
            window.UpdateLayout();

            crumb.EnterEditMode();
            var editBox = crumb.FindControl<AutoCompleteBox>("EditBox");
            Assert.NotNull(editBox);

            editBox!.Text = @"C:\Users\A";
            editBox.IsDropDownOpen = true;
            Pump(window);

            var popupRoot = editBox.GetVisualDescendants().OfType<Popup>().FirstOrDefault()?.Child;
            Assert.NotNull(popupRoot);

            var item = popupRoot!.GetVisualDescendants()
                .OfType<ListBoxItem>()
                .FirstOrDefault(i => (i.DataContext as string) == @"C:\Users\Admin");
            Assert.NotNull(item);

            item!.RaiseEvent(new PointerPressedEventArgs(
                item,
                new Pointer(1, PointerType.Mouse, true),
                item,
                new Point(1, 1),
                0,
                new PointerPointProperties(),
                KeyModifiers.None));
            Pump(window);

            Assert.Equal(@"C:\Users\Admin", navigated);
            Assert.False(crumb.IsEditing);
        }
        finally
        {
            app.Styles.Remove(fluent);
            app.Resources.MergedDictionaries.Remove(themeResources);
        }
    }

    /// <summary>
    /// 下拉打开期间连续改文本不抛异常（冒烟）。
    /// 注意：这条**不能**复现真机那次崩溃——无头环境没有真实的指针/选择适配器路径，
    /// 把候选刷新改回同步也照样绿（已实测）。真机崩溃（2026-10-29）的真实栈是：
    /// AutoCompleteBox.PopulateDropDown → CloseDropDown → 把 SelectedItem 写回选择模型
    /// → 我们在 TextChanged 里同步换掉 ItemsSource → 选择模型残留旧索引 → ItemsSourceView.GetAt 越界
    /// （System.ArgumentOutOfRangeException）。修法是把候选刷新延后到 Background 优先级；
    /// 该修法的证据是真机压测（连续三轮快速输入 + Esc + 点候选，进程不退出）与事件日志无新条目。
    /// </summary>
    [AvaloniaFact]
    public void TypingWhileDropDownOpen_DoesNotThrow()
    {
        var app = Application.Current!;
        var fluent = new global::Avalonia.Themes.Fluent.FluentTheme();
        app.Styles.Add(fluent);
        var themeResources = new ResourceInclude(new Uri("avares://MantisZip.UI.Avalonia/"))
        {
            Source = new Uri("avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml"),
        };
        app.Resources.MergedDictionaries.Add(themeResources);

        try
        {
            var crumb = new PathBreadcrumb
            {
                PathKind = BreadcrumbPathKind.Disk,
                Path = @"C:\Users",
                EditCompletionProvider = _ => Suggestions,
            };

            var window = new Window { Content = crumb, Width = 600, Height = 240 };
            window.Show();
            window.UpdateLayout();

            crumb.EnterEditMode();
            var editBox = crumb.FindControl<AutoCompleteBox>("EditBox");
            Assert.NotNull(editBox);

            editBox!.IsDropDownOpen = true;
            foreach (var text in new[] { "C:", @"C:\U", @"C:\Users", @"C:\Users\" })
            {
                editBox.Text = text;
                Pump(window);
            }

            Assert.True(crumb.IsEditing);
        }
        finally
        {
            app.Styles.Remove(fluent);
            app.Resources.MergedDictionaries.Remove(themeResources);
        }
    }

    /// <summary>跑几轮布局与消息泵，让下拉 Popup 完成实例化与打开。</summary>
    private static void Pump(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}
