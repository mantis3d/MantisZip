using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Views;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// issue #87「密码相关默认值可配置」的回归测试。
///
/// 四项默认值此前硬编码在 <see cref="CompressSettingsViewModel"/> 的字段初值与
/// <c>PasswordDialog.axaml</c> 的 <c>IsChecked</c> 里。现在它们来自 <see cref="AppSettings"/>，
/// 本类钉住三件事：
/// ① 出厂默认值与改动前的硬编码行为逐项一致（老用户升级后行为不变）；
/// ② 各消费端**真的读取**了设置（不是死开关 —— 这也是 <c>settingswiring</c> 守卫的要求）；
/// ③ 窗内临时改动只对本次生效，不回写设置。
/// </summary>
public class PasswordDefaultsSettingsTests
{
    [Fact]
    public void AppSettings_DefaultsMatchFormerHardcodedBehaviour()
    {
        // 改动前：密码模式恒为「密码库」，两个规则勾选恒为勾上，永久保存恒为不勾。
        // 默认值一旦漂移，没改过设置的老用户升级后就会看到行为变化。
        var settings = new AppSettings();

        Assert.Equal("library", settings.PasswordModeDefault);
        Assert.True(settings.AutoGenerateRulesByDefault);
        Assert.True(settings.UpdateMatchingRulesByDefault);
        Assert.False(settings.SavePermanentlyByDefault);
    }

    [Fact]
    public void AppSettings_EmbeddedDefaultsJson_CarriesAllFourKeys()
    {
        // 出厂 settings.json 是「设置文件缺失/损坏」时的回落来源，缺键会让新装用户
        // 拿到字段初值、而不是出厂文件里的值。四个键必须与 AppSettings 的初值一致。
        var json = ReadEmbeddedDefaults();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("library", root.GetProperty("PasswordModeDefault").GetString());
        Assert.True(root.GetProperty("AutoGenerateRulesByDefault").GetBoolean());
        Assert.True(root.GetProperty("UpdateMatchingRulesByDefault").GetBoolean());
        Assert.False(root.GetProperty("SavePermanentlyByDefault").GetBoolean());
    }

    [Fact]
    public void ApplyMigrations_UnknownPasswordMode_FallsBackToLibrary()
    {
        // 手改 settings.json 写错时不能让压缩设置窗打开后两档单选都不亮。
        var settings = new AppSettings { PasswordModeDefault = "bogus" };

        AppSettings.ApplyMigrations(settings);

        Assert.Equal("library", settings.PasswordModeDefault);
    }

    [Fact]
    public void ApplyMigrations_NewPasswordMode_IsPreserved()
    {
        var settings = new AppSettings { PasswordModeDefault = "new" };

        AppSettings.ApplyMigrations(settings);

        Assert.Equal("new", settings.PasswordModeDefault);
    }

    [AvaloniaFact]
    public void CompressSettingsViewModel_FollowsPasswordDefaults()
    {
        // 消费端 #1：压缩设置窗。这里必须真的读到设置 —— 只加设置页不接消费端会被
        // settingswiring 守卫判成死开关。
        using var sandbox = new AppSettingsTestSandbox();
        var settings = AppSettings.Load();
        settings.PasswordModeDefault = "new";
        settings.AutoGenerateRulesByDefault = false;
        settings.UpdateMatchingRulesByDefault = false;
        settings.Save();

        var vm = new CompressSettingsViewModel(Array.Empty<string>());

        Assert.False(vm.IsPasswordLibraryMode);
        Assert.False(vm.AutoGenerateRules);
        Assert.False(vm.SaveToLibrary);
    }

    [AvaloniaFact]
    public void CompressSettingsViewModel_TogglingInWindow_DoesNotWriteBackSettings()
    {
        // issue #87 第 4 条验收：窗内临时修改只对本次生效，不回写全局默认值。
        using var sandbox = new AppSettingsTestSandbox();
        var settings = AppSettings.Load();
        settings.PasswordModeDefault = "library";
        settings.AutoGenerateRulesByDefault = true;
        settings.UpdateMatchingRulesByDefault = true;
        settings.Save();

        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        vm.IsPasswordLibraryMode = false;
        vm.AutoGenerateRules = false;
        vm.SaveToLibrary = false;

        var reloaded = AppSettings.Load();
        Assert.Equal("library", reloaded.PasswordModeDefault);
        Assert.True(reloaded.AutoGenerateRulesByDefault);
        Assert.True(reloaded.UpdateMatchingRulesByDefault);
    }

    [AvaloniaFact]
    public void PasswordDialog_InitialCheckboxes_FollowSettings()
    {
        // 消费端 #2：密码对话框。两个勾选框此前是 XAML 里硬编码的初值。
        using var sandbox = new AppSettingsTestSandbox();
        var settings = AppSettings.Load();
        settings.SavePermanentlyByDefault = true;
        settings.AutoGenerateRulesByDefault = false;
        settings.Save();

        using var icons = new AppIconResources();
        var dialog = new PasswordDialog();

        Assert.True(dialog.SavePermanentlyCheck.IsChecked);
        Assert.False(dialog.AutoGenerateRulesCheck.IsChecked);
    }

    [AvaloniaFact]
    public void PasswordDialog_AutoRulesChecked_PrefillsPatternsFromFileName()
    {
        // 默认勾了「自动生成规则」时，带文件名的构造必须真的把规则文本填出来 ——
        // 构造顺序若把 FileName 赋值放在自动规则之后，这里会拿到空串。
        using var sandbox = new AppSettingsTestSandbox();
        var settings = AppSettings.Load();
        settings.SavePermanentlyByDefault = true;
        settings.AutoGenerateRulesByDefault = true;
        settings.Save();

        using var icons = new AppIconResources();
        var dialog = new PasswordDialog("样本.zip");

        Assert.True(dialog.AutoGenerateRulesCheck.IsChecked);
        Assert.Equal("样本*", dialog.PatternsTextBox.Text);
        Assert.True(dialog.PatternsTextBox.IsReadOnly);
    }

    [AvaloniaFact]
    public void PasswordDialog_TogglingInDialog_DoesNotWriteBackSettings()
    {
        using var sandbox = new AppSettingsTestSandbox();
        var settings = AppSettings.Load();
        settings.SavePermanentlyByDefault = false;
        settings.AutoGenerateRulesByDefault = true;
        settings.Save();

        using var icons = new AppIconResources();
        var dialog = new PasswordDialog("样本.zip");
        dialog.SavePermanentlyCheck.IsChecked = true;
        dialog.AutoGenerateRulesCheck.IsChecked = false;

        var reloaded = AppSettings.Load();
        Assert.False(reloaded.SavePermanentlyByDefault);
        Assert.True(reloaded.AutoGenerateRulesByDefault);
    }

    [AvaloniaFact]
    public void SettingsWindowViewModel_ExposesPasswordDefaults()
    {
        using var sandbox = new AppSettingsTestSandbox();
        var settings = AppSettings.Load();
        settings.PasswordModeDefault = "new";
        settings.AutoGenerateRulesByDefault = false;
        settings.UpdateMatchingRulesByDefault = false;
        settings.SavePermanentlyByDefault = true;
        settings.Save();

        var vm = new SettingsWindowViewModel();

        Assert.Equal("new", vm.SelectedPasswordModeDefaultOption?.Value);
        Assert.False(vm.AutoGenerateRulesByDefault);
        Assert.False(vm.UpdateMatchingRulesByDefault);
        Assert.True(vm.SavePermanentlyByDefault);
    }

    [AvaloniaFact]
    public void SettingsWindowViewModel_Save_PersistsPasswordDefaultsAndNormalizesMode()
    {
        using var sandbox = new AppSettingsTestSandbox();
        var vm = new SettingsWindowViewModel();

        // 选中「新密码」后保存：值应原样落盘
        vm.SelectedPasswordModeDefaultOption =
            vm.PasswordModeDefaultOptions.First(o => o.Value == "new");
        vm.AutoGenerateRulesByDefault = false;
        vm.SavePermanentlyByDefault = true;
        vm.SaveCommand.Execute(null);

        var saved = AppSettings.Load();
        Assert.Equal("new", saved.PasswordModeDefault);
        Assert.False(saved.AutoGenerateRulesByDefault);
        Assert.True(saved.SavePermanentlyByDefault);
    }

    private static string ReadEmbeddedDefaults()
    {
        using var stream = typeof(AppSettings).Assembly
            .GetManifestResourceStream(AppSettings.DefaultSettingsResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }
}
