using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using global::Avalonia.Markup.Xaml.Styling;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Dialogs;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 内嵌文件选择器「复选框模式」的真实指针语义测试（2026-11-03 与主窗口文件列表统一）。
///
/// 覆盖五件事：①关掉时点行即结果 ②打开后点行只高亮、不累积
/// ③点行内复选框才累积 ④Shift 点另一行复选框整段累积 ⑤右键菜单只在复选框模式下挂上。
///
/// 注意：本测试宿主只配了裸 <c>Application</c>，没有控件主题——ListBox 的模板来自主题，
/// 不挂主题就一个 <c>ListBoxItem</c> 都不会生成（排查花了不少时间，记在这里）。
/// </summary>
public class CustomFilePickerCheckboxModeTests
{
    private static (Window Window, CustomFilePickerControl Picker, ListBox List, string Dir) Show(bool checkboxMode)
    {
        EnsureTheme();

        var dir = Path.Combine(Path.GetTempPath(), "mz-picker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
        {
            File.WriteAllText(Path.Combine(dir, name), "x");
        }

        var picker = new CustomFilePickerControl
        {
            Mode = PickerMode.PickItems,
            IsCheckboxMode = checkboxMode,
        };
        var window = new Window { Width = 900, Height = 620, Content = picker };
        window.Show();
        picker.Initialize(initialPath: dir);

        // 初始目录由 AppSettings.DefaultPathOrder 决定（测试环境的顺序里未必有 context），
        // 这里显式导航到测试目录，保证行内容确定。
        Assert.True(picker.TryNavigate(dir));

        var list = picker.FindControl<ListBox>("FileList")
                   ?? picker.GetVisualDescendants().OfType<ListBox>().First();
        Pump(window);
        return (window, picker, list, dir);
    }

    private static void EnsureTheme()
    {
        var app = Application.Current!;
        if (app.Styles.OfType<global::Avalonia.Themes.Fluent.FluentTheme>().Any())
        {
            return;
        }

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

    private static void Click(Window window, Control target, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window);
        Assert.NotNull(point);
        window.MouseDown(point!.Value, MouseButton.Left, modifiers);
        window.MouseUp(point.Value, MouseButton.Left, modifiers);
        Pump(window);
    }

    private static List<ListBoxItem> Rows(ListBox list)
        => list.GetVisualDescendants().OfType<ListBoxItem>().ToList();

    private static CheckBox RowCheckBox(ListBoxItem row)
        => row.GetVisualDescendants().OfType<CheckBox>().First();

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
    public void WithoutCheckboxMode_ClickingRow_AccumulatesThatFile()
    {
        var (window, picker, list, dir) = Show(checkboxMode: false);
        var rows = Rows(list);
        Assert.Equal(3, rows.Count);

        Click(window, rows[1]);

        Assert.Equal(new[] { Path.Combine(dir, "b.txt") }, picker.SelectedPaths);
        Cleanup(window, dir);
    }

    [AvaloniaFact]
    public void WithCheckboxMode_ClickingRow_OnlyHighlightsWithoutAccumulating()
    {
        var (window, picker, list, dir) = Show(checkboxMode: true);
        var rows = Rows(list);

        Click(window, rows[1]);

        Assert.Empty(picker.SelectedPaths);
        Assert.True(rows[1].IsSelected);
        Cleanup(window, dir);
    }

    [AvaloniaFact]
    public void WithCheckboxMode_ClickingRowCheckBox_AccumulatesThatFile()
    {
        var (window, picker, list, dir) = Show(checkboxMode: true);
        var rows = Rows(list);

        Click(window, RowCheckBox(rows[1]));

        Assert.Equal(new[] { Path.Combine(dir, "b.txt") }, picker.SelectedPaths);
        Cleanup(window, dir);
    }

    [AvaloniaFact]
    public void WithCheckboxMode_ShiftClickingCheckBox_AccumulatesRange()
    {
        var (window, picker, list, dir) = Show(checkboxMode: true);
        var rows = Rows(list);

        Click(window, RowCheckBox(rows[0]));
        Click(window, RowCheckBox(rows[2]), RawInputModifiers.Shift);

        Assert.Equal(
            new[] { Path.Combine(dir, "a.txt"), Path.Combine(dir, "b.txt"), Path.Combine(dir, "c.txt") },
            picker.SelectedPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        Cleanup(window, dir);
    }

    [AvaloniaFact]
    public void WithCheckboxMode_FileList_HasThreeItemContextMenu()
    {
        var (window, picker, list, dir) = Show(checkboxMode: true);

        Assert.NotNull(list.ContextMenu);
        Assert.Equal(3, list.ContextMenu!.Items.Count);
        Assert.All(
            list.ContextMenu.Items.OfType<MenuItem>(),
            item => Assert.False(string.IsNullOrWhiteSpace(item.Header as string)));
        Cleanup(window, dir);
    }

    [AvaloniaFact]
    public void WithoutCheckboxMode_FileList_HasNoContextMenu()
    {
        var (window, picker, list, dir) = Show(checkboxMode: false);

        Assert.Null(list.ContextMenu);
        Cleanup(window, dir);
    }
}
