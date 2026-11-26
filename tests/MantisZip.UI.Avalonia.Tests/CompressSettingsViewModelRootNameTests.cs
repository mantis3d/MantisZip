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
/// 设置读写走 <see cref="AppSettingsTestSandbox"/>：用例跑在自己的临时目录里。
/// </summary>
/// <remarks>
/// 2026-11-23 换成沙箱：此前的「读真实 settings.json → 改 → finally 还原」写法会让断言依赖
/// 本机配置 —— 出厂默认把 <c>FileNameTemplateDialog</c> 设成 <c>{name:auto}</c> 时，
/// 压缩包默认主名来自模板渲染（<c>report</c>），没有模板时才回落到落点目录名，
/// 于是「根名留空回落包名」这条断言在同一台机器上时而通过时而失败。
/// </remarks>
public class CompressSettingsViewModelRootNameTests : IDisposable
{
    private readonly AppSettingsTestSandbox _sandbox;
    private readonly string _root;

    public CompressSettingsViewModelRootNameTests()
    {
        _sandbox = new AppSettingsTestSandbox();
        _root = _sandbox.Root;
    }

    public void Dispose()
    {
        _sandbox.Dispose();
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
    public void ExampleText_EmptyInputMatchesArchiveName()
    {
        Apply(s => s.CompressRootMode = RootDirectoryMode.AlwaysWrap);

        var vm = new CompressSettingsViewModel(new[] { MakeFile("report.txt") });
        vm.WrapRoot = "";

        // 留空 = 未启用模板 ⇒ 回落到「压缩包主名」，而包名由文件名模板（出厂默认 {name:auto}）
        // 或落点目录名决定 —— 根名只要**等于 VM 自己算出的包名**，「预览 = 实际」就成立。
        // 这条不变量比写死具体字符串更抗两侧重构：2026-11-18 与 11-23 两次改动都只动了包名来源。
        Assert.Equal(vm.OutputFileName, vm.RootNameExampleText);
        Assert.False(vm.IsRootNameExampleError);
    }

    [Fact]
    public void ExampleText_EmptyInputWithNoFileNameTemplate_FallsBackToOutputDirName()
    {
        // 文件名模板留空时包名来自落点目录名（沙箱目录），根名跟着它走
        Apply(s =>
        {
            s.CompressRootMode = RootDirectoryMode.AlwaysWrap;
            s.FileNameTemplateDialog = "";
        });

        var vm = new CompressSettingsViewModel(new[] { MakeFile("report.txt") });
        vm.WrapRoot = "";

        Assert.Equal(Path.GetFileName(_root), vm.RootNameExampleText);
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
