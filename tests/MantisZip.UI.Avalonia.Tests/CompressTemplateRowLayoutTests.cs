using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 压缩设置窗「文件名」卡：模板输入 / 插入占位符 / 命名预设必须在**同一行**
/// （形态对齐设置页 <c>Views/SettingsWindow.axaml</c> 的三行模板输入）。
///
/// 背景（2026-11-19）：用户反馈「预设那行跟模板输入合并为一行，就跟设置里一样」——
/// 原来预设是独立一行（还带 <c>Margin="80,0,0,0"</c> 缩进），卡片高度白白多一行。
///
/// 为什么必须物化界面才能测：把两个控件塞进同一个 Grid 只是「看起来在同一行」，
/// 真正决定是否同行的是 ColumnDefinitions 的列数与各自的 <c>Grid.Column</c>。
/// 列写少一列、或者预设忘了写 Grid.Column（默认 0）就会跟标签列重叠，
/// 而编译期与纯源码文本断言都看不出来。
/// </summary>
public sealed class CompressTemplateRowLayoutTests
{
    /// <summary>模板输入框 / 插入按钮 / 预设选择器必须同属这一个四列网格。</summary>
    [AvaloniaFact]
    public void TemplateInsertAndPreset_ShareOneRow_WithFourColumns()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            Pump(window);

            var box = window.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(t => t.Name == "FileNameTemplateBox");
            var insert = window.GetVisualDescendants()
                .OfType<PlaceholderInsertButton>()
                .Single(b => ReferenceEquals(b.TargetTextBox, box));
            var picker = window.GetVisualDescendants()
                .OfType<TemplatePresetPicker>()
                .Single();

            // 三者必须是同一个 Grid 的直接子级（不在同一行时会落进不同的 StackPanel/Grid）
            var row = Assert.IsType<Grid>(box.Parent);
            Assert.Same(row, insert.Parent);
            Assert.Same(row, picker.Parent);

            // 列顺序：标签 0 / 模板框 1 / 插入按钮 2 / 预设选择器 3
            Assert.Equal(4, row.ColumnDefinitions.Count);
            Assert.Equal(1, Grid.GetColumn(box));
            Assert.Equal(2, Grid.GetColumn(insert));
            Assert.Equal(3, Grid.GetColumn(picker));

            // 后三列都是 Auto（只有模板框吃弹性），否则预设会被挤成固定宽度
            Assert.Equal(GridLength.Auto, row.ColumnDefinitions[2].Width);
            Assert.Equal(GridLength.Auto, row.ColumnDefinitions[3].Width);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 预设选择器必须落在模板输入框右侧、且两者垂直居中对齐（同一视觉行）。
    /// 分处两行时 Y 坐标会差一整行高度，这条会失败。
    /// </summary>
    [AvaloniaFact]
    public void PresetPicker_SitsRightOfTheTemplateBox_OnTheSameVisualLine()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            Pump(window);

            var box = window.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(t => t.Name == "FileNameTemplateBox");
            var picker = window.GetVisualDescendants().OfType<TemplatePresetPicker>().Single();

            var boxRight = box.TranslatePoint(new global::Avalonia.Point(box.Bounds.Width, 0), window);
            var pickerLeft = picker.TranslatePoint(new global::Avalonia.Point(0, 0), window);
            Assert.NotNull(boxRight);
            Assert.NotNull(pickerLeft);
            Assert.True(boxRight!.Value.X <= pickerLeft!.Value.X + 1,
                $"预设选择器左缘 {pickerLeft.Value.X} 应在模板输入框右缘 {boxRight.Value.X} 右侧");

            // 同一个视觉行：两者中心 Y 相差不超过半个控件高度（换行时会差一整行）
            var boxCenterY = box.TranslatePoint(new global::Avalonia.Point(0, box.Bounds.Height / 2), window);
            var pickerCenterY = picker.TranslatePoint(new global::Avalonia.Point(0, picker.Bounds.Height / 2), window);
            Assert.NotNull(boxCenterY);
            Assert.NotNull(pickerCenterY);
            Assert.True(Math.Abs(boxCenterY!.Value.Y - pickerCenterY!.Value.Y) <= box.Bounds.Height / 2,
                $"模板框中心 Y {boxCenterY.Value.Y} 与预设选择器中心 Y {pickerCenterY.Value.Y} 不在同一行");
        }
        finally
        {
            window.Close();
        }
    }

    private static CompressSettingsWindow CreateWindow()
    {
        EnsureTheme();
        var context = new CompressRunContext(
            Owner: null,
            Presenter: new NullPresenter(),
            ExitPolicy: ExitPolicy.None);
        return new CompressSettingsWindow(new NullSession(), context, Array.Empty<string>());
    }

    /// <summary>
    /// 无头宿主只配了裸 Application：没有 FluentTheme 时页签内容不会被物化
    /// （TemplatePresetPicker 在视觉树里根本找不到），照着设置页那组用例补一次主题。
    /// </summary>
    private static void EnsureTheme()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        if (app.Styles.OfType<FluentTheme>().Any())
        {
            return;
        }

        app.Styles.Add(new FluentTheme());
        app.Resources.MergedDictionaries.Add(
            new ResourceInclude(new Uri("avares://MantisZip.UI.Avalonia/"))
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

    private sealed class NullSession : ICompressSession
    {
        public Task<CompressOutcome> RunAsync(
            CompressSettings settings,
            CompressRunContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult<CompressOutcome>(CompressOutcome.Completed);
    }

    private sealed class NullPresenter : IProgressPresenter
    {
        public ProgressWindow? ActiveWindow => null;

        public Task<bool> RunAsync(ProgressRunRequest request) => Task.FromResult(true);
    }
}
