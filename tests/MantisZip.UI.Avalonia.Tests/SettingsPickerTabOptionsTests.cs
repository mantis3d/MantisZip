using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 设置窗口「路径」标签页的标签策略回归测试：
/// ① 固定标签下拉的四项与 <c>Controls.PathTab</c> 成员名一一对应（该字符串同时是
///    <c>AppSettings.PickerSourceTab</c> 的持久化值，改成员名会静默丢失用户选择）；
/// ② 新增的三组文案 key 均有对应翻译（不是回退成 key 本身）。
/// </summary>
public class SettingsPickerTabOptionsTests
{
    [AvaloniaFact]
    public void PickerTabOptions_MatchPathTabMemberNames()
    {
        var vm = new SettingsWindowViewModel();

        var values = vm.PickerTabOptions.Select(o => o.Value).ToArray();

        Assert.Equal(["Favorites", "History", "Windows", "Tree"], values);
    }

    [AvaloniaFact]
    public void PickerTabOptions_EachHasLocalizedDisplayName()
    {
        var vm = new SettingsWindowViewModel();

        Assert.All(vm.PickerTabOptions, o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Display));
            Assert.DoesNotContain("QuickPath_Tab", o.Display);
        });
    }

    [AvaloniaFact]
    public void PickerTabOption_IsNeverNull()
    {
        var vm = new SettingsWindowViewModel();

        // 持久化值为空（首次运行）或非法时也必须落到「收藏」，避免下拉显示空白
        Assert.NotNull(vm.SelectedPickerTabOption);
        Assert.Contains(vm.SelectedPickerTabOption, vm.PickerTabOptions);
    }

    [AvaloniaFact]
    public void PickerTabTexts_AreLocalized()
    {
        var vm = new SettingsWindowViewModel();

        Assert.DoesNotContain("Settings_PickerTab", vm.PickerTabGroupHeader);
        Assert.DoesNotContain("Settings_PickerTab", vm.PickerTabRememberText);
        Assert.DoesNotContain("Settings_PickerTab", vm.PickerTabFixedText);
        Assert.DoesNotContain("Settings_PickerTab", vm.PickerTabHint);
        Assert.DoesNotContain("Settings_Tab_Path", vm.TabPathHeader);
    }
}
