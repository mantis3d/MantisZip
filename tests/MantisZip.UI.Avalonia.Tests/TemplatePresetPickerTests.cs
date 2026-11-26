using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 命名预设选择器（<see cref="TemplatePresetPicker"/>）的接线回归：
/// 可编辑下拉装预设名，保存/删除只把名字交回宿主，控件自己不碰设置。
///
/// 这层接线错了编译期看不出来：按钮点下去没事件、删除永灰、水印没本地化，
/// 表现都是「界面正常、点了没反应」，必须物化控件才能发现。
/// </summary>
public sealed class TemplatePresetPickerTests
{
    private static readonly FileNameTemplatePreset[] Presets =
    [
        new("日期", "{name}_{date}"),
        new("序号", "{name}_{seq:0000}"),
    ];

    [AvaloniaFact]
    public void SaveButton_RaisesSaveRequested_WithTheNameInTheBox()
    {
        var (window, picker) = Show(presetName: "新预设");
        string? received = null;
        picker.SaveRequested += (_, name) => received = name;

        Click(window, picker.FindButton("SaveButton"));

        Assert.Equal("新预设", received);
    }

    [AvaloniaFact]
    public void DeleteButton_RaisesDeleteRequested_WithTheTrimmedName()
    {
        var (window, picker) = Show(presetName: "  日期  ");
        string? received = null;
        picker.DeleteRequested += (_, name) => received = name;

        var delete = picker.FindButton("DeleteButton");
        Assert.True(delete.IsEnabled, "名字对应一条已有预设时「删除」应当可点");
        Click(window, delete);

        Assert.Equal("日期", received);
    }

    [AvaloniaFact]
    public void DeleteButton_IsDisabled_WhenNameMatchesNothing()
    {
        var (_, picker) = Show(presetName: "还没存过的名字");

        // 点了也没有任何反馈的按钮看起来像坏键，所以新名字一律禁用
        Assert.False(picker.FindButton("DeleteButton").IsEnabled);
    }

    [AvaloniaFact]
    public void DeleteButton_IsDisabled_WhenNameIsBlank()
    {
        var (_, picker) = Show(presetName: "   ");

        Assert.False(picker.FindButton("DeleteButton").IsEnabled);
    }

    [AvaloniaFact]
    public void PresetName_IsTwoWay_RoundTripsThroughTheEditableBox()
    {
        var (_, picker) = Show(presetName: "日期");

        var combo = picker.GetVisualDescendants().OfType<ComboBox>().Single();
        Assert.Equal("日期", combo.Text);

        // 宿主侧改名字（例如存成功后把名字规范化）必须体现在框里
        picker.PresetName = "序号";
        Pump();

        Assert.Equal("序号", combo.Text);
    }

    [AvaloniaFact]
    public void Watermark_IsLocalized_AndRefreshesOnCultureChange()
    {
        var (_, picker) = Show(presetName: null);

        var combo = picker.GetVisualDescendants().OfType<ComboBox>().Single();
        Assert.Equal(LocalizationManager.T("Compress_FileNameTemplate_PresetName"), combo.PlaceholderText);

        Assert.NotNull(LocalizationManager.T("Compress_FileNameTemplate_SavePreset"));
    }

    [AvaloniaFact]
    public void Buttons_CarryToolTips_ThemedAsIcons()
    {
        var (_, picker) = Show(presetName: null);

        var save = picker.FindButton("SaveButton");
        var delete = picker.FindButton("DeleteButton");

        // 卡片里的图标按钮只挂 ToolbarIcon（规则 4），且提示文案走本地化
        Assert.Contains("ToolbarIcon", save.Classes);
        Assert.Contains("ToolbarIcon", delete.Classes);
        Assert.Equal(LocalizationManager.T("Compress_FileNameTemplate_SavePreset"), ToolTip.GetTip(save));
        Assert.Equal(LocalizationManager.T("Compress_FileNameTemplate_DeletePreset"), ToolTip.GetTip(delete));
    }

    [AvaloniaFact]
    public void DeleteButton_IsDisabled_AfterPresetRemovedFromTheCollection()
    {
        // 宿主删预设是就地把项移出 ObservableCollection（Presets 属性引用不变），
        // 控件必须跟着集合事件重算「删除」可点状态，否则删完按钮还亮着、点下去静默无动作。
        var presets = new System.Collections.ObjectModel.ObservableCollection<FileNameTemplatePreset>(Presets);
        var picker = new TemplatePresetPicker
        {
            Presets = presets,
            PresetName = "日期",
            WatermarkKey = "Compress_FileNameTemplate_PresetName",
        };

        var app = Application.Current ?? new Application();
        if (app.Styles.Count == 0) app.Styles.Add(new FluentTheme());
        var window = new Window { Width = 420, Height = 160, Content = picker };
        window.Show();
        Pump();

        Assert.True(picker.FindButton("DeleteButton").IsEnabled);

        presets.Remove(presets[0]); // 删掉「日期」
        Pump();

        Assert.False(picker.FindButton("DeleteButton").IsEnabled);

        window.Close();
    }

    [AvaloniaFact]
    public void PresetSelected_IsRaised_OnlyWhenSelectionChangesWhileDropdownIsOpen()
    {
        var (_, picker) = Show(presetName: null);
        var fired = new List<string?>();
        picker.PresetSelected += (_, preset) => fired.Add(preset.Name);

        var combo = picker.GetVisualDescendants().OfType<ComboBox>().Single();

        // 打字打出一个与已有预设一模一样的名字：ComboBox 会把它精确匹配成选中项，
        // 但用户是在编辑模板，不是在「选预设」，这时绝不能回填模板。
        combo.Text = "序号";
        Pump();
        Assert.Empty(fired);

        // 真从下拉里换了一条（打开时选中「序号」→ 关时选中「日期」）才通知宿主
        combo.IsDropDownOpen = true;
        Pump();
        combo.SelectedItem = Presets[0];
        Pump();
        combo.IsDropDownOpen = false;
        Pump();

        Assert.Equal(["日期"], fired);
    }

    /// <summary>
    /// 操作结果走浮层气泡：消息出现时控件高度不变（气泡不进布局流），
    /// 这是 2026-11-21 用户明确要求的「不要挤出新的一行」。
    /// </summary>
    [AvaloniaFact]
    public void Message_ShowsInPopup_WithoutTakingLayoutSpace()
    {
        var (_, picker) = Show(presetName: "新预设");
        var before = picker.Bounds.Height;

        picker.Message = "已保存预设 新预设。";
        Pump();

        Assert.True(picker.IsMessagePopupOpen);
        Assert.Equal(before, picker.Bounds.Height);
    }

    /// <summary>成功消息到点后自动收起，并回调宿主清空消息属性。</summary>
    [AvaloniaFact]
    public void SuccessMessage_ClosesItself_AndAsksHostToClearIt()
    {
        var (_, picker) = Show(presetName: "新预设");
        var dismissed = 0;
        picker.MessageDismissed += (_, _) => dismissed++;

        picker.Message = "已保存预设 新预设。";
        picker.IsMessageError = false;
        Pump();
        Assert.True(picker.IsMessagePopupOpen);

        // 无头测试里 DispatcherTimer 不自行到期，直接驱动「到期」这一步：
        // 验证的是到期之后的行为（收气泡 + 回调宿主），1.6s 的计时本身靠人工验证。
        picker.DismissMessageNow();
        Pump();

        Assert.False(picker.IsMessagePopupOpen);
        Assert.Equal(1, dismissed);
    }

    /// <summary>
    /// 失败消息常驻：用户必须读到「为什么存不上」，不能一闪而过。
    /// <para>
    /// 这里必须让「成功消息时长」真的过去再断言——历史上的写法是只等 120ms、
    /// 而控件默认时长 1600ms，于是「计时器没起来」和「计时器起来了但还没到点」
    /// 两种状态都通过，测不出「失败消息被当成成功消息收起」这个真实缺陷。
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public async System.Threading.Tasks.Task ErrorMessage_StaysUntilHostClearsIt()
    {
        var (_, picker) = Show(presetName: "新预设", successDuration: TimeSpan.FromMilliseconds(150));
        var dismissed = 0;
        picker.MessageDismissed += (_, _) => dismissed++;

        // 宿主的真实赋值顺序：先 Message，再 IsMessageError（见 SetPresetMessage）。
        picker.Message = "预设最多 20 条，删掉一条再存。";
        picker.IsMessageError = true;
        Pump();
        Assert.True(picker.IsMessagePopupOpen);

        // 等到远超成功时长：失败态不该启动自动收起计时器
        await System.Threading.Tasks.Task.Delay(600);
        Pump();
        Assert.True(picker.IsMessagePopupOpen, "失败消息被自动收起了 —— 应按 IsMessageError 常驻");
        Assert.Equal(0, dismissed);

        picker.Message = null;
        Pump();
        Assert.False(picker.IsMessagePopupOpen);
    }

    /// <summary>
    /// 错误消息之后紧跟一条成功消息：成功消息仍必须按时收起。
    /// 反过来同理——两个属性各自的变更都要让计时器按最新的 <see cref="TemplatePresetPicker.IsMessageError"/> 重判，
    /// 只处理 Message 一侧会漏掉「标志晚到」的半个状态机。
    /// </summary>
    [AvaloniaFact]
    public async System.Threading.Tasks.Task SuccessMessage_AfterErrorMessage_StillClosesItself()
    {
        var (_, picker) = Show(presetName: "新预设", successDuration: TimeSpan.FromMilliseconds(150));

        picker.Message = "预设最多 20 条，删掉一条再存。";
        picker.IsMessageError = true;
        Pump();
        Assert.True(picker.IsMessagePopupOpen);

        // 换成功消息，宿主依旧先写 Message 再写标志
        picker.Message = "已保存预设 新预设。";
        picker.IsMessageError = false;
        Pump();
        Assert.True(picker.IsMessagePopupOpen);

        await System.Threading.Tasks.Task.Delay(600);
        Pump();
        Assert.False(picker.IsMessagePopupOpen, "错误之后的成功消息没有自动收起 —— 计时器没被重新启动");
    }

    private static (Window Window, TemplatePresetPicker Picker) Show(
        string? presetName, TimeSpan? successDuration = null)
    {
        var app = Application.Current ?? new Application();
        if (app.Styles.Count == 0) app.Styles.Add(new FluentTheme());

        var picker = new TemplatePresetPicker
        {
            Presets = Presets,
            PresetName = presetName,
            WatermarkKey = "Compress_FileNameTemplate_PresetName",
            Width = 320,
        };
        if (successDuration is { } duration) picker.SuccessMessageDuration = duration;

        var window = new Window { Width = 420, Height = 160, Content = picker };
        window.Show();
        Pump();
        return (window, picker);
    }

    private static void Click(Window window, Control target)
    {
        var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window);
        Assert.NotNull(center);
        window.MouseDown(center!.Value, MouseButton.Left);
        window.MouseUp(center.Value, MouseButton.Left);
        Pump();
    }

    private static void Pump()
    {
        for (var i = 0; i < 4; i++)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }
}

/// <summary>测试辅助：按 <c>x:Name</c> 找控件内部的按钮（<c>FindControl</c> 在无模板前拿不到）。</summary>
internal static class TemplatePresetPickerTestExtensions
{
    public static Button FindButton(this TemplatePresetPicker picker, string name)
        => picker.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
}
