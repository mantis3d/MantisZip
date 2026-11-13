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
        Assert.True(vm.IsTemplateErrorVisible);
    }

    [Fact]
    public void ValidTemplate_HasNoError()
    {
        Apply(s => s.FileNameTemplateDialog = "");

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });
        vm.FileNameTemplate = "{name}_{date}";

        Assert.Null(vm.TemplateError);
        Assert.False(vm.IsTemplateErrorVisible);
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
    public void PresetSelection_FillsTemplateAndKeepsSelection()
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
        // 保留选中态，否则「删除」按钮拿不到目标、点了没反应（2026-11-12 修）
        Assert.Same(vm.TemplatePresetOptions[0], vm.SelectedTemplatePreset);
        Assert.True(vm.HasTemplatePresets);
    }

    /// <summary>手动改模板改到与选中预设不一致时，下拉里的选中态要自动取消。</summary>
    [Fact]
    public void EditingTemplateAwayFromPreset_ClearsSelection()
    {
        Apply(s =>
        {
            s.FileNameTemplateDialog = "";
            s.FileNameTemplatePresets.Clear();
            s.FileNameTemplatePresets.Add(new FileNameTemplatePreset("日期", "{name}_{date}"));
        });

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });
        vm.SelectedTemplatePreset = vm.TemplatePresetOptions[0];
        Assert.NotNull(vm.SelectedTemplatePreset);

        vm.FileNameTemplate = "{name}_custom";

        Assert.Null(vm.SelectedTemplatePreset);
    }

    /// <summary>删除按钮此前无效：选中预设后立刻被置 null，命令里拿不到目标。现在要真能删掉。</summary>
    [Fact]
    public void DeletePreset_RemovesSelectedPresetFromSettings()
    {
        Apply(s =>
        {
            s.FileNameTemplateDialog = "";
            s.FileNameTemplatePresets.Clear();
            s.FileNameTemplatePresets.Add(new FileNameTemplatePreset("日期", "{name}_{date}"));
            s.FileNameTemplatePresets.Add(new FileNameTemplatePreset("序号", "{name}_{seq:0000}"));
        });

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });
        Assert.Equal(2, vm.TemplatePresetOptions.Count);

        vm.SelectedTemplatePreset = vm.TemplatePresetOptions[0];
        vm.DeleteTemplatePresetCommand.Execute(null);

        Assert.Single(vm.TemplatePresetOptions);
        Assert.Equal("序号", vm.TemplatePresetOptions[0].Name);
        Assert.DoesNotContain(AppSettings.Load().FileNameTemplatePresets, p => p.Name == "日期");
    }

    /// <summary>
    /// 源列表变化必须重渲染模板：1 个源时 <c>{name:auto}</c> = 该源主名，
    /// 加到 2 个源变成父目录名。早先只在 Manual 且路径为空时才渲染，加文件后数字不更新。
    /// </summary>
    [Fact]
    public void AddingSource_ReRendersNameAuto()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mz_tpl_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var first = System.IO.Path.Combine(dir, "alpha.txt");
            var second = System.IO.Path.Combine(dir, "beta.txt");
            System.IO.File.WriteAllText(first, "a");
            System.IO.File.WriteAllText(second, "b");

            Apply(s => s.FileNameTemplateDialog = "");

            var vm = new CompressSettingsViewModel(new[] { first });
            vm.FileNameTemplate = "{name:auto}";

            // 单源：{name:auto} 回退源主名
            Assert.Equal("alpha", vm.OutputFileName);

            vm.SelectedPaths.Add(second);

            // 双源：{name:auto} 变成公共父目录名
            Assert.Equal(System.IO.Path.GetFileName(dir), vm.OutputFileName);
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
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

    /// <summary>
    /// 缺陷 G 的回归网：模板启用后改输出目录，<see cref="CompressSettingsViewModel.OutputPath"/>
    /// 必须跟着换目录（旧实现只重渲染文件名、不重算路径，预览树与实际产物都停在旧目录）。
    /// </summary>
    [Fact]
    public void TemplateActive_ChangingOutputDirectory_UpdatesOutputPathAndKeepsTemplateName()
    {
        Apply(s => s.FileNameTemplateDialog = "");

        var dirA = Path.Combine(_root, "dirA");
        var dirB = Path.Combine(_root, "dirB");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        var source = Path.Combine(dirA, "report.txt");
        File.WriteAllText(source, "x");

        var vm = new CompressSettingsViewModel(new[] { source });
        vm.FileNameTemplate = "{name}_bak";

        Assert.Equal("report_bak", vm.OutputFileName);
        Assert.Equal(Path.Combine(dirA, "report_bak.zip"), vm.OutputPath);

        vm.OutputDirectory = dirB;

        // 目录变了 → 路径必须跟着变（名字仍由模板决定）
        Assert.Equal("report_bak", vm.OutputFileName);
        Assert.Equal(Path.Combine(dirB, "report_bak.zip"), vm.OutputPath);
    }

    /// <summary>
    /// 缺陷 G 的第二种表现：未启用模板时改目录，路径也要跟随（旧实现靠 ComposeManualOutputPath
    /// 写 OutputPath，重构后由同一入口算出，这里钉住不许回退）。
    /// </summary>
    [Fact]
    public void ManualMode_ChangingOutputDirectory_UpdatesOutputPath()
    {
        Apply(s => s.FileNameTemplateDialog = "");

        var dirA = Path.Combine(_root, "dirA");
        var dirB = Path.Combine(_root, "dirB");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        File.WriteAllText(Path.Combine(dirA, "report.txt"), "x");

        var vm = new CompressSettingsViewModel(new[] { Path.Combine(dirA, "report.txt") });
        vm.OutputFileName = "mine";
        Assert.Equal(Path.Combine(dirA, "mine.zip"), vm.OutputPath);

        vm.OutputDirectory = dirB;
        Assert.Equal(Path.Combine(dirB, "mine.zip"), vm.OutputPath);
    }

    /// <summary>
    /// 分别压缩的重名校验按**完整路径**判重，不是按文件名：
    /// A/report.txt 与 B/report.txt 各自落在自己的目录，两个 report.zip 并存是合法的。
    /// </summary>
    [Fact]
    public void SeparateMode_TemplateRenderSameNameInDifferentDirectories_IsNotDuplicate()
    {
        Apply(s => s.FileNameTemplateDialog = "");

        var dirA = Path.Combine(_root, "dirA");
        var dirB = Path.Combine(_root, "dirB");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        File.WriteAllText(Path.Combine(dirA, "report.txt"), "x");
        File.WriteAllText(Path.Combine(dirB, "report.txt"), "y");

        var vm = new CompressSettingsViewModel(new[]
        {
            Path.Combine(dirA, "report.txt"),
            Path.Combine(dirB, "report.txt"),
        });
        vm.OutputMode = CompressOutputMode.Separate;
        vm.FileNameTemplate = "{name}";

        Assert.Null(vm.TemplateError);
    }

    /// <summary>
    /// 分别压缩且两个源在**同一目录**、模板又不带区分度 ⇒ 仍必须拦下（不能静默覆盖）。
    /// </summary>
    [Fact]
    public void SeparateMode_TemplateSameNameInSameDirectory_IsStillDuplicate()
    {
        Apply(s => s.FileNameTemplateDialog = "");

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt"), MakeFile("b.txt") });
        vm.OutputMode = CompressOutputMode.Separate;
        vm.FileNameTemplate = "{parent}";

        Assert.NotNull(vm.TemplateError);
    }

    /// <summary>
    /// 合并模式下模板渲染的主名必须进输出路径（旧实现里 Combined 沿用了 Manual 早前填的路径，
    /// 模板名显示在文件名框里却进不了实际产物名）。
    /// </summary>
    [Fact]
    public void CombinedMode_TemplateName_IsUsedInOutputPath()
    {
        Apply(s => s.FileNameTemplateDialog = "");

        var dir = Path.Combine(_root, "proj");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "x");
        File.WriteAllText(Path.Combine(dir, "b.txt"), "y");

        var vm = new CompressSettingsViewModel(new[]
        {
            Path.Combine(dir, "a.txt"),
            Path.Combine(dir, "b.txt"),
        });
        vm.OutputMode = CompressOutputMode.Combined;
        vm.FileNameTemplate = "{name:auto}_bak";

        var folderName = Path.GetFileName(dir);
        Assert.Equal(folderName + "_bak", vm.OutputFileName);
        Assert.Equal(Path.Combine(dir, folderName + "_bak.zip"), vm.OutputPath);
    }

    /// <summary>分别压缩没有单一路径：<see cref="CompressSettingsViewModel.OutputPath"/> 为 null（不再塞提示串）。</summary>
    [Fact]
    public void SeparateMode_OutputPath_IsNull()
    {
        Apply(s => s.FileNameTemplateDialog = "");

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });
        vm.OutputMode = CompressOutputMode.Separate;

        Assert.Null(vm.OutputPath);
    }
}
