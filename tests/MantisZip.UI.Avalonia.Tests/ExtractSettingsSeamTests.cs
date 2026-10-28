using System;
using System.Linq;
using System.Reflection;
using MantisZip.Core.Services;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 解压设置窗口「意图-执行接缝」守卫（AGENTS.md 规则 20）。
/// <para>
/// 旧结构是三轨拼装：<c>ShowDialog&lt;bool&gt;</c> 表示「点了确认」，真参数分散在
/// ① VM 上由调用方 <c>CopyExecutionSettingsFrom</c> 逐个手抄、② 窗口 <c>GetFilteredEntryKeys()</c> 补读过滤集合、
/// ③ 窗口 <c>DialogResult</c> 表示结局——漏抄一项就是「界面正常、点下去没反应」。
/// </para>
/// <para>
/// 接缝改造后设置窗口只产出 <c>ConfirmOutcome&lt;ExtractSettings&gt;</c>：本测试因此从
/// 「拷贝清单是否完整」反转为「搬运通道必须不存在」+「确认载荷必须自带全部执行设置」。
/// </para>
/// </summary>
public class ExtractSettingsSeamTests
{
    private static readonly string[] SeedArchives = { "mz-seam-extract-seed.zip" };

    /// <summary>守卫①：给「关窗 + 补读」留后门的成员必须全部消失。</summary>
    [Fact]
    public void RetiredSeamChannels_AreGone()
    {
        const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

        var vmType = typeof(ExtractSettingsViewModel);
        Assert.DoesNotContain(
            vmType.GetMethods(PublicInstance | BindingFlags.Static),
            m => m.Name == "CopyExecutionSettingsFrom");
        Assert.DoesNotContain(
            vmType.GetProperties(PublicInstance),
            p => p.Name is "CloseAction" or "FilteredEntryKeys");
        // 过滤集合只能在确认事务内部计算，不允许被外部当作「补读」入口调用
        Assert.DoesNotContain(
            vmType.GetMethods(PublicInstance),
            m => m.Name is "ComputeFilteredEntryKeys" or "GetFilteredEntryKeys");

        var windowType = typeof(ExtractSettingsWindow);
        Assert.DoesNotContain(
            windowType.GetProperties(PublicInstance),
            p => p.Name == "DialogResult");
        Assert.DoesNotContain(
            windowType.GetMethods(PublicInstance),
            m => m.Name is "GetFilter" or "GetFilteredEntryKeys");

        // 守卫②：唯一出口存在——确认事务事件 + 非模态完成信号 + 执行入口
        Assert.Contains("ConfirmCompleted", vmType.GetEvents(PublicInstance).Select(e => e.Name));
        Assert.Contains("Completion", windowType.GetProperties(PublicInstance).Select(p => p.Name));
        Assert.Contains("RunConfirmedAsync", windowType.GetMethods(PublicInstance).Select(m => m.Name));
    }

    /// <summary>
    /// 守卫③：确认载荷自带全部执行侧设置（目标目录 / 冲突策略 / 两个后置开关 / 并行快照 / 密码 / 路径契约）。
    /// 这些值过去靠调用方在关窗后逐个手抄，抄漏不报错；现在抄漏会在这里失败。
    /// </summary>
    [Fact]
    public async Task ConfirmTransaction_CarriesEveryExecutionSetting()
    {
        var vm = new ExtractSettingsViewModel(SeedArchives)
        {
            DestinationPath = @"C:\mz-seam-extract-probe",
            ConflictAction = "rename",
            OpenFolderAfterExtract = true,
            DeleteArchiveAfterExtract = true,
            EnableParallelExtract = true,
            SmartParallelDegree = false,
            SmartSsdDegree = 9,
            SmartHddDegree = 7,
            ManualParallelDegree = 5,
            LargeArchivePreTest = false,
        };
        vm.SetKnownPassword("seam-pw");

        var confirmed = Assert.IsType<Confirmed<ExtractSettings>>(
            await vm.TryConfirmAsync(TestContext.Current.CancellationToken));
        var settings = confirmed.Value;

        Assert.Equal(new[] { SeedArchives[0] }, settings.ArchivePaths);
        Assert.Equal(@"C:\mz-seam-extract-probe", settings.DestinationPath);
        Assert.Equal("rename", settings.ConflictAction);
        Assert.True(settings.OpenFolderAfterExtract);
        Assert.True(settings.DeleteArchiveAfterExtract);
        Assert.Equal("seam-pw", settings.Password);
        // 路径契约：保留完整路径取自全局设置（解压会话不另读设置，避免两处口径分叉）
        Assert.Equal(AppSettings.Load().ExtractPreserveFullPath, settings.PreserveFullPath);

        var parallel = Assert.IsType<ParallelExtractSettings>(settings.ParallelOverride);
        Assert.True(parallel.EnableParallel);
        Assert.False(parallel.SmartParallel);
        Assert.Equal(9, parallel.SmartSsdDegree);
        Assert.Equal(7, parallel.SmartHddDegree);
        Assert.Equal(5, parallel.ManualDegree);
        Assert.False(parallel.LargeArchivePreTest);

        // 未启用过滤时不过滤整包：FilteredEntryKeys 为 null = 整包，空集合才是「有意零匹配」
        Assert.Null(settings.FilteredEntryKeys);
        Assert.Null(settings.SelectedEntries);
    }

    /// <summary>
    /// 守卫④：窗口的唯一确认结局通道是 <c>ConfirmOutcome&lt;T&gt;</c>——三个具体类型必须都存在，
    /// 否则「拒绝留窗 / 确认关窗 / 取消静默」三分支无从表达。
    /// </summary>
    [Fact]
    public void ConfirmOutcome_HasAllThreeOutcomes()
    {
        var outcome = new Confirmed<ExtractSettings>(new ExtractSettings
        {
            ArchivePaths = Array.Empty<string>(),
            DestinationPath = @"C:\x",
        });
        Assert.IsType<Confirmed<ExtractSettings>>(outcome);

        var rejected = new Rejected<ExtractSettings>("原因");
        Assert.Equal("原因", rejected.Reason);

        var cancelled = new Cancelled<ExtractSettings>();
        Assert.IsType<Cancelled<ExtractSettings>>(cancelled);
    }
}
