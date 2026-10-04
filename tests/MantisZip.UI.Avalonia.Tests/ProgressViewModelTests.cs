using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

public class ProgressViewModelTests
{
    [Fact]
    public void Constructor_Defaults()
    {
        var vm = new ProgressViewModel();
        Assert.Equal(0, vm.PercentComplete);
        Assert.Equal(0, vm.FilePercentComplete);
        Assert.Null(vm.FileName);
        Assert.Null(vm.StatusMessage);
        Assert.False(vm.IsIndeterminate);
    }

    [Fact]
    public void SetProgress_UpdatesProperties()
    {
        var vm = new ProgressViewModel();
        var progress = new ArchiveProgress
        {
            PercentComplete = 50,
            FilePercentComplete = 75.0,
            CurrentFile = "file.txt",
        };

        vm.SetProgress(progress);

        Assert.Equal(50, vm.PercentComplete);
        Assert.Equal(75, vm.FilePercentComplete);
        Assert.Equal("file.txt", vm.FileName);
        Assert.False(vm.IsIndeterminate);
    }

    [Fact]
    public void SetProgress_NullFilePercent_SetsToZero()
    {
        var vm = new ProgressViewModel();
        var progress = new ArchiveProgress
        {
            PercentComplete = 30,
            FilePercentComplete = null,
            CurrentFile = "file.txt",
        };

        vm.SetProgress(progress);

        Assert.Equal(30, vm.PercentComplete);
        Assert.Equal(0, vm.FilePercentComplete);
        Assert.Equal("file.txt", vm.FileName);
        Assert.False(vm.IsIndeterminate);
    }

    [Fact]
    public void SetProgress_SetsFileName()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { CurrentFile = "test.bin" });
        Assert.Equal("test.bin", vm.FileName);
    }

    [Fact]
    public void StatusMessage_DirectSet()
    {
        var vm = new ProgressViewModel();
        vm.StatusMessage = "Working...";
        Assert.Equal("Working...", vm.StatusMessage);
    }

    [Fact]
    public void CancelCommand_TriggersCancellation()
    {
        var vm = new ProgressViewModel();
        vm.InitCancellation();
        Assert.False(vm.CancellationToken.IsCancellationRequested);

        vm.CancelCommand.Execute(null);

        Assert.True(vm.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void CancelCommand_WithoutInit_DoesNotThrow()
    {
        var vm = new ProgressViewModel();
        var exception = Record.Exception(() => vm.CancelCommand.Execute(null));
        Assert.Null(exception);
    }

    [Fact]
    public void PercentComplete_ClampsTo100()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { PercentComplete = 150 });
        Assert.Equal(100, vm.PercentComplete);
    }

    [Fact]
    public void PercentComplete_Negative_ClampsTo0()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { PercentComplete = -10 });
        Assert.Equal(0, vm.PercentComplete);
    }

    // ════════════════════════════════════════════
    //  T11: 顶部内容模式 / 信息量分级 / 并行度显隐
    // ════════════════════════════════════════════

    [Fact]
    public void ContentMode_ExposesThreeModes()
    {
        var vm = new ProgressViewModel();

        // 默认 Simple（构造后无需赋值即生效）
        Assert.Equal(ProgressContentMode.Simple, vm.ContentMode);
        Assert.True(vm.IsSimpleMode);
        Assert.False(vm.IsDetailedMode);
        Assert.False(vm.IsListMode);

        // 收集派生属性变更通知（集中通知模式：一次赋值应同时通知三个派生属性）
        var notified = new List<string>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName ?? string.Empty);

        // Detailed：三者恰好一个为 true
        vm.ContentMode = ProgressContentMode.Detailed;
        Assert.Equal(ProgressContentMode.Detailed, vm.ContentMode);
        Assert.False(vm.IsSimpleMode);
        Assert.True(vm.IsDetailedMode);
        Assert.False(vm.IsListMode);
        Assert.Equal(1, new[] { vm.IsSimpleMode, vm.IsDetailedMode, vm.IsListMode }.Count(b => b));

        // List：三者恰好一个为 true
        vm.ContentMode = ProgressContentMode.List;
        Assert.Equal(ProgressContentMode.List, vm.ContentMode);
        Assert.False(vm.IsSimpleMode);
        Assert.False(vm.IsDetailedMode);
        Assert.True(vm.IsListMode);
        Assert.Equal(1, new[] { vm.IsSimpleMode, vm.IsDetailedMode, vm.IsListMode }.Count(b => b));

        // 回到 Simple
        vm.ContentMode = ProgressContentMode.Simple;
        Assert.True(vm.IsSimpleMode);
        Assert.False(vm.IsDetailedMode);
        Assert.False(vm.IsListMode);
        Assert.Equal(1, new[] { vm.IsSimpleMode, vm.IsDetailedMode, vm.IsListMode }.Count(b => b));

        // 属性变更通知确实触发（派生属性集中通知）
        Assert.Contains(nameof(vm.IsSimpleMode), notified);
        Assert.Contains(nameof(vm.IsDetailedMode), notified);
        Assert.Contains(nameof(vm.IsListMode), notified);

        // D6 回落：无并行批次行时不允许停留在 Detailed（切批次后必须回落 Simple）
        vm.InitBatchMode(new[] { "a.zip", "b.zip" });
        vm.ContentMode = ProgressContentMode.Detailed;
        vm.SetCurrentBatchItem(0);
        Assert.Equal(ProgressContentMode.Simple, vm.ContentMode);
        Assert.True(vm.IsSimpleMode);
    }

    [Fact]
    public void InfoDensity_ChangesVisibleDetailLevel()
    {
        var vm = new ProgressViewModel();

        // 默认 Medium（中等单行可见、统计卡隐藏）
        Assert.Equal(ProgressInfoDensity.Medium, vm.InfoDensity);
        Assert.True(vm.IsMediumDensity);
        Assert.False(vm.IsMinimalDensity);
        Assert.False(vm.IsFullDensity);

        var notified = new List<string>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName ?? string.Empty);

        // (Minimal, Medium, Full) 三元组 —— 三档必须产生三种不同组合
        var seen = new List<(bool Minimal, bool Medium, bool Full)>
        {
            (vm.IsMinimalDensity, vm.IsMediumDensity, vm.IsFullDensity),
        };

        vm.InfoDensity = ProgressInfoDensity.Minimal;
        Assert.True(vm.IsMinimalDensity);
        Assert.False(vm.IsMediumDensity);
        Assert.False(vm.IsFullDensity);
        seen.Add((vm.IsMinimalDensity, vm.IsMediumDensity, vm.IsFullDensity));

        vm.InfoDensity = ProgressInfoDensity.Full;
        Assert.False(vm.IsMinimalDensity);
        Assert.False(vm.IsMediumDensity);
        Assert.True(vm.IsFullDensity);
        seen.Add((vm.IsMinimalDensity, vm.IsMediumDensity, vm.IsFullDensity));

        // 三档输出互不相同（Medium 已在开头采集）
        Assert.Equal(3, seen.Distinct().Count());

        // 派生属性变更通知触发
        Assert.Contains(nameof(vm.IsMinimalDensity), notified);
        Assert.Contains(nameof(vm.IsMediumDensity), notified);
        Assert.Contains(nameof(vm.IsFullDensity), notified);
    }

    [Fact]
    public void ParallelDegree_ParallelStatVisibleOnlyWhenDegreeAtLeastTwo()
    {
        var vm = new ProgressViewModel();

        // 未接线（null）→ 隐藏
        Assert.Null(vm.ParallelDegree);
        Assert.False(vm.HasParallelDegree);

        var notified = new List<string>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName ?? string.Empty);

        // degree = 1（串行回退）→ 隐藏
        vm.ParallelDegree = 1;
        Assert.False(vm.HasParallelDegree);

        // degree >= 2 → 显示
        vm.ParallelDegree = 2;
        Assert.True(vm.HasParallelDegree);

        vm.ParallelDegree = 8;
        Assert.True(vm.HasParallelDegree);

        // degree 回落到 1 → 隐藏（两个分支都覆盖）
        vm.ParallelDegree = 1;
        Assert.False(vm.HasParallelDegree);

        // 派生属性变更通知触发
        Assert.Contains(nameof(vm.HasParallelDegree), notified);

        // 新批次重置并行度 → 隐藏
        vm.InitBatchMode(new[] { "a.zip" });
        Assert.Null(vm.ParallelDegree);
        Assert.False(vm.HasParallelDegree);
    }

    // ════════════════════════════════════════════
    //  T11: 逐条目终态事件（EntryStatus）播种与 O(1) upsert
    // ════════════════════════════════════════════

    [Fact]
    public void SetProgress_WithEntryStatus_SeedsRowWhenNotPreSeeded()
    {
        var vm = new ProgressViewModel();
        Assert.Empty(vm.EntryItems);

        // 未播种（渐进模式兜底路径）：首条终态事件直接建行
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "docs/readme.txt",
            EntryStatus = ArchiveEntryStatus.Completed,
        });

        // 恰好一行
        Assert.Single(vm.EntryItems);
        var row = vm.EntryItems[0];
        Assert.Equal("docs/readme.txt", row.EntryKey);
        Assert.Equal("readme.txt", row.Name);
        Assert.Equal(EntryRowState.Completed, row.State);
        Assert.True(row.IsTerminal);
        Assert.True(vm.HasEntryItems);

        // 行内状态文案解析为本地化 Progress_Entry_* 值（新 key 的 JSON 值确实被消费）
        Assert.Equal(LocalizationManager.T("Progress_Entry_Completed"), row.StatusText);
        Assert.False(string.IsNullOrWhiteSpace(row.StatusText));
        Assert.NotEqual("Progress_Entry_Completed", row.StatusText);
    }

    [Fact]
    public void SetProgress_WithEntryStatus_UpdatesExistingRowWithoutDuplicating()
    {
        var vm = new ProgressViewModel();

        // 第一次：建行
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "folder/data.bin",
            EntryStatus = ArchiveEntryStatus.Completed,
        });
        Assert.Single(vm.EntryItems);
        var first = vm.EntryItems[0];
        Assert.Equal(LocalizationManager.T("Progress_Entry_Completed"), first.StatusText);

        // 第二次：同一 EntryKey 的另一终态 —— 必须原地更新，绝不追加重复行
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "folder/data.bin",
            EntryStatus = ArchiveEntryStatus.Skipped,
        });

        Assert.Single(vm.EntryItems);
        var row = vm.EntryItems[0];
        Assert.Same(first, row);
        Assert.Equal(EntryRowState.Skipped, row.State);
        Assert.Equal("folder/data.bin", row.EntryKey);

        // 状态文案随之刷新为新的本地化值
        Assert.Equal(LocalizationManager.T("Progress_Entry_Skipped"), row.StatusText);
        Assert.NotEqual("Progress_Entry_Skipped", row.StatusText);

        // 第三个不同 key 仍会新建行（upsert 不误伤其它条目）
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "folder/other.txt",
            EntryStatus = ArchiveEntryStatus.Failed,
        });
        Assert.Equal(2, vm.EntryItems.Count);
        Assert.Equal(EntryRowState.Failed, vm.EntryItems[1].State);
        Assert.Equal(LocalizationManager.T("Progress_Entry_Failed"), vm.EntryItems[1].StatusText);
    }

    [Fact]
    public void SetProgress_WithEntryStatus_DoesNotClobberPercentOrOverallSpeed()
    {
        var vm = new ProgressViewModel();

        // 常规进度报告：确立总百分比与速度文案基线
        vm.SetProgress(new ArchiveProgress
        {
            PercentComplete = 42,
            ProcessedBytes = 1_000_000,
            TotalBytes = 100_000_000,
            CurrentFile = "a.txt",
        });
        Assert.Equal(42, vm.PercentComplete);
        var baselinePercent = vm.PercentComplete;
        var baselineSpeed = vm.SpeedText;
        var baselineFilePercent = vm.FilePercentComplete;

        // 紧接着（100ms 节流窗口内）发一条逐条目终态事件：
        // ① 状态更新不得被节流吞掉 → 行必须落地
        // ② 早返回 → 不得触碰总百分比 / 总速度
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "a.txt",
            EntryStatus = ArchiveEntryStatus.Failed,
        });

        Assert.Single(vm.EntryItems);
        Assert.Equal(EntryRowState.Failed, vm.EntryItems[0].State);
        Assert.Equal(LocalizationManager.T("Progress_Entry_Failed"), vm.EntryItems[0].StatusText);

        // 总百分比与速度不受逐条目事件影响
        Assert.Equal(baselinePercent, vm.PercentComplete);
        Assert.Equal(42, vm.PercentComplete);
        Assert.Equal(baselineSpeed, vm.SpeedText);
        Assert.Equal(baselineFilePercent, vm.FilePercentComplete);
    }
}
