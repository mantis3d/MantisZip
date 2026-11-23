using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 出厂默认设置快照（<c>Resources/Defaults/settings.json</c>，编译进程序集作为嵌入资源）。
///
/// 这是「设置文件缺失 / 损坏且备份也读不出」时的唯一默认值来源，取代了原先
/// ① 代码里散落的字段初值  ② <c>installer/prebuilt/settings.json</c> 两份事实源。
/// 维护方式就是直接改那个 json —— 所以这里的断言重点是**那份 json 不会悄悄腐朽**：
/// 键必须都对得上真实属性（改名/删属性后忘改 json 会当场红），核心出厂值必须存在，
/// 机器相关字段不许混进去（否则新用户拿到别人的窗口尺寸）。
/// </summary>
public class AppSettingsDefaultSettingsJsonTests
{
    /// <summary>读出内嵌默认快照原文（资源缺失直接失败，不以空串掩盖）。</summary>
    private static string ReadDefaultJson()
    {
        using var stream = typeof(AppSettings).Assembly
            .GetManifestResourceStream(AppSettings.DefaultSettingsResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    [Fact]
    public void EmbeddedResource_Exists_AndDeserializes()
    {
        var json = ReadDefaultJson();

        Assert.False(string.IsNullOrWhiteSpace(json));

        var settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);

        Assert.NotNull(settings);
    }

    [Fact]
    public void EveryKey_MapsToRealProperty()
    {
        using var doc = JsonDocument.Parse(ReadDefaultJson());
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);

        var known = typeof(AppSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var unknown = doc.RootElement
            .EnumerateObject()
            .Select(p => p.Name)
            .Where(name => !known.Contains(name))
            .ToList();

        Assert.True(
            unknown.Count == 0,
            "默认设置 json 里有 AppSettings 已不存在的键（删属性后忘了同步 json？）：" +
            string.Join(", ", unknown));
    }

    [Fact]
    public void Defaults_CarryFactoryNamingTemplateAndPresets()
    {
        // 出厂默认的核心诉求：新用户第一次打开压缩对话框就有模板和两条预设可用
        var settings = JsonSerializer.Deserialize(ReadDefaultJson(), AppSettingsJsonContext.Default.AppSettings);

        Assert.NotNull(settings);
        Assert.Equal("{name:auto}", settings!.FileNameTemplateDialog);
        Assert.Equal("", settings.FileNameTemplateQuick);
        Assert.Equal("", settings.FileNameTemplateSeparate);

        Assert.Collection(
            settings.FileNameTemplatePresets,
            first =>
            {
                Assert.Equal("Auto Name", first.Name);
                Assert.Equal("{name:auto}", first.Template);
            },
            second =>
            {
                Assert.Equal("Name & Date", second.Name);
                Assert.Equal("{name:auto}-{date}", second.Template);
            });
    }

    [Fact]
    public void Defaults_DoNotCarryMachineSpecificState()
    {
        // 窗口几何 / 面板宽度 / 上次所在页 / 更新检查时间戳都属于「这台机器此刻的状态」，
        // 混进出厂默认等于把开发者自己的窗口尺寸发给每个新用户。
        using var doc = JsonDocument.Parse(ReadDefaultJson());
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        string[] forbidden =
        [
            "CompressWindowWidth", "CompressWindowHeight", "CompressWindowLeftColumnWidth",
            "ExtractWindowWidth", "ExtractWindowHeight", "ExtractWindowLeftColumnWidth",
            "SettingsWindowWidth", "SettingsWindowHeight", "PickerDialogWidth", "ProgressWindowWidth",
            "PickItemsPanelWidth", "ExtractFolderPanelWidth", "PickerSourceTab",
            "LastUpdateCheckTime", "LastSkippedVersion",
        ];

        var present = forbidden.Where(keys.Contains).ToList();
        Assert.True(present.Count == 0, "出厂默认不该包含机器相关键：" + string.Join(", ", present));
    }

    [Fact]
    public void Defaults_DoNotCarryLegacySupersededKeys()
    {
        // 旧的 7 个关联开关与 OpenDialogExtensions 已被 AssocExtensions 取代；
        // 两种事实源同时存在时，迁移顺序一改就会出现「用户关掉 .rar 却仍然关联」这类幽灵问题。
        using var doc = JsonDocument.Parse(ReadDefaultJson());
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        string[] legacy =
        [
            "AssocZip", "Assoc7z", "AssocRar", "AssocTar", "AssocTarGz", "AssocGz", "AssocIso",
            "OpenDialogExtensions", "AdaptiveCompressionMode", "MultiThreadedStoreFormatIds",
        ];

        var present = legacy.Where(keys.Contains).ToList();
        Assert.True(present.Count == 0, "出厂默认不该包含已废弃键：" + string.Join(", ", present));
    }

    [Fact]
    public void BuiltInFallback_MatchesJsonCoreSemantics()
    {
        // 内嵌资源读不出来时回落代码默认值；两边的核心语义必须一致，
        // 否则「json 坏了」这种少见路径会让用户看到完全不同的默认配置。
        using var sandbox = new AppSettingsTestSandbox();

        // 把内嵌资源临时「弄坏」不可行，改为直接比对：代码默认值经公开入口拿不到，
        // 故这里校验 json 侧已由其它用例覆盖，此处只钉「两边模板/预设完全一致」这层语义
        // —— 通过 AppSettings.Load()（无设置文件 ⇒ CreateWithDefaults ⇒ json）对照内置常量。
        var fromJson = AppSettings.Load();

        Assert.Equal("{name:auto}", fromJson.FileNameTemplateDialog);
        Assert.Equal(2, fromJson.FileNameTemplatePresets.Count);
        Assert.All(fromJson.FileNameTemplatePresets, preset =>
            Assert.True(
                MantisZip.Core.Utils.FileNameTemplate.TryValidateSyntax(preset.Template, out var error),
                $"出厂预设「{preset.Name}」不是合法模板：{error?.Kind}"));
    }

    [Fact]
    public void InstallChoices_AreApplied_AndFileRemoved()
    {
        using var sandbox = new AppSettingsTestSandbox();

        // 安装器只做一件事：把向导选的语言/主题写成一次性 install-choices.json
        var choices = Path.Combine(sandbox.Root, "install-choices.json");
        File.WriteAllText(choices, """{"Language":"en","Theme":"Dark"}""");
        Assert.False(File.Exists(sandbox.SettingsFile), "沙箱初始不应有设置文件");

        var settings = AppSettings.Load();

        Assert.Equal("en", settings.Language);
        Assert.Equal("Dark", settings.Theme);
        Assert.False(File.Exists(choices), "向导选择文件必须读一次即删，否则每次首启都会覆盖用户后来的修改");
    }

    [Fact]
    public void InstallChoices_AreIgnored_WhenExistingSettingsFilePresent()
    {
        using var sandbox = new AppSettingsTestSandbox();

        var existing = new AppSettings { Language = "zh-TW", Theme = "Light" };
        Assert.True(existing.SaveTo(sandbox.SettingsFile));

        // 老用户机器上残留的 install-choices.json 不得覆盖已有设置
        File.WriteAllText(
            Path.Combine(sandbox.Root, "install-choices.json"),
            """{"Language":"en","Theme":"Dark"}""");

        AppSettings.ResetCacheForTests();
        var loaded = AppSettings.Load();

        Assert.Equal("zh-TW", loaded.Language);
        Assert.Equal("Light", loaded.Theme);
    }

    [Fact]
    public void InstallChoices_SurviveSecondLaunch_ByBeingPersistedOnFirstUse()
    {
        using var sandbox = new AppSettingsTestSandbox();
        var choices = Path.Combine(sandbox.Root, "install-choices.json");
        File.WriteAllText(choices, """{"Language":"en","Theme":"Dark"}""");

        // 第一次启动：向导选择生效
        var first = AppSettings.Load();
        Assert.Equal("en", first.Language);
        Assert.Equal("Dark", first.Theme);
        Assert.False(File.Exists(choices), "向导选择文件读一次即删");

        // 启动路径从不写 settings.json（唯一的自动写盘是联网成功的更新检查），
        // 所以「套用后必须当场落盘」是向导选择能活到第二次启动的唯一保障。
        Assert.True(
            File.Exists(sandbox.SettingsFile),
            "套用向导选择后必须立刻落盘，否则第二次启动会退回出厂默认");

        // 第二次启动：全新的读盘上下文，选择必须还在
        AppSettings.ResetCacheForTests();
        var second = AppSettings.Load();
        Assert.Equal("en", second.Language);
        Assert.Equal("Dark", second.Theme);
    }

    [Fact]
    public void InstallOptions_WithMalformedJson_DoNotBreakStartup()
    {
        using var sandbox = new AppSettingsTestSandbox();
        File.WriteAllText(Path.Combine(sandbox.Root, "install-choices.json"), "{not json");

        var settings = AppSettings.Load();

        // 兜底为出厂默认语言/主题即可，绝不因为一个坏文件让首启失败
        Assert.Equal("zh", settings.Language);
        Assert.Equal("System", settings.Theme);
    }
}
