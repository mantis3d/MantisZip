using System;
using System.IO;
using System.Linq;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 压缩设置对话框里的「文件名模板」：默认值装载、三模式行为、校验拦截。
/// 模板留空必须与引入前逐字节一致，这些用例是那条不变量的回归网。
/// </summary>
public class CompressSettingsViewModelTemplateTests : IDisposable
{
    private readonly string _root;
    private readonly string _dialog;
    private readonly string _quick;
    private readonly string _separate;
    private readonly FileNameTemplatePreset[] _presets;

    public CompressSettingsViewModelTemplateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mz-tpl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var settings = AppSettings.Load();
        _dialog = settings.FileNameTemplateDialog;
        _quick = settings.FileNameTemplateQuick;
        _separate = settings.FileNameTemplateSeparate;
        _presets = settings.FileNameTemplatePresets.ToArray();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 清理失败不影响断言 */ }

        // 设置是进程级缓存 + 落盘，测试必须还原，否则污染其它用例
        var settings = AppSettings.Load();
        settings.FileNameTemplateDialog = _dialog;
        settings.FileNameTemplateQuick = _quick;
        settings.FileNameTemplateSeparate = _separate;
        settings.FileNameTemplatePresets.Clear();
        settings.FileNameTemplatePresets.AddRange(_presets);
        settings.Save();
        AppSettings.ResetCacheForTests();
        GC.SuppressFinalize(this);
    }

    private static void Apply(Action<AppSettings> mutate)
    {
        var settings = AppSettings.Load();
        mutate(settings);
        settings.Save();
        AppSettings.ResetCacheForTests();
    }

    private string MakeFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Constructor_LoadsDialogTemplateFromSettings()
    {
        Apply(s => s.FileNameTemplateDialog = "{name}_{date}");

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });

        Assert.Equal("{name}_{date}", vm.FileNameTemplate);
        Assert.True(vm.IsTemplateActive);
    }

    [Fact]
    public void EmptyTemplate_KeepsLegacyNamingAndEditableFileName()
    {
        Apply(s => s.FileNameTemplateDialog = "");

        var vm = new CompressSettingsViewModel(new[] { MakeFile("report.txt") });

        Assert.False(vm.IsTemplateActive);
        Assert.Equal(CompressOutputMode.Manual, vm.OutputMode);
        Assert.False(vm.IsOutputFileNameReadOnly);
    }

    [Fact]
    public void TemplateInManualMode_MakesFileNameReadOnlyAndRendersIntoIt()
    {
        Apply(s => s.FileNameTemplateDialog = "");

        var vm = new CompressSettingsViewModel(new[] { MakeFile("report.txt") });
        vm.FileNameTemplate = "{name}_backup";

        Assert.True(vm.IsTemplateActive);
        Assert.True(vm.IsOutputFileNameReadOnly);
        Assert.Equal("report_backup", vm.OutputFileName);
    }

    [Fact]
    public void UnknownPlaceholder_IsRejected()
    {
        Apply(s => s.FileNameTemplateDialog = "");

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });
        vm.FileNameTemplate = "{foo}";

        Assert.NotNull(vm.TemplateError);
        Assert.True(vm.IsTemplateExampleError);
    }

    [Fact]
    public void ValidTemplate_HasNoError()
    {
        Apply(s => s.FileNameTemplateDialog = "");

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });
        vm.FileNameTemplate = "{name}_{date}";

        Assert.Null(vm.TemplateError);
        Assert.False(vm.IsTemplateExampleError);
    }

    [Fact]
    public async Task UnknownPlaceholder_BlocksConfirm()
    {
        Apply(s => s.FileNameTemplateDialog = "");

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });
        vm.FileNameTemplate = "{foo}";

        var outcome = await vm.TryConfirmAsync();

        var rejected = Assert.IsType<Rejected<CompressSettings>>(outcome);
        Assert.Contains("foo", rejected.Reason);
    }

    [Fact]
    public void SeparateMode_TemplateWithoutName_IsRejectedAsDuplicate()
    {
        Apply(s => s.FileNameTemplateDialog = "");

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt"), MakeFile("b.txt") });
        vm.OutputMode = CompressOutputMode.Separate;

        // 两个源都渲染成同一个固定名 ⇒ 必须被拦下（不能静默覆盖）
        vm.FileNameTemplate = "same_name";

        Assert.NotNull(vm.TemplateError);
    }

    [Fact]
    public void SeparateMode_TemplateWithSeq_ProducesDistinctNames()
    {
        Apply(s => s.FileNameTemplateDialog = "");

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt"), MakeFile("b.txt") });
        vm.OutputMode = CompressOutputMode.Separate;
        vm.FileNameTemplate = "{name}_{seq:0000}";

        Assert.Null(vm.TemplateError);
    }

    [Fact]
    public async Task ConfirmedSettings_CarryTemplateOnlyWhenActive()
    {
        Apply(s => s.FileNameTemplateDialog = "");
        var file = MakeFile("a.txt");

        var without = new CompressSettingsViewModel(new[] { file });
        var first = await without.TryConfirmAsync();
        Assert.Null(Assert.IsType<Confirmed<CompressSettings>>(first).Value.FileNameTemplate);

        var with = new CompressSettingsViewModel(new[] { file });
        with.FileNameTemplate = "{name}";
        var second = await with.TryConfirmAsync();
        Assert.Equal("{name}", Assert.IsType<Confirmed<CompressSettings>>(second).Value.FileNameTemplate);
    }

    [Fact]
    public void PresetSelection_FillsTemplateAndClearsSelection()
    {
        Apply(s =>
        {
            s.FileNameTemplateDialog = "";
            s.FileNameTemplatePresets.Clear();
            s.FileNameTemplatePresets.Add(new FileNameTemplatePreset("日期", "{name}_{date}"));
        });

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });
        Assert.Single(vm.TemplatePresetOptions);

        vm.SelectedTemplatePreset = vm.TemplatePresetOptions[0];

        Assert.Equal("{name}_{date}", vm.FileNameTemplate);
        Assert.Null(vm.SelectedTemplatePreset);
        Assert.True(vm.HasTemplatePresets);
    }

    [Fact]
    public void SavePreset_OverLimit_ReportsFriendlyError()
    {
        Apply(s =>
        {
            s.FileNameTemplateDialog = "";
            s.FileNameTemplatePresets.Clear();
            for (var i = 0; i < AppSettings.MaxFileNameTemplatePresets; i++)
                s.FileNameTemplatePresets.Add(new FileNameTemplatePreset("p" + i, "{name}_" + i));
        });

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });
        vm.FileNameTemplate = "{name}_new";
        vm.NewPresetName = "新预设";

        vm.SaveTemplatePresetCommand.Execute(null);

        Assert.NotNull(vm.TemplateError);
        Assert.Equal(AppSettings.MaxFileNameTemplatePresets, vm.TemplatePresetOptions.Count);
    }
}
