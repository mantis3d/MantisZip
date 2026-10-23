using Avalonia.Headless.XUnit;
using MantisZip.Core.Models;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// Phase 2 / Task 11（恢复 Phase 1 隐藏的 UI）+ Task 10（默认规则集迁移）回归测试：
/// ① 设置页「魔数检测」「自适应规则」区块随自适应开关联动；
/// ② 格式目录的魔数 hex 恢复显示；
/// ③ 旧持久化的默认规则集迁移到与 SSOT 对齐的新默认规则集（用户自改规则集保持原样）。
/// </summary>
public class AdaptiveSettingsUiTests
{
    [AvaloniaFact]
    public void SmartDetectAndUserRulesSections_FollowAdaptiveCompressionSwitch()
    {
        var vm = new SettingsWindowViewModel();

        vm.AdaptiveCompression = false;
        Assert.False(vm.IsSmartDetectVisible);
        Assert.False(vm.IsUserRulesSectionVisible);

        vm.AdaptiveCompression = true;
        Assert.True(vm.IsSmartDetectVisible);
        Assert.True(vm.IsUserRulesSectionVisible);
    }

    [AvaloniaFact]
    public void FormatCatalogSection_FollowsAdaptiveCompressionSwitch()
    {
        var vm = new SettingsWindowViewModel();

        vm.AdaptiveCompression = false;
        Assert.False(vm.IsFormatCatalogSectionVisible);

        vm.AdaptiveCompression = true;
        Assert.True(vm.IsFormatCatalogSectionVisible);
    }

    [Fact]
    public void MagicHexDisplay_ShowsHexInBrackets()
    {
        var vm = new FormatDefinitionViewModel();

        Assert.Equal("", vm.MagicHexDisplay);

        vm.MagicHex = "FFD8FFE0";
        Assert.Equal("[FFD8FFE0]", vm.MagicHexDisplay);
    }

    [Fact]
    public void MigrateAdaptiveOverrides_RewritesLegacyDefaultRuleSet()
    {
        // 构造旧版默认规则集（含 svg/tar/wav 等未压缩格式）
        var settings = new AppSettings
        {
            AdaptiveOverridesDefaultsVersion = 0,
            AdaptiveOverrides = new List<AdaptiveOverrideRule>
            {
                new() { Name = "图片类", FormatIds = new() { "Jpeg", "Png", "WebP", "Bmp", "Gif", "Ico", "Tga", "Hdr", "Exr", "Svg" }, Level = AdaptiveLevel.Store, Enabled = true },
                new() { Name = "视频类", FormatIds = new() { "Mp4", "Mkv", "WebM", "Wmv", "Mov", "Avi", "Flv" }, Level = AdaptiveLevel.Store, Enabled = true },
                new() { Name = "音频类", FormatIds = new() { "Mp3", "Flac", "Wav", "Ogg" }, Level = AdaptiveLevel.Store, Enabled = true },
                new() { Name = "压缩包类", FormatIds = new() { "Zip", "SevenZip", "Rar", "Tar", "Gz", "Bz2", "Xz", "Zstd", "Iso" }, Level = AdaptiveLevel.Store, Enabled = true },
            },
        };

        AppSettings.MigrateAdaptiveOverridesDefaults(settings);

        Assert.Equal(1, settings.AdaptiveOverridesDefaultsVersion);
        var expected = AppSettings.GetDefaultAdaptiveOverrides();
        Assert.Equal(expected.Count, settings.AdaptiveOverrides.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Name, settings.AdaptiveOverrides[i].Name);
            Assert.Equal(expected[i].FormatIds, settings.AdaptiveOverrides[i].FormatIds);
        }
        // 与 SSOT 对齐：未压缩格式不再出现在默认规则里
        Assert.DoesNotContain("Svg", settings.AdaptiveOverrides.SelectMany(r => r.FormatIds));
        Assert.DoesNotContain("Tar", settings.AdaptiveOverrides.SelectMany(r => r.FormatIds));
        Assert.DoesNotContain("Wav", settings.AdaptiveOverrides.SelectMany(r => r.FormatIds));
    }

    [Fact]
    public void MigrateAdaptiveOverrides_KeepsUserCustomizedRuleSet()
    {
        var settings = new AppSettings
        {
            AdaptiveOverridesDefaultsVersion = 0,
            AdaptiveOverrides = new List<AdaptiveOverrideRule>
            {
                new() { Name = "我的规则", FormatIds = new() { "Txt" }, Level = AdaptiveLevel.Custom, CustomLevel = 3, Enabled = true },
            },
        };

        AppSettings.MigrateAdaptiveOverridesDefaults(settings);

        Assert.Equal(1, settings.AdaptiveOverridesDefaultsVersion);
        Assert.Single(settings.AdaptiveOverrides);
        Assert.Equal("我的规则", settings.AdaptiveOverrides[0].Name);
    }

    [Fact]
    public void MigrateAdaptiveOverrides_MigratesCustomStoreOnlyIdsIntoRule()
    {
        // 旧「仅存储格式」列表：默认项 + 用户额外添加项 → 只有用户项迁成规则
        // （注意 Wav/Tar 等本就属于旧默认列表，不算用户自定义项）
        var defaults = new[] { "Jpeg", "Png", "Mp4" };
        var settings = new AppSettings
        {
            AdaptiveOverridesDefaultsVersion = 0,
            AdaptiveOverrides = new List<AdaptiveOverrideRule>(),
            MultiThreadedStoreFormatIds = new List<string>(defaults) { "Txt", "MyFmt" },
        };

        AppSettings.MigrateAdaptiveOverridesDefaults(settings);

        Assert.Equal(1, settings.AdaptiveOverridesDefaultsVersion);
        var migrated = Assert.Single(settings.AdaptiveOverrides);
        Assert.Equal("仅存储格式（迁移）", migrated.Name);
        Assert.Equal(new[] { "Txt", "MyFmt" }, migrated.FormatIds);
        Assert.Equal(AdaptiveLevel.Store, migrated.Level);
        // 旧列表迁移后清空
        Assert.Empty(settings.MultiThreadedStoreFormatIds);
    }

    [Fact]
    public void DefaultRuleSet_CoversSixCategories_AndExcludesUncompressedFormats()
    {
        var defaults = AppSettings.GetDefaultAdaptiveOverrides();

        // 六类：图片 / 视频 / 音频 / 压缩包 / 字体 / 文档
        Assert.Equal(6, defaults.Count);
        var allIds = defaults.SelectMany(r => r.FormatIds).ToList();
        Assert.Contains("Jpeg", allIds);
        Assert.Contains("Ttf", allIds);
        Assert.Contains("Pdf", allIds);
        // 未压缩格式不在默认规则里
        Assert.DoesNotContain("Svg", allIds);
        Assert.DoesNotContain("Tar", allIds);
        Assert.DoesNotContain("Wav", allIds);
    }

    [Fact]
    public void RuleRow_ShowsMtIneffectiveBadge_OnlyWhenMtOnAndLevelNotStore()
    {
        var store = new AdaptiveOverrideRuleViewModel { Level = AdaptiveLevel.Store, IsMultiThreadedEnabled = true };
        var custom = new AdaptiveOverrideRuleViewModel { Level = AdaptiveLevel.Custom, CustomLevel = 3, IsMultiThreadedEnabled = true };
        var customNoMt = new AdaptiveOverrideRuleViewModel { Level = AdaptiveLevel.Custom, CustomLevel = 3, IsMultiThreadedEnabled = false };

        Assert.False(store.ShowMtIneffectiveBadge);
        Assert.True(custom.ShowMtIneffectiveBadge);
        Assert.False(customNoMt.ShowMtIneffectiveBadge);
    }

    [AvaloniaFact]
    public void MtWarningAndRuleBadge_UpdateLiveWhenMultiThreadedToggled()
    {
        var vm = new SettingsWindowViewModel { AdaptiveCompression = true };
        // 先归零，确保下面 true 一定会触发变更通知（本机 settings.json 可能已为 true）
        vm.MultiThreadedCompression = false;
        vm.AdaptiveOverrides.Clear();
        vm.AdaptiveOverrides.Add(new AdaptiveOverrideRuleViewModel { Level = AdaptiveLevel.Custom, CustomLevel = 3 });
        vm.AdaptiveOverrides.Add(new AdaptiveOverrideRuleViewModel { Level = AdaptiveLevel.Store });

        vm.MultiThreadedCompression = true;
        Assert.True(vm.IsAdaptiveOverridesMtWarningVisible);
        Assert.True(vm.AdaptiveOverrides[0].ShowMtIneffectiveBadge);
        Assert.False(vm.AdaptiveOverrides[1].ShowMtIneffectiveBadge); // Store 类始终生效

        vm.MultiThreadedCompression = false;
        Assert.False(vm.IsAdaptiveOverridesMtWarningVisible);
        Assert.False(vm.AdaptiveOverrides[0].ShowMtIneffectiveBadge);
    }
}
