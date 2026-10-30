using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 解压设置窗内嵌选择器（v0.5.5「文件选择器去弹窗化」第三步）的回归用例。
///
/// 覆盖三件事：
/// ① 四个页签都在（输出设置 / 添加压缩包 / 文件过滤 / 加速）；
/// ② 两个选择器按内嵌用法配置（目标目录=PickFolder、压缩包=PickItems，都是简单模式）；
/// ③ 勾「高级模式」后压缩包选择器切回跨目录累积（与压缩窗对称，2026-10-30 用户反馈第 2 点）。
///
/// 另外锁住一个真实缺陷：新 key 只加进三份 JSON 还不够，VM 的 <c>LocalizedStrings</c>
/// 是白名单字典，漏登记会让复选框**只剩一个空框没有文字**（当时真机就是这么发现的）。
/// </summary>
public sealed class ExtractSettingsWindowEmbeddedPickerTests
{
    [AvaloniaFact]
    public void Window_HasFourTabs_WithNonEmptyHeaders()
    {
        var window = new ExtractSettingsWindow();
        try
        {
            window.Show();
            Pump(window);

            var tabs = window.GetVisualDescendants().OfType<TabControl>().FirstOrDefault();
            Assert.NotNull(tabs);
            // 五个 TabItem：四个业务页 + 「选择要解压的文件」（条目选择模式下才可见，2026-10-30）
        Assert.Equal(5, tabs!.ItemCount);

            var headers = tabs.Items.OfType<TabItem>()
                .Select(t => t.Header as string)
                .ToList();
            Assert.Equal(5, headers.Count); // 四个业务页 + 默认隐藏的「选择要解压的文件」页（2026-10-30）
            Assert.All(headers.Where(h => h is not null), h => Assert.False(string.IsNullOrWhiteSpace(h))); // 隐藏页的 Header 绑定可能尚未求值
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Pickers_AreConfiguredForEmbeddedUse()
    {
        var window = new ExtractSettingsWindow();
        try
        {
            window.Show();
            Pump(window);

            var destination = window.FindControl<CustomFilePickerControl>("DestinationPicker");
            var archive = window.FindControl<CustomFilePickerControl>("ArchivePicker");
            Assert.NotNull(destination);
            Assert.NotNull(archive);

            // 目标目录：PickFolder + 简单模式（当前浏览目录即目标）
            Assert.Equal(PickerMode.PickFolder, destination!.Mode);
            Assert.True(destination.IsSimpleMode);

            // 压缩包：PickItems + 简单模式（默认）；勾「高级模式」后切回累积模式
            Assert.Equal(PickerMode.PickItems, archive!.Mode);
            Assert.True(archive.IsSimpleMode);

            var advanced = window.FindControl<CheckBox>("AdvancedModeCheck");
            Assert.NotNull(advanced);
            advanced!.IsChecked = true;
            Pump(window);
            Assert.False(archive.IsSimpleMode);

            advanced.IsChecked = false;
            Pump(window);
            Assert.True(archive.IsSimpleMode);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 「高级模式」复选框必须有文字：文案走 <c>LocalizedStrings[Extract_PickerAdvancedMode]</c>，
    /// 而 VM 的字典是白名单构建的——漏登记时 Binding 静默失败，真机上只剩一个空框。
    /// </summary>
    [AvaloniaFact]
    public void AdvancedModeCheckBox_HasLocalizedLabel()
    {
        var window = new ExtractSettingsWindow();
        try
        {
            window.Show();
            Pump(window);

            var advanced = window.FindControl<CheckBox>("AdvancedModeCheck");
            Assert.NotNull(advanced);

            var label = advanced!.Content as string;
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.Equal(LocalizationManager.T("Extract_PickerAdvancedMode"), label);

            // VM 的白名单字典必须收录该 key，否则上面那行会在真实运行环境里拿到空串
            var vm = Assert.IsType<ExtractSettingsViewModel>(window.DataContext);
            Assert.True(vm.LocalizedStrings.ContainsKey("Extract_PickerAdvancedMode"));
        }
        finally
        {
            window.Close();
        }
    }

    private static void Pump(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}
