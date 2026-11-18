using System;
using System.IO;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 压缩设置对话框里的「根目录名模板」：活字名/模板分派、示例行、确认拦截、载荷透传。
/// 关键不变量：不含花括号的字面名仍按旧语义净化（<c>a:b</c> 不该变成红框）。
/// </summary>
public class CompressSettingsViewModelRootNameTests : IDisposable
{
    private readonly string _root;
    private readonly RootDirectoryMode _rootMode;
    private readonly string _rootName;

    public CompressSettingsViewModelRootNameTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mz-rootname-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var settings = AppSettings.Load();
        _rootMode = settings.CompressRootMode;
        _rootName = settings.CompressRootName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 清理失败不影响断言 */ }

        // 设置是进程级缓存 + 落盘，测试必须还原，否则污染其它用例
        var settings = AppSettings.Load();
        settings.CompressRootMode = _rootMode;
        settings.CompressRootName = _rootName;
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

    private string MakeDir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private string MakeFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "x");
        return path;
    }

    // ════════════════════════════════════════════════════════════════
    //  装载与「是否算模板」
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_LoadsRootModeAndNameFromSettings()
    {
        Apply(s =>
        {
            s.CompressRootMode = RootDirectoryMode.AlwaysWrap;
            s.CompressRootName = "{name}_{date}";
        });

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });

        Assert.Equal(RootDirectoryMode.AlwaysWrap, vm.RootMode);
        Assert.Equal("{name}_{date}", vm.WrapRoot);
        Assert.True(vm.IsRootNameTemplateActive);
    }

    [Fact]
    public void LiteralName_IsNotTreatedAsTemplate()
    {
        // 老配置里的非法字面名（a:b）过去是净化后照用，不能因为加了模板就变红框
        Apply(s =>
        {
            s.CompressRootMode = RootDirectoryMode.AlwaysWrap;
            s.CompressRootName = "a:b";
        });

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });

        Assert.False(vm.IsRootNameTemplateActive);
        Assert.Null(vm.RootNameError);
        Assert.False(vm.IsRootNameErrorVisible);
    }

    [Fact]
    public void TemplateIsActiveOnlyInWrapModes()
    {
        Apply(s =>
        {
            s.CompressRootMode = RootDirectoryMode.Original;
            s.CompressRootName = "{name}";
        });

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });

        // 保持原样时不加根 ⇒ 根名与模板都不参与，也不该报错
        Assert.False(vm.IsRootNameTemplateActive);
        Assert.Null(vm.RootNameError);
        Assert.Equal(string.Empty, vm.RootNameExampleText);
    }

    // ════════════════════════════════════════════════════════════════
    //  示例行
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ExampleText_ShowsFinalNameForLiteralInput()
    {
        Apply(s => s.CompressRootMode = RootDirectoryMode.AlwaysWrap);

        var vm = new CompressSettingsViewModel(new[] { MakeFile("report.txt") });
        vm.WrapRoot = "myroot";

        Assert.Equal("myroot", vm.RootNameExampleText);
    }

    [Fact]
    public void ExampleText_ShowsRenderResultForTemplate()
    {
        Apply(s => s.CompressRootMode = RootDirectoryMode.AlwaysWrap);

        var vm = new CompressSettingsViewModel(new[] { MakeFile("report.txt") });
        vm.WrapRoot = "{name}_backup";

        Assert.Equal("report_backup", vm.RootNameExampleText);
        Assert.False(vm.IsRootNameExampleError);
    }

    [Fact]
    public void ExampleText_EmptyInputFallsBackToArchiveName()
    {
        Apply(s => s.CompressRootMode = RootDirectoryMode.AlwaysWrap);

        var vm = new CompressSettingsViewModel(new[] { MakeFile("report.txt") });
        vm.WrapRoot = "";

        // 留空 = 未启用模板 ⇒ 回落到「压缩包主名」。
        // 注意这里**不是**源文件名：压缩包默认按落点目录命名（单个 report.txt 落在夹具目录里，
        // 包名就是夹具目录名），根名跟着包名走才是「预览 = 实际」。
        // 2026-11-18 合并 beta 后修正断言 —— 旧断言写死 "report" 是按源文件名猜的，
        // 在「输出路径单一变量」重构后（默认包名 = 落点目录名）已经过期。
        Assert.Equal(Path.GetFileName(_root), vm.RootNameExampleText);
        // 与 VM 自己算出的包名一致：这条不变量比具体字符串更能防止两边漂移。
        Assert.Equal(vm.OutputFileName, vm.RootNameExampleText);
    }

    // ════════════════════════════════════════════════════════════════
    //  校验：模板错误可见
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UnknownPlaceholder_IsRejected()
    {
        Apply(s => s.CompressRootMode = RootDirectoryMode.AlwaysWrap);

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });
        vm.WrapRoot = "{foo}";

        Assert.NotNull(vm.RootNameError);
        Assert.True(vm.IsRootNameErrorVisible);
        Assert.True(vm.IsRootNameExampleError);
        // 错误文案里应提到那个 token，否则用户不知道该改哪一段
        Assert.Contains("foo", vm.RootNameError!);
    }

    [Fact]
    public void FileOnlyToken_IsRejectedForRootName()
    {
        Apply(s => s.CompressRootMode = RootDirectoryMode.AlwaysWrap);

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });

        // 根名没有 {ext}/{seq}：写出来必须报错，不能静默渲染成空
        vm.WrapRoot = "{ext}";
        Assert.NotNull(vm.RootNameError);

        vm.WrapRoot = "{seq}";
        Assert.NotNull(vm.RootNameError);
    }

    [Fact]
    public void ValidTemplate_HasNoError()
    {
        Apply(s => s.CompressRootMode = RootDirectoryMode.AlwaysWrap);

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });
        vm.WrapRoot = "{name}_{date}";

        Assert.Null(vm.RootNameError);
        Assert.False(vm.IsRootNameErrorVisible);
    }

    [Fact]
    public void SwitchingAwayFromWrapMode_ClearsStaleError()
    {
        Apply(s => s.CompressRootMode = RootDirectoryMode.AlwaysWrap);

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });
        vm.WrapRoot = "{foo}";
        Assert.NotNull(vm.RootNameError);

        vm.RootMode = RootDirectoryMode.Original;

        // 切回「保持原样」后不该留一条无主红字
        Assert.Null(vm.RootNameError);
        Assert.False(vm.IsRootNameErrorVisible);
    }

    // ════════════════════════════════════════════════════════════════
    //  确认拦截 + 载荷透传
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnknownPlaceholder_BlocksConfirm()
    {
        Apply(s => s.CompressRootMode = RootDirectoryMode.AlwaysWrap);

        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });
        vm.WrapRoot = "{foo}";

        var outcome = await vm.TryConfirmAsync();

        var rejected = Assert.IsType<Rejected<CompressSettings>>(outcome);
        Assert.Contains("foo", rejected.Reason);
    }

    [Fact]
    public async Task ConfirmedSettings_CarryRootNameOnlyInWrapModes()
    {
        Apply(s => s.CompressRootMode = RootDirectoryMode.AlwaysWrap);
        var file = MakeFile("a.txt");

        var wrapped = new CompressSettingsViewModel(new[] { file });
        wrapped.WrapRoot = "myroot";
        var first = await wrapped.TryConfirmAsync();
        var settings = Assert.IsType<Confirmed<CompressSettings>>(first).Value;
        Assert.Equal(RootDirectoryMode.AlwaysWrap, settings.RootMode);
        Assert.Equal("myroot", settings.WrapRoot);

        var plain = new CompressSettingsViewModel(new[] { file });
        plain.RootMode = RootDirectoryMode.Original;
        var second = await plain.TryConfirmAsync();
        // 不加根时根名不该被带进载荷（否则执行侧会以为要加根）
        Assert.Null(Assert.IsType<Confirmed<CompressSettings>>(second).Value.WrapRoot);
    }
}
