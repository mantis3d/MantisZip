using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using MantisZip.Core.Models;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 出厂默认规则名走 i18n 键（<c>NameKey</c>）而不是写死中文：
/// ① 编辑对话框标题区分「添加规则」/「编辑规则」（此前两者都用「添加规则」）；
/// ② 用户没改名就点确定时 <c>NameChanged</c> 必须为 false，
///    调用方据此保留 <c>NameKey</c>——否则出厂规则会被固化成当时语言的名字，之后切语言不再跟着变。
///
/// 2026-11-23：英文界面下「自适应规则」列表里六条规则一直显示中文，因为名字是写死的数据而非 i18n 文案。
/// </summary>
public class AdaptiveRuleNamingTests
{
    [AvaloniaFact]
    public void AddMode_TitleIsAddRule()
    {
        var dialog = new EditAdaptiveRuleDialog();

        Assert.Equal(LocalizationManager.T("Settings_AdaptiveRules_AddTitle"), dialog.Title);
        var prompt = dialog.FindControl<TextBlock>("PromptBlock");
        Assert.NotNull(prompt);
        Assert.Equal(LocalizationManager.T("Settings_AdaptiveRules_AddTitle"), prompt!.Text);
    }

    [AvaloniaFact]
    public void EditMode_TitleIsEditRule()
    {
        var dialog = new EditAdaptiveRuleDialog();

        dialog.SetExistingValues("图片类", AdaptiveLevel.Store);

        Assert.Equal(LocalizationManager.T("Settings_AdaptiveRules_EditTitle"), dialog.Title);
        var prompt = dialog.FindControl<TextBlock>("PromptBlock");
        Assert.Equal(LocalizationManager.T("Settings_AdaptiveRules_EditTitle"), prompt!.Text);
    }

    /// <summary>
    /// 打开编辑框什么都没改就点确定：必须不算改名。
    /// 这条是「比一下」与「有输入就清」的分水岭——后者会把出厂规则固化成一个语言的名字。
    /// </summary>
    [AvaloniaFact]
    public void EditMode_UnchangedName_DoesNotCountAsRename()
    {
        var dialog = new EditAdaptiveRuleDialog();
        dialog.SetExistingValues("图片类", AdaptiveLevel.Store);

        var ok = dialog.FindControl<Button>("OkButton");
        Assert.NotNull(ok);
        ok!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.False(dialog.NameChanged);
        Assert.Equal("图片类", dialog.RuleName);
    }

    /// <summary>改了名字才算改名（此时调用方清空 NameKey，改用用户输入）。</summary>
    [AvaloniaFact]
    public void EditMode_ChangedName_CountsAsRename()
    {
        var dialog = new EditAdaptiveRuleDialog();
        dialog.SetExistingValues("图片类", AdaptiveLevel.Store);

        var box = dialog.FindControl<TextBox>("RuleNameTextBox");
        Assert.NotNull(box);
        box!.Text = "我的图片";

        var ok = dialog.FindControl<Button>("OkButton");
        Assert.NotNull(ok);
        ok!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(dialog.NameChanged);
        Assert.Equal("我的图片", dialog.RuleName);
    }

    /// <summary>
    /// 名字在两侧空白上不同不算改名（TextBox 内容会被 Trim 后再比较）。
    /// </summary>
    [AvaloniaFact]
    public void EditMode_NameWithSurroundingWhitespace_DoesNotCountAsRename()
    {
        var dialog = new EditAdaptiveRuleDialog();
        dialog.SetExistingValues("图片类", AdaptiveLevel.Store);

        var box = dialog.FindControl<TextBox>("RuleNameTextBox");
        box!.Text = "  图片类  ";

        dialog.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.False(dialog.NameChanged);
        Assert.Equal("图片类", dialog.RuleName);
    }
}
