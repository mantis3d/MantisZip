using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Services;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 压缩输出包冲突策略的端到端回归：压缩对话框选择 → CompressRequest.ConflictAction
/// → CompressFlow.CreateResolver 默认映射 → 服务层实际行为。
/// 回归背景：这条链此前完全没有测试覆盖，用户反馈「压缩窗口的文件冲突选项似乎不起作用」。
/// </summary>
public sealed class CompressConflictDefaultActionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mz_conflict_{Guid.NewGuid():N}");

    public CompressConflictDefaultActionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch { /* 忽略清理失败 */ }
    }

    /// <summary>造一个源文件 + 一个已存在的输出包（内容为垃圾字节，用于验证「未被触碰」）。</summary>
    private (string Source, string Output, byte[] Before) Arrange()
    {
        var source = Path.Combine(_dir, "data.txt");
        File.WriteAllText(source, "hello world");
        var output = Path.Combine(_dir, "data.zip");
        File.WriteAllBytes(output, [1, 2, 3, 4]);
        return (source, output, File.ReadAllBytes(output));
    }

    private static CompressRequest BuildManualRequest(string source, string output, string conflictAction) => new()
    {
        Mode = CompressOutputMode.Manual,
        SourcePaths = { source },
        OutputPath = output,
        Format = "zip",
        ConflictAction = conflictAction,
    };

    [Fact]
    public void DialogSelection_UpdatesConflictAction()
    {
        var source = Path.Combine(_dir, "data.txt");
        File.WriteAllText(source, "hello world");

        var vm = new CompressSettingsViewModel([source]);
        var skip = vm.ConflictActionOptions.First(o => o.Tag == "skip");
        vm.SelectedConflictActionOption = skip;

        // 下拉选中 → VM.ConflictAction（BuildRequest 直接取该值）
        Assert.Equal("skip", vm.ConflictAction);
    }

    [Fact]
    public void Plan_TargetsExistingArchive_SoConflictIsDetected()
    {
        var (source, output, _) = Arrange();
        var subtree = ResultPreviewService.BuildSourceSubtree(source);

        var (_, plan) = ResultPreviewService.AssembleCompressPreview(
            [source], [subtree], CompressOutputMode.Manual, output, "zip",
            keepOriginalExtension: true, filter: null);

        // 计划输出路径必须就是用户填的那个（已存在）路径，否则冲突检测永远不触发 → 策略「不起作用」
        Assert.Single(plan.Items);
        Assert.Equal(output, plan.Items[0].OutputArchivePath, ignoreCase: true);
    }

    [Fact]
    public async Task Skip_DoesNotTouchExistingArchive()
    {
        var (source, output, before) = Arrange();
        var request = BuildManualRequest(source, output, "skip");

        var resolver = CompressFlow.CreateResolver(
            _ => throw new InvalidOperationException("skip 策略不应弹窗"),
            request.ConflictAction);

        var result = await new AvaloniaCompressService()
            .CompressAsync(request, null, CancellationToken.None, resolver);

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(before, File.ReadAllBytes(output));
    }

    [Fact]
    public async Task Rename_KeepsExistingAndWritesNewArchive()
    {
        var (source, output, before) = Arrange();
        var request = BuildManualRequest(source, output, "rename");

        var resolver = CompressFlow.CreateResolver(
            _ => throw new InvalidOperationException("rename 策略不应弹窗"),
            request.ConflictAction);

        var result = await new AvaloniaCompressService()
            .CompressAsync(request, null, CancellationToken.None, resolver);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(before, File.ReadAllBytes(output));
        Assert.Equal(2, Directory.GetFiles(_dir, "data*.zip").Length);
    }

    [Fact]
    public async Task Overwrite_ReplacesExistingArchive()
    {
        var (source, output, before) = Arrange();
        var request = BuildManualRequest(source, output, "overwrite");

        var resolver = CompressFlow.CreateResolver(
            _ => throw new InvalidOperationException("overwrite 策略不应弹窗"),
            request.ConflictAction);

        var result = await new AvaloniaCompressService()
            .CompressAsync(request, null, CancellationToken.None, resolver);

        Assert.Equal(1, result.Succeeded);
        var after = File.ReadAllBytes(output);
        Assert.NotEqual(before, after);
        Assert.True(after.Length > 4);
    }

    /// <summary>「追加」策略：目标包可追加时应直接追加，不弹窗。</summary>
    [Fact]
    public async Task Add_AppendsToExistingArchive_WithoutPrompt()
    {
        var (source, output, _) = Arrange();
        // 先用真实引擎造一个合法的 zip，再追加
        await new AvaloniaCompressService().CompressAsync(
            BuildManualRequest(source, output, "overwrite"), null, CancellationToken.None,
            CompressFlow.CreateResolver(_ => throw new InvalidOperationException("不应弹窗"), "overwrite"));

        var extra = Path.Combine(_dir, "extra.txt");
        File.WriteAllText(extra, "second");
        var request = BuildManualRequest(extra, output, "add");

        var resolver = CompressFlow.CreateResolver(
            _ => throw new InvalidOperationException("add 策略在 zip 上不应弹窗"),
            request.ConflictAction);

        var result = await new AvaloniaCompressService()
            .CompressAsync(request, null, CancellationToken.None, resolver);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Skipped);
    }

    /// <summary>
    /// 「自动重命名」后必须回报实际写出的路径：进度窗口顶部的「压缩包列表」
    /// 用它把登录时计划里的旧包名换成真实包名（用户反馈压缩包列表仍显示重命名前的名字）。
    /// </summary>
    [Fact]
    public async Task Rename_ReportsResolvedOutputPath()
    {
        var (source, output, _) = Arrange();
        var request = BuildManualRequest(source, output, "rename");

        var reported = new System.Collections.Generic.List<(int Index, string Path)>();
        var result = await new AvaloniaCompressService().CompressAsync(
            request, null, CancellationToken.None,
            CompressFlow.CreateResolver(_ => throw new InvalidOperationException("rename 策略不应弹窗"), "rename"),
            onItemOutputPath: (index, path) => reported.Add((index, path)));

        Assert.Equal(1, result.Succeeded);
        var (index, resolved) = Assert.Single(reported);
        Assert.Equal(0, index);
        // 实际写出的包名与计划名不同，且文件确实存在于回报的路径上
        Assert.False(string.Equals(output, resolved, StringComparison.OrdinalIgnoreCase));
        Assert.False(string.Equals(Path.GetFileName(output), Path.GetFileName(resolved), StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(resolved));
    }

    /// <summary>覆盖策略下路径不变，回调仍应照发（UI 侧无需分支判断）。</summary>
    [Fact]
    public async Task Overwrite_ReportsUnchangedOutputPath()
    {
        var (source, output, _) = Arrange();
        var request = BuildManualRequest(source, output, "overwrite");

        var reported = new System.Collections.Generic.List<(int Index, string Path)>();
        await new AvaloniaCompressService().CompressAsync(
            request, null, CancellationToken.None,
            CompressFlow.CreateResolver(_ => throw new InvalidOperationException("overwrite 策略不应弹窗"), "overwrite"),
            onItemOutputPath: (index, path) => reported.Add((index, path)));

        var (index, resolved) = Assert.Single(reported);
        Assert.Equal(0, index);
        Assert.Equal(output, resolved, ignoreCase: true);
    }
}
