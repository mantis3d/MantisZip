using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;
using Xunit.Sdk;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 设置窗口「下拉框选中项」接线回归。
/// <para>
/// 起因（2026-11-03 用户实测）：ZIP 默认选项里的「默认读取代码页」显示空白。
/// 根因是 <c>ReadNameEncodingOptions</c> 当时是计算属性、每次读都返回新列表，
/// 而 <c>NameEncodingOption</c> 是 class（引用相等）——ComboBox 用 Equals 在 ItemsSource 里找选中项，
/// 找不到就把 SelectedItem 清成 null，于是显示空白。
/// </para>
/// <para>
/// 判据：每个 <c>SelectedXxxOption</c> 必须能在对应的 <c>XxxOptions</c> 里用 Equals 找到；
/// 找不到时把所有违规项一次列出来，避免逐个试。
/// </para>
/// </summary>
public class SettingsOptionWiringTests
{
    [AvaloniaFact]
    public void EverySelectedOption_IsContainedInItsOptionsCollection()
    {
        var vm = new SettingsWindowViewModel();
        var problems = new List<string>();

        var selectedProperties = typeof(SettingsWindowViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.StartsWith("Selected", StringComparison.Ordinal)
                        && p.Name.EndsWith("Option", StringComparison.Ordinal));

        foreach (var selectedProperty in selectedProperties)
        {
            var selected = selectedProperty.GetValue(vm);
            if (selected is null)
            {
                problems.Add($"{selectedProperty.Name} = <null>");
                continue;
            }

            // SelectedZipEncodingOption → ZipEncodingOptions
            var stem = selectedProperty.Name["Selected".Length..^"Option".Length];
            var optionsProperty = typeof(SettingsWindowViewModel).GetProperty(stem + "Options");
            if (optionsProperty?.GetValue(vm) is not IEnumerable options)
            {
                problems.Add($"{selectedProperty.Name}：找不到对应的 {stem}Options 属性");
                continue;
            }

            var items = options.Cast<object>().ToList();
            if (items.Count == 0)
            {
                problems.Add($"{selectedProperty.Name}：{stem}Options 是空集合");
                continue;
            }

            if (!items.Any(o => Equals(o, selected)))
            {
                problems.Add($"{selectedProperty.Name}[{selected}] 不在 {stem}Options（{items.Count} 项）里");
            }
        }

        if (problems.Count > 0)
        {
            throw new XunitException("下拉框选中项不在自己的选项集合里（界面会显示空白）：\n" + string.Join("\n", problems));
        }
    }

    /// <summary>
    /// 压缩级别的兜底：设置里存着不在预设档位（0/3/5/9）里的旧值时要退到最接近的档位，
    /// 否则 ComboBox 选中项为 null、同样显示空白。
    /// </summary>
    [Theory]
    [InlineData(4, "3")]
    [InlineData(8, "9")]
    [InlineData(12, "9")]
    [InlineData(-5, "0")]
    [InlineData(5, "5")]
    public void LevelFallback_PicksClosestOption(int level, string expectedValue)
    {
        var options = new List<Option>
        {
            new("仅存储", "0"),
            new("快速压缩", "3"),
            new("正常压缩", "5"),
            new("极限压缩", "9"),
        };

        var closest = SettingsWindowViewModel.FindClosestLevelOption(options, level);

        Assert.NotNull(closest);
        Assert.Equal(expectedValue, closest!.Value);
    }

    /// <summary>没有可解析数值的选项时返回 null（调用方据此保留原有的 null 行为）。</summary>
    [Fact]
    public void LevelFallback_WithoutNumericOptions_ReturnsNull()
    {
        var options = new List<Option> { new("未知", "abc") };

        Assert.Null(SettingsWindowViewModel.FindClosestLevelOption(options, 5));
    }
}
