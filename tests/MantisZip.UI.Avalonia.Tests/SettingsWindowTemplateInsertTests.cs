using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Views;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 设置页「压缩 → 常规 → 文件名模板」三行输入框上的「插入占位符」按钮回归用例。
///
/// 这里只验「接线」不验手势（手势已有 <c>PlaceholderInsertButtonTests</c>）：
/// 四列网格改错会让按钮落到错误的列、或者 <c>TargetTextBox</c> 指向了别的行，
/// 而这两类错误编译期完全看不出来，必须物化 XAML 才能发现。
/// </summary>
public sealed class SettingsWindowTemplateInsertTests
{
    private static readonly string[] BoxNames =
    [
        "FileNameTemplateDialogBox",
        "FileNameTemplateQuickBox",
        "FileNameTemplateSeparateBox",
    ];

    [AvaloniaFact]
    public void ThreeRows_HaveAnInsertButton_WiredToTheirOwnTextBox()
    {
        EnsureTheme();
        var window = new SettingsWindow();
        try
        {
            window.Show();
            Pump(window);

            var buttons = window.GetVisualDescendants()
                .OfType<PlaceholderInsertButton>()
                .ToList();
            Assert.Equal(3, buttons.Count);

            foreach (var boxName in BoxNames)
            {
                var box = window.GetVisualDescendants()
                    .OfType<TextBox>()
                    .Single(t => t.Name == boxName);

                // 每一行恰好一个按钮，且指向本行输入框（顺序接错时这里会命中 0 或 2 个）
                var wired = buttons.Where(b => ReferenceEquals(b.TargetTextBox, box)).ToList();
                Assert.Single(wired);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InsertButtons_SitLeftOfThePresetCombo_InTheSameRow()
    {
        EnsureTheme();
        var window = new SettingsWindow();
        try
        {
            window.Show();
            Pump(window);

            var box = window.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(t => t.Name == "FileNameTemplateDialogBox");
            var button = window.GetVisualDescendants()
                .OfType<PlaceholderInsertButton>()
                .Single(b => ReferenceEquals(b.TargetTextBox, box));

            // 同一行里「输入框 → 按钮 → 预设下拉」从左到右，按钮必须卡在中间
            var combos = window.GetVisualDescendants()
                .OfType<ComboBox>()
                .Where(c => Grid.GetColumn(c) == 3)
                .ToList();
            Assert.NotEmpty(combos);

            Assert.Equal(1, Grid.GetColumn(box));
            Assert.Equal(2, Grid.GetColumn(button));

            var buttonRight = button.TranslatePoint(new Point(button.Bounds.Width, 0), window);
            Assert.NotNull(buttonRight);
            foreach (var combo in combos)
            {
                var comboLeft = combo.TranslatePoint(new Point(0, 0), window);
                Assert.NotNull(comboLeft);
                // 按钮整体在预设下拉左侧（列顺序接反时会在这里失败）
                Assert.True(buttonRight!.Value.X <= comboLeft!.Value.X + 1,
                    $"插入按钮右缘 {buttonRight.Value.X} 应不大于预设下拉左缘 {comboLeft.Value.X}");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InsertButtonLabel_IsTheSharedLocalizedText()
    {
        EnsureTheme();
        var window = new SettingsWindow();
        try
        {
            window.Show();
            Pump(window);

            var button = window.GetVisualDescendants().OfType<PlaceholderInsertButton>().First();
            Assert.Equal(MantisZip.UI.Avalonia.Services.LocalizationManager.T("Compress_FileNameTemplate_Insert"),
                button.Content as string);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>无头宿主只配了裸 Application，控件模板（含 TextBox/ComboBox 默认外观）需要主题。</summary>
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
            new ResourceInclude(new System.Uri("avares://MantisZip.UI.Avalonia/"))
            {
                Source = new System.Uri("avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml"),
            });
    }

    private static void Pump(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}
