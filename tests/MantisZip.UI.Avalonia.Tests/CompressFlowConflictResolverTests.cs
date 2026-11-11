using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 压缩侧冲突 resolver 的实时策略层（v0.5.7）：
/// ①进度窗口下拉（livePolicy）优先于本次请求冻结的策略；
/// ②非 ask 时免弹窗直接映射；
/// ③弹窗勾「应用到全部」后把实际生效的动作回写下拉（syncPolicy）。
/// </summary>
public class CompressFlowConflictResolverTests
{
    private static CompressConflictInfo Info(bool canAdd = true) =>
        new(OutputPath: @"C:\out\pkg.zip", CanAdd: canAdd, SuggestedName: "pkg (2).zip");

    [Fact]
    public async Task LivePolicy_OverwritesRequestDefault_WithoutDialog()
    {
        var dialogCalls = 0;
        var resolver = CompressFlow.CreateResolver(
            _ => { dialogCalls++; return Task.FromResult((CompressConflictAction.Rename, (string?)"x", false)); },
            defaultAction: "skip",
            livePolicy: () => "overwrite");

        var result = await resolver(Info());

        Assert.Equal(CompressConflictAction.Overwrite, result.Action);
        Assert.Equal(0, dialogCalls);
    }

    [Fact]
    public async Task LivePolicy_Ask_FallsThroughToDialog()
    {
        var dialogCalls = 0;
        var resolver = CompressFlow.CreateResolver(
            _ =>
            {
                dialogCalls++;
                return Task.FromResult((CompressConflictAction.Rename, (string?)"custom.zip", false));
            },
            defaultAction: "overwrite",
            livePolicy: () => "ask");

        var result = await resolver(Info());

        Assert.Equal(CompressConflictAction.Rename, result.Action);
        Assert.Equal("custom.zip", result.CustomName);
        Assert.Equal(1, dialogCalls);
    }

    [Fact]
    public async Task NoLivePolicy_FallsBackToRequestDefault()
    {
        var resolver = CompressFlow.CreateResolver(
            _ => throw new InvalidOperationException("不应弹窗"),
            defaultAction: "skip");

        var result = await resolver(Info());

        // Core 没有 Skip 动作，压缩侧的「跳过」映射成 Cancel（单项跳过）
        Assert.Equal(CompressConflictAction.Cancel, result.Action);
    }

    [Fact]
    public async Task ApplyToAll_SyncsPolicyDropdown_AndMemorizes()
    {
        var dialogCalls = 0;
        var synced = new List<string>();
        var resolver = CompressFlow.CreateResolver(
            _ =>
            {
                dialogCalls++;
                return Task.FromResult((CompressConflictAction.Overwrite, (string?)null, true));
            },
            defaultAction: "ask",
            livePolicy: () => "ask",
            syncPolicy: code => synced.Add(code));

        var first = await resolver(Info());
        var second = await resolver(Info());

        Assert.Equal(CompressConflictAction.Overwrite, first.Action);
        Assert.Equal(CompressConflictAction.Overwrite, second.Action);
        Assert.Equal(1, dialogCalls); // 已记忆，不再弹窗
        Assert.Equal(new[] { "overwrite" }, synced);
    }

    [Fact]
    public async Task LivePolicy_Add_WithoutPackageSupport_FallsBackToDialog()
    {
        var dialogCalls = 0;
        var resolver = CompressFlow.CreateResolver(
            _ =>
            {
                dialogCalls++;
                return Task.FromResult((CompressConflictAction.Rename, (string?)null, false));
            },
            defaultAction: "overwrite",
            livePolicy: () => "add");

        // tar.gz 之类不支持追加：live "add" 映射不可用 → 回退弹窗
        var result = await resolver(Info(canAdd: false));

        Assert.Equal(CompressConflictAction.Rename, result.Action);
        Assert.Equal(1, dialogCalls);
    }

    [Theory]
    [InlineData("overwrite", CompressConflictAction.Overwrite)]
    [InlineData("add", CompressConflictAction.Add)]
    [InlineData("rename", CompressConflictAction.Rename)]
    [InlineData("skip", CompressConflictAction.Cancel)]
    public async Task LivePolicy_Codes_MapToCoreActions(string code, CompressConflictAction expected)
    {
        var resolver = CompressFlow.CreateResolver(
            _ => throw new InvalidOperationException("不应弹窗"),
            livePolicy: () => code);

        var result = await resolver(Info());

        Assert.Equal(expected, result.Action);
    }
}
