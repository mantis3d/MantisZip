using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
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

    // ════════════════════════════════════════════
    //  D7: 播种路径（SeedEntryItems / ClearEntryItems）
    //  CLI 单文件/多文件叶子直连引擎、绕过 ExtractFlow.ExtractAsync 后自行播种，
    //  故这条路径必须有独立覆盖（此前只覆盖了未播种的 upsert 兜底）。
    // ════════════════════════════════════════════

    [Fact]
    public void SeedEntryItems_CreatesPendingRowsInOrder()
    {
        var vm = new ProgressViewModel();
        Assert.Empty(vm.EntryItems);
        Assert.False(vm.HasEntryItems);

        vm.SeedEntryItems(new[]
        {
            ("docs/a.txt", "a.txt", 100L),
            ("docs/b.txt", "b.txt", 200L),
        });

        Assert.Equal(2, vm.EntryItems.Count);
        Assert.True(vm.HasEntryItems);

        var row = vm.EntryItems[0];
        Assert.Equal("docs/a.txt", row.EntryKey);
        Assert.Equal("a.txt", row.Name);
        Assert.Equal(100L, row.Size);
        // 播种行必须落 Pending，等待文案走本地化（Rule 13 禁止硬编码）
        Assert.Equal(EntryRowState.Pending, row.State);
        Assert.False(row.IsTerminal);
        Assert.Equal(LocalizationManager.T("Progress_Entry_Pending"), row.StatusText);
        Assert.NotEqual("Progress_Entry_Pending", row.StatusText);

        // 顺序必须与传入一致（列表行序 = 压缩包内条目序）
        Assert.Equal("docs/b.txt", vm.EntryItems[1].EntryKey);
    }

    [Fact]
    public void SeedEntryItems_SecondCallReplacesPreviousRows()
    {
        var vm = new ProgressViewModel();

        vm.SeedEntryItems(new[] { ("old/1.txt", "1.txt", 1L) });
        Assert.Single(vm.EntryItems);

        // 第二次播种必须先清空（批处理多包切换：上一包行不得残留）
        vm.SeedEntryItems(new[]
        {
            ("new/1.txt", "1.txt", 10L),
            ("new/2.txt", "2.txt", 20L),
        });

        Assert.Equal(2, vm.EntryItems.Count);
        Assert.DoesNotContain(vm.EntryItems, r => r.EntryKey == "old/1.txt");
        Assert.All(vm.EntryItems, r => Assert.Equal(EntryRowState.Pending, r.State));
    }

    [Fact]
    public void SeedEntryItems_ThenTerminalEvent_UpdatesSeededRowInPlace()
    {
        var vm = new ProgressViewModel();

        vm.SeedEntryItems(new[]
        {
            ("docs/a.txt", "a.txt", 100L),
            ("docs/b.txt", "b.txt", 200L),
        });
        var seeded = vm.EntryItems[0];

        // 播种 → 终态事件的交接：命中已播行，原地更新，绝不追加重复行
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "docs/a.txt",
            EntryStatus = ArchiveEntryStatus.Completed,
        });

        Assert.Equal(2, vm.EntryItems.Count);
        Assert.Same(seeded, vm.EntryItems[0]);
        Assert.Equal(EntryRowState.Completed, vm.EntryItems[0].State);
        Assert.Equal(LocalizationManager.T("Progress_Entry_Completed"), vm.EntryItems[0].StatusText);
        // 未收到终态的行仍停在 Pending
        Assert.Equal(EntryRowState.Pending, vm.EntryItems[1].State);
    }

    [Fact]
    public void ClearEntryItems_ResetsKeyIndex_SoNextEventCreatesFreshRow()
    {
        var vm = new ProgressViewModel();

        vm.SeedEntryItems(new[] { ("docs/a.txt", "a.txt", 100L) });
        var stale = vm.EntryItems[0];

        vm.ClearEntryItems();
        Assert.Empty(vm.EntryItems);
        Assert.False(vm.HasEntryItems);

        // key 索引必须同步清空：同名 key 的终态事件应新建行，而不是改到已废弃的旧行对象上
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "docs/a.txt",
            EntryStatus = ArchiveEntryStatus.Failed,
        });

        Assert.Single(vm.EntryItems);
        Assert.NotSame(stale, vm.EntryItems[0]);
        Assert.Equal(EntryRowState.Failed, vm.EntryItems[0].State);
    }

    [Fact]
    public void SeedAndClearEntryItems_RaiseHasEntryItemsNotification()
    {
        var vm = new ProgressViewModel();

        var notified = new List<string>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName ?? string.Empty);

        vm.SeedEntryItems(new[] { ("a.txt", "a.txt", 1L) });
        Assert.Contains(nameof(vm.HasEntryItems), notified);

        notified.Clear();
        vm.ClearEntryItems();
        Assert.Contains(nameof(vm.HasEntryItems), notified);
    }

    // ════════════════════════════════════════════
    //  手工测试问题 2：进度窗口缺「文件总数」显示（N/M 分子分母）
    // ════════════════════════════════════════════

    /// <summary>
    /// 引擎上报 TotalFiles 时，「已处理」统计必须显示 N/M 分子分母
    /// （如 "已处理 60/100"），而不是只有已处理数。
    /// 锁定缺陷：进度窗口缺文件总数显示（手工测试问题 2）。
    /// </summary>
    [Fact]
    public void SetProgress_WithTotalFiles_ShowsProcessedOverTotal()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { TotalFiles = 100, ProcessedFiles = 60 });

        Assert.Contains("60/100", vm.StatsProcessedText);
    }

    /// <summary>
    /// 短标签派生不得泄漏 {0} 占位符（标签从格式化文案剥离占位符而来，
    /// 格式改为带分子分母后派生逻辑必须同步）。
    /// </summary>
    [Fact]
    public void StatsProcessedLabel_DoesNotLeakPlaceholder()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { TotalFiles = 100, ProcessedFiles = 60 });

        Assert.DoesNotContain("{0}", vm.StatsProcessedLabel);
    }

    /// <summary>
    /// 兼容重载（TotalFiles=0）不得清零已显示的 N/M 统计。
    /// </summary>
    [Fact]
    public void SetProgress_ZeroTotalFiles_KeepsPreviousFraction()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { TotalFiles = 100, ProcessedFiles = 60 });
        vm.SetProgress(new ArchiveProgress { TotalFiles = 0, ProcessedFiles = 999 });

        Assert.Contains("60/100", vm.StatsProcessedText);
    }

    // ════════════════════════════════════════════
    //  波 B（编译级红）：StatsProcessedCount 改 string + 总大小新成员
    // ════════════════════════════════════════════

    /// <summary>
    /// 统计卡计数槽显示已处理数纯计数（2026-10-09 方案 A 去分母：原 N/M 分数
    /// 的分母与总大小卡行 2 的文件总数同源重复，完成比例由总进度条承担）。
    /// 中等档 StatsProcessedText 仍为 N/M 分数，见 SetProgress_WithTotalFiles_ShowsProcessedOverTotal。
    /// </summary>
    [Fact]
    public void StatsProcessedCount_ShowsPureCount()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { TotalFiles = 100, ProcessedFiles = 60 });

        Assert.Equal("60", vm.StatsProcessedCount);
    }

    /// <summary>
    /// 引擎上报 TotalBytes 时总大小三个成员必须填充（编译级红：
    /// StatsTotalSizeText/StatsTotalSizeLabel 为新增成员）。
    /// </summary>
    [Fact]
    public void SetProgress_WithTotalBytes_PopulatesTotalSizeMembers()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            TotalFiles = 10,
            ProcessedFiles = 1,
            TotalBytes = 1048576,
        });

        Assert.False(string.IsNullOrEmpty(vm.StatsTotalSizeText));
        Assert.False(string.IsNullOrEmpty(vm.StatsTotalSizeValue));
        Assert.DoesNotContain("{0}", vm.StatsTotalSizeLabel);
    }

    /// <summary>
    /// 引擎未上报 TotalBytes（7z/TAR）时总大小为空（编译级红 + Rule 6：
    /// 空串由 XAML 侧 StringNotEmpty 转换器隐藏整卡/整行）。
    /// </summary>
    [Fact]
    public void SetProgress_WithoutTotalBytes_LeavesTotalSizeEmpty()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { TotalFiles = 10, ProcessedFiles = 1 });

        Assert.Equal(string.Empty, vm.StatsTotalSizeText);
        Assert.Equal(string.Empty, vm.StatsTotalSizeValue);
    }

    // ════════════════════════════════════════════
    //  统计卡三行结构（progress-stats-cards-three-row）：行 3 文件大小
    // ════════════════════════════════════════════

    /// <summary>
    /// 引擎上报 TotalBytes 时，已处理卡行 3 必须填充文件大小（编译级红：
    /// StatsProcessedSize 为新增成员）。行值映射见设计 D2。
    /// </summary>
    [Fact]
    public void SetProgress_WithTotalBytes_PopulatesProcessedSize()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { TotalBytes = 1048576, ProcessedBytes = 524288 });

        Assert.Equal(FormatUtil.FormatSize(524288), vm.StatsProcessedSize);
    }

    /// <summary>
    /// 引擎未上报 TotalBytes（7z/TAR）时，已处理卡行 3 保持默认 — 占位
    /// （行级永不隐藏，缺数据显 —，设计 D1）。
    /// </summary>
    [Fact]
    public void SetProgress_WithoutTotalBytes_ProcessedSizeStaysDash()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { TotalFiles = 10, ProcessedFiles = 1 });

        Assert.Equal("—", vm.StatsProcessedSize);
    }

    /// <summary>
    /// 引擎上报 TotalFiles 时，总大小卡行 2 必须填充文件总数；
    /// 初始默认 —（行级占位，D2/D4）。编译级红：StatsTotalCount 为新增成员。
    /// </summary>
    [Fact]
    public void SetProgress_WithTotalFiles_PopulatesTotalCount()
    {
        var vm = new ProgressViewModel();
        Assert.Equal("—", vm.StatsTotalCount);

        vm.SetProgress(new ArchiveProgress { TotalFiles = 100, ProcessedFiles = 60 });

        Assert.Equal("100", vm.StatsTotalCount);
    }

    /// <summary>
    /// 播种带 Size 的条目行后上报 Skipped 终态，跳过卡行 3 必须累加该行
    /// 原始（未压缩）尺寸（D2 注 2 / D4 累加器：播种行 Size 回退路径）。
    /// </summary>
    [Fact]
    public void UpdateEntryStatus_SkippedWithSeededSize_AccumulatesSkippedSize()
    {
        var vm = new ProgressViewModel();
        vm.SeedEntryItems(new[] { ("a.txt", "a.txt", 1048576L), ("b.txt", "b.txt", 2097152L) });

        vm.UpdateEntryStatus("a.txt", EntryRowState.Skipped, null);
        Assert.Equal(FormatUtil.FormatSize(1048576), vm.StatsSkippedSize);

        vm.UpdateEntryStatus("b.txt", EntryRowState.Skipped, null);
        Assert.Equal(FormatUtil.FormatSize(1048576 + 2097152), vm.StatsSkippedSize);
    }

    /// <summary>
    /// Failed 终态累加到出错卡行 3（与 Skipped 通道互不干扰）。
    /// </summary>
    [Fact]
    public void UpdateEntryStatus_FailedWithSeededSize_AccumulatesFailedSize()
    {
        var vm = new ProgressViewModel();
        vm.SeedEntryItems(new[] { ("a.txt", "a.txt", 4096L) });

        vm.UpdateEntryStatus("a.txt", EntryRowState.Failed, null);

        Assert.Equal(FormatUtil.FormatSize(4096), vm.StatsFailedSize);
        Assert.Equal("—", vm.StatsSkippedSize);
    }

    /// <summary>
    /// 同一条目行重复上报同一终态只计一次（D4 去重守卫：累加前检查旧状态）。
    /// </summary>
    [Fact]
    public void UpdateEntryStatus_SameTerminalStateReportedTwice_CountsOnce()
    {
        var vm = new ProgressViewModel();
        vm.SeedEntryItems(new[] { ("a.txt", "a.txt", 1048576L) });

        vm.UpdateEntryStatus("a.txt", EntryRowState.Skipped, null);
        vm.UpdateEntryStatus("a.txt", EntryRowState.Skipped, null);

        Assert.Equal(FormatUtil.FormatSize(1048576), vm.StatsSkippedSize);
    }

    /// <summary>
    /// ClearEntryItems 清零累加器并复位三个 Size 字符串为 —
    /// （SeedEntryItems 内部先调 ClearEntryItems，新批次语义自动覆盖）。
    /// </summary>
    [Fact]
    public void ClearEntryItems_ResetsAccumulatedSizes()
    {
        var vm = new ProgressViewModel();
        vm.SeedEntryItems(new[] { ("a.txt", "a.txt", 1048576L) });
        vm.UpdateEntryStatus("a.txt", EntryRowState.Overwritten, null);
        Assert.Equal(FormatUtil.FormatSize(1048576), vm.StatsOverwrittenSize);

        vm.ClearEntryItems();

        Assert.Equal("—", vm.StatsSkippedSize);
        Assert.Equal("—", vm.StatsFailedSize);
        Assert.Equal("—", vm.StatsOverwrittenSize);
    }
}
