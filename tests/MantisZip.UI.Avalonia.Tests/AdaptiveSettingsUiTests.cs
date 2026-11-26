using Avalonia.Headless.XUnit;
using MantisZip.Core.Models;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
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

        Assert.Equal(2, settings.AdaptiveOverridesDefaultsVersion);
        var expected = AppSettings.GetDefaultAdaptiveOverrides();
        Assert.Equal(expected.Count, settings.AdaptiveOverrides.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            // 出厂规则只用 NameKey 标识，Name 留空（v2：名称随界面语言切换）
            Assert.Equal(expected[i].NameKey, settings.AdaptiveOverrides[i].NameKey);
            Assert.Equal(expected[i].FormatIds, settings.AdaptiveOverrides[i].FormatIds);
        }
        // 与 SSOT 对齐：未压缩格式不再出现在默认规则里
        Assert.DoesNotContain("Svg", settings.AdaptiveOverrides.SelectMany(r => r.FormatIds));
        Assert.DoesNotContain("Tar", settings.AdaptiveOverrides.SelectMany(r => r.FormatIds));
        Assert.DoesNotContain("Wav", settings.AdaptiveOverrides.SelectMany(r => r.FormatIds));
    }

    /// <summary>
    /// 旧默认集是靠**名称**识别的，用户只要改过名字就识别不出来。
    /// 2026-11-23 改成按格式 ID 集合判定后，改名不再影响升级。
    /// </summary>
    [Fact]
    public void MigrateAdaptiveOverrides_StillDetectsLegacySet_WhenUserRenamedRules()
    {
        var settings = new AppSettings
        {
            AdaptiveOverridesDefaultsVersion = 0,
            AdaptiveOverrides = new List<AdaptiveOverrideRule>
            {
                new() { Name = "我的图片", FormatIds = new() { "Jpeg", "Png", "WebP", "Bmp", "Gif", "Ico", "Tga", "Hdr", "Exr", "Svg" }, Level = AdaptiveLevel.Store, Enabled = true },
                new() { Name = "my video", FormatIds = new() { "Mp4", "Mkv", "WebM", "Wmv", "Mov", "Avi", "Flv" }, Level = AdaptiveLevel.Store, Enabled = true },
                new() { Name = "", FormatIds = new() { "Mp3", "Flac", "Wav", "Ogg" }, Level = AdaptiveLevel.Store, Enabled = true },
                new() { Name = "压缩", FormatIds = new() { "Zip", "SevenZip", "Rar", "Tar", "Gz", "Bz2", "Xz", "Zstd", "Iso" }, Level = AdaptiveLevel.Store, Enabled = true },
            },
        };

        AppSettings.MigrateAdaptiveOverridesDefaults(settings);

        Assert.Equal(2, settings.AdaptiveOverridesDefaultsVersion);
        Assert.Equal(6, settings.AdaptiveOverrides.Count);
        Assert.All(settings.AdaptiveOverrides, r => Assert.False(string.IsNullOrEmpty(r.NameKey)));
    }

    /// <summary>
    /// v1 → v2 迁移：硬盘上已经是 v1 六类默认规则（名称写死中文）的用户，
    /// 升级后必须变成 NameKey 版，否则他们的界面一辈子是中文名。
    /// </summary>
    [Fact]
    public void MigrateAdaptiveOverrides_V1ChineseNames_ConvertToNameKeys()
    {
        var settings = new AppSettings
        {
            AdaptiveOverridesDefaultsVersion = 1,
            AdaptiveOverrides = new List<AdaptiveOverrideRule>
            {
                new() { Name = "图片类", FormatIds = new() { "Jpeg", "Png" }, Level = AdaptiveLevel.Store, Enabled = true },
                new() { Name = "视频类", FormatIds = new() { "Mp4" }, Level = AdaptiveLevel.Store, Enabled = false },
                new() { Name = "我的自定义", FormatIds = new() { "Txt" }, Level = AdaptiveLevel.Custom, CustomLevel = 3, Enabled = true },
                new() { Name = "仅存储格式（迁移）", FormatIds = new() { "MyFmt" }, Level = AdaptiveLevel.Store, Enabled = true },
            },
        };

        AppSettings.MigrateAdaptiveOverridesDefaults(settings);

        Assert.Equal(2, settings.AdaptiveOverridesDefaultsVersion);
        Assert.Equal("Settings_AdaptiveRuleName_Image", settings.AdaptiveOverrides[0].NameKey);
        Assert.Equal("", settings.AdaptiveOverrides[0].Name);
        // 用户改过的级别/启用状态保留
        Assert.Equal(AdaptiveLevel.Store, settings.AdaptiveOverrides[1].Level);
        Assert.False(settings.AdaptiveOverrides[1].Enabled);
        Assert.Equal("Settings_AdaptiveRuleName_Video", settings.AdaptiveOverrides[1].NameKey);
        // 用户自建规则不动（名字是用户自己敲的）
        Assert.Null(settings.AdaptiveOverrides[2].NameKey);
        Assert.Equal("我的自定义", settings.AdaptiveOverrides[2].Name);
        Assert.Equal("Settings_AdaptiveRuleName_MigratedStoreOnly", settings.AdaptiveOverrides[3].NameKey);
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

        Assert.Equal(2, settings.AdaptiveOverridesDefaultsVersion);
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

        Assert.Equal(2, settings.AdaptiveOverridesDefaultsVersion);
        var migrated = Assert.Single(settings.AdaptiveOverrides);
        // 迁移生成的规则名也走 i18n 键，随界面语言切换
        Assert.Equal("Settings_AdaptiveRuleName_MigratedStoreOnly", migrated.NameKey);
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

    /// <summary>
    /// 出厂规则不放中文名，只放 i18n 键；否则英文界面下仍是中文（2026-11-23）。
    /// </summary>
    [Fact]
    public void DefaultRuleSet_NamesAreI18nKeys_NotHardcodedChinese()
    {
        var defaults = AppSettings.GetDefaultAdaptiveOverrides();

        Assert.All(defaults, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.NameKey));
            Assert.Equal("", rule.Name);
        });
        Assert.Equal(
            new[]
            {
                "Settings_AdaptiveRuleName_Image", "Settings_AdaptiveRuleName_Video",
                "Settings_AdaptiveRuleName_Audio", "Settings_AdaptiveRuleName_Archive",
                "Settings_AdaptiveRuleName_Font", "Settings_AdaptiveRuleName_Document",
            },
            defaults.Select(r => r.NameKey));
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

    /// <summary>
    /// 规则行显示名：有 NameKey 走本地化（切语言跟着变），没有则显示用户自己写的 Name。
    /// </summary>
    [Fact]
    public void RuleDisplayName_UsesNameKeyWhenPresent_ElseUserName()
    {
        var builtIn = new AdaptiveOverrideRuleViewModel { NameKey = "Settings_AdaptiveRuleName_Image" };
        var custom = new AdaptiveOverrideRuleViewModel { Name = "我的规则", NameKey = null };
        var blank = new AdaptiveOverrideRuleViewModel();

        Assert.Equal(LocalizationManager.T("Settings_AdaptiveRuleName_Image"), builtIn.DisplayName);
        Assert.Equal("我的规则", custom.DisplayName);
        Assert.Equal("", blank.DisplayName);
    }

    /// <summary>
    /// 切语言后必须能逐行刷新：<c>NotifyDisplayName()</c> 要真的发出 <c>DisplayName</c> 的变更通知，
    /// 否则界面上的出厂规则名会停在旧语言。
    ///
    /// 这里只验证通知本身，不切换 <see cref="LocalizationManager.CurrentLanguage"/>——
    /// 全局切语言会触发 <c>CultureChanged</c>，让已挂接的界面控件（如 PathBreadcrumb）在无头环境里抛异常，
    /// 属于测试隔离问题而非被测行为。文案解析已由 <c>RuleDisplayName_UsesNameKeyWhenPresent_ElseUserName</c> 覆盖。
    /// </summary>
    [Fact]
    public void NotifyDisplayName_RaisesPropertyChanged()
    {
        var rule = new AdaptiveOverrideRuleViewModel { NameKey = "Settings_AdaptiveRuleName_Image" };
        var raised = new List<string?>();
        ((System.ComponentModel.INotifyPropertyChanged)rule).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        rule.NotifyDisplayName();

        Assert.Contains(nameof(AdaptiveOverrideRuleViewModel.DisplayName), raised);
    }

    /// <summary>
    /// 「恢复默认」必须真的重建出厂六类规则，而不是把现有规则全禁掉
    /// （2026-11-23 修正的实现只是 `Enabled = false`，按钮名不副实）。
    /// </summary>
    [AvaloniaFact]
    public void ResetDefaults_RebuildsFactoryRuleSet()
    {
        var vm = new SettingsWindowViewModel { AdaptiveCompression = true };
        vm.AdaptiveOverrides.Clear();
        vm.AdaptiveOverrides.Add(new AdaptiveOverrideRuleViewModel { Name = "我的规则", Enabled = false });

        vm.RestoreFactoryRules();

        Assert.Equal(6, vm.AdaptiveOverrides.Count);
        Assert.All(vm.AdaptiveOverrides, r => Assert.False(string.IsNullOrEmpty(r.NameKey)));
        Assert.All(vm.AdaptiveOverrides, r => Assert.True(r.Enabled));
        // 用户自建规则被清掉
        Assert.DoesNotContain(vm.AdaptiveOverrides, r => r.Name == "我的规则");
    }
}
