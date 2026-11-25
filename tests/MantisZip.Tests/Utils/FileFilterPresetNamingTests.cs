using System.Collections.Generic;
using System.Linq;
using MantisZip.Core.FileFilter;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// 内置过滤预设的命名契约（<see cref="FileFilterPreset.GetBuiltInPresets"/>）。
///
/// 内置预设名是**界面文案**，所以只能放 i18n 键、不能写死中文——否则英文/繁中界面下
/// 8 条内置预设原样显示中文。但 <see cref="FileFilterPreset.Name"/> 在本类型里
/// 兼任运行期标识（下拉选中回找、基底预设是否还在都按名字比对），因此显示名与标识名
/// 必须分开：<see cref="FileFilterPreset.NameKey"/> 承担显示，<see cref="FileFilterPreset.Key"/>
/// 承担标识。这组用例就是钉住这个分工，防止有人「顺手把 NameKey 抄过来」又填回 Name。
/// </summary>
public class FileFilterPresetNamingTests
{
    [Fact]
    public void BuiltIns_CarryNameKeyAndLeaveNameEmpty()
    {
        var presets = FileFilterPreset.GetBuiltInPresets();

        Assert.Equal(8, presets.Count);
        Assert.All(presets, p =>
        {
            Assert.True(p.IsBuiltIn);
            Assert.False(string.IsNullOrWhiteSpace(p.NameKey), "内置预设必须有 i18n 键，否则英文界面会显示中文");
            Assert.Equal(string.Empty, p.Name);
        });
    }

    [Fact]
    public void BuiltIns_HaveDistinctNameKeys()
    {
        var keys = FileFilterPreset.GetBuiltInPresets().Select(p => p.NameKey).ToList();

        // Key 是运行期标识：8 条重复任意一条，下拉里的选中回找就会指向错的那条
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Key_FallsBackToNameForUserPresets()
    {
        var custom = new FileFilterPreset("我的规则", new FileFilterCriteria());

        Assert.Null(custom.NameKey);
        Assert.Equal("我的规则", custom.Key);
        Assert.Equal("我的规则", custom.ToString());
    }

    [Fact]
    public void Key_PrefersNameKeyForBuiltIns()
    {
        var builtIn = FileFilterPreset.GetBuiltInPresets()[0];

        // 内置预设的 Name 是空串，拿它做标识会让 8 条全部命中空串
        Assert.Equal(builtIn.NameKey, builtIn.Key);
        Assert.NotEqual(string.Empty, builtIn.Key);
    }

    [Fact]
    public void ToString_ReturnsNameKeyForBuiltIns_SoCoreDoesNotLeakChineseText()
    {
        var presets = FileFilterPreset.GetBuiltInPresets();

        // Core 不引用 UI 的 LocalizationManager，本地化交给调用方；
        // 这里只保证「没有把中文硬编码漏出去」
        Assert.All(presets, p => Assert.Equal(p.NameKey, p.ToString()));
        Assert.DoesNotContain(presets, p => ContainsCjk(p.ToString()));
    }

    [Fact]
    public void BuiltIns_KeepTheirFilterSemantics()
    {
        var presets = FileFilterPreset.GetBuiltInPresets();
        var byKey = presets.ToDictionary(p => p.Key!);

        Assert.Contains(".jpg", byKey["FileFilter_BuiltInPreset_Image"].Criteria.IncludeExtensions);
        Assert.Equal(100L * 1024 * 1024, byKey["FileFilter_BuiltInPreset_LargeFile"].Criteria.MinSize);
        Assert.Equal("*.tmp;*.cache;*.log;*.bak", byKey["FileFilter_BuiltInPreset_ExcludeTemp"].Criteria.NamePattern);
        Assert.NotNull(byKey["FileFilter_BuiltInPreset_ThisMonth"].Criteria.MinDate);
        Assert.All(presets, p => Assert.True(p.Criteria.IsActive, "内置预设不能是空条件"));
    }

    private static bool ContainsCjk(string text)
        => text.Any(c => c >= 0x4E00 && c <= 0x9FFF);
}
