using System.Reflection;
using Avalonia.Headless.XUnit;
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

        // 默认 Detailed（构造后无需赋值即生效；窗口构造时按 AppSettings 持久化值覆盖）
        Assert.Equal(ProgressContentMode.Detailed, vm.ContentMode);
        Assert.False(vm.IsSimpleMode);
        Assert.True(vm.IsDetailedMode);
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
    }

    // channel-info：详细门禁已移除，详细模式常显；非并行由单行通道承载。
    [Fact]
    public void SetCurrentBatchItem_DetailedMode_NoLongerFallsBackToSimple()
    {
        var vm = new ProgressViewModel();
        vm.InitBatchMode(new[] { "a.zip", "b.zip" });
        vm.ContentMode = ProgressContentMode.Detailed;
        vm.SetCurrentBatchItem(0);
        Assert.Equal(ProgressContentMode.Detailed, vm.ContentMode);
    }

    [Fact]
    public void SetProgress_NonParallel_SynthesizesSingleChannelRow()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            CurrentFile = "docs/a.txt",
            PercentComplete = 40,
            FileTotalBytes = 400,
            BatchTotalBytes = 1000,
            BatchProcessedBytes = 400,
        });
        var rows = vm.ParallelBatchItems;
        Assert.Single(rows);
        Assert.False(rows[0].IsParallel);
        Assert.Equal("docs", rows[0].DirectoryText);
        Assert.Equal("a.txt", rows[0].FileNameText);
        Assert.Equal("400 B", rows[0].FileSizeText);        // 左区文件大小
        Assert.Contains("400 B/1000 B", rows[0].InfoText);  // 右区字节进度
        // 2026-10-10 修订（D2/D8/D10）：单行右区仅信息列——底纹/百分比与底部总进度重复，不再填充
        Assert.Equal(string.Empty, rows[0].PctDetailText);
        Assert.Equal(0.0, rows[0].BatchRatio);
        Assert.True(rows[0].ShowRightRegion);               // 信息列非空 → 右区显示
    }

    // 2026-10-10 修订（决策 b）：单行信息列为空（7z 压缩/解压无字节无压缩率）→ 整右区隐藏，左区占满
    [Fact]
    public void SetProgress_NonParallel_NoBytesNoRatio_HidesRightRegion()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            CurrentFile = "docs/a.txt",
            PercentComplete = 40,
            // 不填 BatchTotalBytes/CompressionRatio → InfoText 为空
        });
        var row = Assert.Single(vm.ParallelBatchItems);
        Assert.False(row.IsParallel);
        Assert.Equal(string.Empty, row.InfoText);
        Assert.False(row.ShowRightRegion);
    }

    // 并行行右区恒显（序号/底纹/百分比有效），即使信息列为空
    [Fact]
    public void ParallelRow_EmptyInfoText_StillShowsRightRegion()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            CurrentFile = "a.txt",
            BatchIndex = 0,
            BatchCount = 2,
            BatchPercentComplete = 50,
        });
        var row = vm.ParallelBatchItems[0];
        Assert.True(row.IsParallel);
        Assert.True(row.ShowRightRegion);
    }

    // 预测试/收集阶段的非批次上报会先合成一条 IsParallel=false 单行（占索引 0）；
    // 首个并行批次上报必须清掉它，否则 batch 0 写进只读 IsParallel 的行 → 底纹（批次进度条）/
    // 序号/N% 全部永久隐藏（看似「第一批次进度条恒 0」），且后续行序号从 2 起。
    [Fact]
    public void SetProgress_PreTestSingleRow_ThenParallel_BatchZeroGetsParallelRow()
    {
        var vm = new ProgressViewModel();
        // 1) 大包预测试状态上报（无 BatchIndex / EntryKey）→ 合成单通道行
        vm.SetProgress(new ArchiveProgress { CurrentFile = "STATUS_TESTING_PARALLEL_DEGREE", PercentComplete = 0 });
        var single = Assert.Single(vm.ParallelBatchItems);
        Assert.False(single.IsParallel);

        // 2) batch 1 先到（补洞创建），batch 0 随后
        vm.SetProgress(new ArchiveProgress
        {
            CurrentFile = "b1.txt", BatchIndex = 1, BatchCount = 2,
            BatchPercentComplete = 40, BatchTotalBytes = 100, BatchProcessedBytes = 40,
        });
        vm.SetProgress(new ArchiveProgress
        {
            CurrentFile = "b0.txt", BatchIndex = 0, BatchCount = 2,
            BatchPercentComplete = 25, BatchTotalBytes = 200, BatchProcessedBytes = 50,
        });

        var rows = vm.ParallelBatchItems;
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsParallel));   // 无残留非并行单行
        Assert.Equal(1, rows[0].Index);                     // 序号 1（不是从 2 起）
        Assert.Equal(2, rows[1].Index);
        Assert.Equal(25, rows[0].Percent);
        Assert.Equal("25%", rows[0].PctDetailText);         // 第一行百分比不再被 IsVisible 吞掉
        Assert.Equal(40, rows[1].Percent);
    }

    [Fact]
    public void SetProgress_Parallel_MarksRowsIsParallel()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            CurrentFile = "a.txt",
            BatchIndex = 0,
            BatchCount = 2,
            BatchPercentComplete = 50,
            TotalBytes = 10,
            ProcessedBytes = 5,
        });
        Assert.True(vm.ParallelBatchItems[0].IsParallel);
    }

    // P1 回归：预选详细模式下，首个并行行到达必须通知 HasParallelChannel
    // （防 CollectionChanged 只通知 HasParallelBatches 的通知缺口）
    [Fact]
    public void ParallelBatchRowAdded_RaisesHasParallelChannelNotification()
    {
        var vm = new ProgressViewModel();
        vm.ContentMode = ProgressContentMode.Detailed;   // 预选详细（门禁已移除，允许）
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProgressViewModel.HasParallelChannel)) raised = true;
        };
        vm.SetProgress(new ArchiveProgress
        {
            CurrentFile = "a.txt",
            BatchIndex = 0,
            BatchCount = 2,
            BatchPercentComplete = 10,
        });
        Assert.True(raised);
    }

    // ════ 逐文件压缩率计划（progress-file-compression-ratio）════

    [Fact]
    public void UpdateEntryStatus_WithCompressedBytes_SetsCompressedSize()
    {
        var vm = new ProgressViewModel();
        vm.SeedEntryItems(new[] { ("docs/a.txt", "a.txt", 1000L) });
        vm.UpdateEntryStatus("docs/a.txt", EntryRowState.Completed, null, fileSize: 1000, compressedBytes: 500);
        var row = vm.EntryItems[0];
        Assert.Equal(500, row.CompressedSize);
        Assert.Contains("500 B", row.CompressedText);
        Assert.Contains("50%", row.CompressedText);
    }

    [Fact]
    public void UpdateEntryStatus_WithoutCompressedBytes_LeavesCompressedTextEmpty()
    {
        var vm = new ProgressViewModel();
        vm.SeedEntryItems(new[] { ("a.txt", "a.txt", 1000L) });
        vm.UpdateEntryStatus("a.txt", EntryRowState.Completed, null);
        Assert.Equal(string.Empty, vm.EntryItems[0].CompressedText);
    }

    [Fact]
    public void SetProgress_TotalCompressedBytes_PopulatesOverallSummary()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { TotalBytes = 1000, PercentComplete = 100, TotalCompressedBytes = 600 });
        Assert.True(vm.HasOverallCompression);
        Assert.Contains("600 B", vm.OverallCompressionText);
        Assert.Contains("60%", vm.OverallCompressionText);
    }

    // 回归：压缩侧播种键含反斜杠（FileScanner），后置回填键为正斜杠（ZIP 条目键）——归一化后必须命中同一行，
    // 否则后置回填会新建 Size=0 的重复行，压缩率列恒空。
    [Fact]
    public void UpdateEntryStatus_BackslashSeededKey_MatchesSlashReportKey()
    {
        var vm = new ProgressViewModel();
        vm.SeedEntryItems(new[] { (@"dir\sub\a.txt", "a.txt", 1000L) });
        vm.UpdateEntryStatus("dir/sub/a.txt", EntryRowState.Completed, null, fileSize: 1000, compressedBytes: 500);
        Assert.Single(vm.EntryItems);                 // 不新建重复行
        Assert.Equal(500, vm.EntryItems[0].CompressedSize);
        Assert.Contains("50%", vm.EntryItems[0].CompressedText);
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

    // ════════════════════════════════════════════
    //  T5: 确定性条目结果聚合（红/黄摘要段 + 📌 自动钉住）
    // ════════════════════════════════════════════

    [Fact]
    public void SetProgress_FailedWithReason_BuildsRedSummary_AndPinsOpen()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "a/b.txt",
            EntryStatus = ArchiveEntryStatus.Failed,
            EntryFailureReason = EntryFailureReason.PermissionDenied,
        });

        Assert.True(vm.HasErrors);
        Assert.False(vm.HasSkippedEntries);
        Assert.Contains("a/b.txt", vm.ErrorSummaryText);
        Assert.Contains(LocalizationManager.T("Error_Reason_PermissionDenied"), vm.ErrorSummaryText);
        Assert.True(vm.KeepOpenOnComplete);
    }

    [Fact]
    public void SetProgress_SkippedWithReason_BuildsYellowSummary_WithoutHasErrors()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "c.dat",
            EntryStatus = ArchiveEntryStatus.Skipped,
            EntryFailureReason = EntryFailureReason.IoFailed,
        });

        Assert.False(vm.HasErrors);
        Assert.True(vm.HasSkippedEntries);
        Assert.Contains("c.dat", vm.SkippedSummaryText);
        Assert.False(vm.KeepOpenOnComplete);
    }

    [Fact]
    public void SetProgress_SkippedWithoutReason_NotCollected()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "c.dat",
            EntryStatus = ArchiveEntryStatus.Skipped,
        });

        Assert.False(vm.HasSkippedEntries);
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void SetProgress_DuplicateEntryKey_CollapsedToSingleLine()
    {
        var vm = new ProgressViewModel();
        for (int i = 0; i < 3; i++)
        {
            vm.SetProgress(new ArchiveProgress
            {
                EntryKey = "a/b.txt",
                EntryStatus = ArchiveEntryStatus.Failed,
                EntryFailureReason = EntryFailureReason.IoFailed,
            });
        }

        Assert.True(vm.HasErrors);
        // 分布行 + 去重后的单条目行 = 2 行；同 key 条目仅出现一次（去重）
        var lines = vm.ErrorSummaryText!.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal(1, lines.Count(l => l.Contains("a/b.txt")));
    }

    [Fact]
    public void SetProgress_MoreThan20Failures_TruncatesWithMoreLine()
    {
        var vm = new ProgressViewModel();
        for (int i = 0; i < 25; i++)
        {
            vm.SetProgress(new ArchiveProgress
            {
                EntryKey = $"f{i}.txt",
                EntryStatus = ArchiveEntryStatus.Failed,
                EntryFailureReason = EntryFailureReason.IoFailed,
            });
        }

        // 截断公式：SummaryMaxLines=20 → 分布行 1 + 18 条明细行 + 1 条溢出行 = 20 行；
        // 溢出计数 = 总数 25 - 已展示 18 = 7。
        var lines = vm.ErrorSummaryText!.Split('\n');
        Assert.Equal(20, lines.Length);
        Assert.Contains(LocalizationManager.T("Progress_ErrorSummary_More", 7), lines[^1]);
    }

    [Fact]
    public void SetProgress_MultipleFailReasons_RedBreakdownLineFirst()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { EntryKey = "a.txt", EntryStatus = ArchiveEntryStatus.Failed, EntryFailureReason = EntryFailureReason.PermissionDenied });
        vm.SetProgress(new ArchiveProgress { EntryKey = "b.txt", EntryStatus = ArchiveEntryStatus.Failed, EntryFailureReason = EntryFailureReason.PermissionDenied });
        vm.SetProgress(new ArchiveProgress { EntryKey = "c.txt", EntryStatus = ArchiveEntryStatus.Failed, EntryFailureReason = EntryFailureReason.CrcMismatch });

        var expected = LocalizationManager.T("Progress_FailedBreakdown_Fmt", 3,
            $"{LocalizationManager.T("Error_Reason_PermissionDenied")} 2 · {LocalizationManager.T("Error_Reason_CrcMismatch")} 1");
        // 分布行为首行（无整包头），按数量降序
        Assert.Equal(expected, vm.ErrorSummaryText!.Split('\n')[0]);
    }

    [Fact]
    public void SetProgress_SkippedWithReason_YellowBreakdownLine()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { EntryKey = "c.dat", EntryStatus = ArchiveEntryStatus.Skipped, EntryFailureReason = EntryFailureReason.IoFailed });

        var expected = LocalizationManager.T("Progress_SkippedBreakdown_Fmt", 1,
            $"{LocalizationManager.T("Error_Reason_IoFailed")} 1");
        Assert.Contains(expected, vm.SkippedSummaryText);
    }

    [Fact]
    public void SetErrorSummary_WholeArchiveOnly_NoBreakdownLine()
    {
        var vm = new ProgressViewModel();
        vm.SetErrorSummary("disk full");

        // 无逐条目失败 → 仅整包头 1 行，不显示分布行
        Assert.Equal(1, vm.ErrorSummaryText!.Split('\n').Length);
    }

    [Fact]
    public void SetErrorSummary_WithEntries_BreakdownAfterHeader()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress { EntryKey = "x.txt", EntryStatus = ArchiveEntryStatus.Failed, EntryFailureReason = EntryFailureReason.Other });
        vm.SetErrorSummary("disk full");

        var lines = vm.ErrorSummaryText!.Split('\n');
        Assert.Equal("disk full", lines[0]);
        Assert.Equal(LocalizationManager.T("Progress_FailedBreakdown_Fmt", 1,
            $"{LocalizationManager.T("Error_Reason_Other")} 1"), lines[1]);
    }

    [Fact]
    public void SetProgress_FailedWithDetail_BuildsSummaryTooltipWithHint()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "a.txt",
            EntryStatus = ArchiveEntryStatus.Failed,
            EntryFailureReason = EntryFailureReason.IoFailed,
            EntryErrorDetail = "The disk is full.",
            EntryErrorHResult = unchecked((int)0x80070070), // ERROR_DISK_FULL
        });

        Assert.NotNull(vm.ErrorSummaryTooltip);
        Assert.Contains("a.txt", vm.ErrorSummaryTooltip);
        Assert.Contains("The disk is full.", vm.ErrorSummaryTooltip);
        Assert.Contains(LocalizationManager.T("Error_Hint_DiskFull"), vm.ErrorSummaryTooltip);
    }

    [Fact]
    public void SetProgress_FailedWithoutDetail_NoSummaryTooltip()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "a.txt",
            EntryStatus = ArchiveEntryStatus.Failed,
            EntryFailureReason = EntryFailureReason.Other,
        });

        Assert.Null(vm.ErrorSummaryTooltip);
    }

    [Fact]
    public void SetErrorSummary_WithDetail_BuildsSummaryTooltip()
    {
        var vm = new ProgressViewModel();
        vm.SetErrorSummary("disk full", "raw detail text");

        Assert.NotNull(vm.ErrorSummaryTooltip);
        Assert.Contains("raw detail text", vm.ErrorSummaryTooltip);
    }

    [Fact]
    public void SetErrorSummary_AfterBeginPreparing_SwitchesStatusFromPreparingToFailed()
    {
        // 回归：引擎在「准备静默期」（首次上报前）抛错时，状态行原先永久停在 BeginPreparing
        // 写入的「正在准备…」（EndPreparing 只在首个 SetProgress 调用）→ SetErrorSummary 须切到失败文案。
        var vm = new ProgressViewModel();
        vm.BeginPreparing();
        Assert.Equal(LocalizationManager.T("Progress_Preparing"), vm.StatusMessage);

        vm.SetErrorSummary("压缩条目包含非法路径: ../evil.txt");

        Assert.Equal(LocalizationManager.T("Status_OperationFailed"), vm.StatusMessage);
        Assert.Equal(ProgressStatusKind.Failed, vm.StatusKind);
    }

    [Fact]
    public void CombinedSummaryText_RedThenYellow_SingleNewline()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "x.txt",
            EntryStatus = ArchiveEntryStatus.Failed,
            EntryFailureReason = EntryFailureReason.Other,
        });
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "y.txt",
            EntryStatus = ArchiveEntryStatus.Skipped,
            EntryFailureReason = EntryFailureReason.IoFailed,
        });

        Assert.NotNull(vm.ErrorSummaryText);
        Assert.NotNull(vm.SkippedSummaryText);
        Assert.StartsWith(vm.ErrorSummaryText!, vm.CombinedSummaryText);
        Assert.Contains("\n" + vm.SkippedSummaryText, vm.CombinedSummaryText);
        Assert.DoesNotContain("\n\n", vm.CombinedSummaryText);
    }

    [Fact]
    public void SetErrorSummary_WholeArchiveError_PinsOpen_AndKeepsEntryLines()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "x.txt",
            EntryStatus = ArchiveEntryStatus.Failed,
            EntryFailureReason = EntryFailureReason.Other,
        });

        vm.SetErrorSummary("disk full");

        Assert.True(vm.KeepOpenOnComplete);
        Assert.StartsWith("disk full", vm.ErrorSummaryText!);
        Assert.Contains("x.txt", vm.ErrorSummaryText!);
    }

    [Fact]
    public void UpdateBatchItemStatus_Failed_PinsOpen_OtherStatusDoesNot()
    {
        // Failed → 钉住
        var failedVm = new ProgressViewModel();
        failedVm.InitBatchMode(new[] { "a.zip" });
        failedVm.UpdateBatchItemStatus(0, BatchItemStatus.Failed);
        Assert.True(failedVm.KeepOpenOnComplete);

        // Completed → 不钉住
        var okVm = new ProgressViewModel();
        okVm.InitBatchMode(new[] { "a.zip" });
        okVm.UpdateBatchItemStatus(0, BatchItemStatus.Completed);
        Assert.False(okVm.KeepOpenOnComplete);
    }

    [Fact]
    public void IsTestFlow_True_PinsOpen()
    {
        var vm = new ProgressViewModel();
        Assert.False(vm.KeepOpenOnComplete);

        vm.IsTestFlow = true;

        Assert.True(vm.KeepOpenOnComplete);
    }

    [Fact]
    public void ClearEntryOutcomes_ResetsSummaryAndCounts()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            EntryKey = "x.txt",
            EntryStatus = ArchiveEntryStatus.Failed,
            EntryFailureReason = EntryFailureReason.Other,
        });
        Assert.True(vm.HasErrors);

        vm.ClearEntryOutcomes();

        Assert.False(vm.HasErrors);
        Assert.False(vm.HasSkippedEntries);
        Assert.Null(vm.ErrorSummaryText);
        Assert.Null(vm.SkippedSummaryText);
        Assert.Null(vm.CombinedSummaryText);
    }

    // ═══════════════════════════════════════════════════════════
    //  解压后置开关（spec §11；实例级，不回写设置，D17/D18）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void PostExtractToggles_DefaultsAndNoPersist()
    {
        var vm = new ProgressViewModel();
        // 能力位默认 false（压缩/测试流全藏，D18）
        Assert.False(vm.ShowOpenFolderToggle);
        Assert.False(vm.ShowDeleteArchiveToggle);

        var settings = AppSettings.Load();
        vm.SetExtractPostActions(openFolder: true, deleteArchive: true, openFolderSeed: null);

        // 能力位置 true + 初值镜像设置（不回写——再读设置应不变）
        Assert.True(vm.ShowOpenFolderToggle);
        Assert.True(vm.ShowDeleteArchiveToggle);
        Assert.Equal(settings.OpenFolderAfterExtract, vm.OpenFolderAfterExtract);
        Assert.Equal(settings.DeleteArchiveAfterExtract, vm.DeleteArchiveAfterExtract);

        // 用户在窗口内翻转 → 仅实例级，设置不动
        vm.OpenFolderAfterExtract = !vm.OpenFolderAfterExtract;
        Assert.Equal(settings.OpenFolderAfterExtract, AppSettings.Load().OpenFolderAfterExtract);
    }

    [Fact]
    public void SetExtractPostActions_OpenFolderSeed_OverridesSettingMirror()
    {
        var vm = new ProgressViewModel();

        vm.SetExtractPostActions(true, true, openFolderSeed: true);

        Assert.True(vm.OpenFolderAfterExtract);   // 对话框选择优先于设置镜像（§11.3）
    }

    [Fact]
    public void SetExtractPostActions_DeleteArchiveSeed_OverridesSettingMirror()
    {
        var vm = new ProgressViewModel();

        vm.SetExtractPostActions(true, true, openFolderSeed: null, deleteArchiveSeed: true);

        Assert.True(vm.DeleteArchiveAfterExtract);   // CLI 弹窗勾选作种子
    }

    [Fact]
    public void SetExtractPostActions_BothDisabled_LeavesInitialsDefault()
    {
        var vm = new ProgressViewModel();

        vm.SetExtractPostActions(false, false, null);

        Assert.False(vm.ShowOpenFolderToggle);
        Assert.False(vm.ShowDeleteArchiveToggle);
        Assert.False(vm.OpenFolderAfterExtract);
        Assert.False(vm.DeleteArchiveAfterExtract);
    }

    // ═══════════════════════════════════════════════════════════
    //  状态图标 ↔ 当前状态对应（状态行左侧图标，始终存在且对应状态）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void StatusKind_Default_Preparing()
    {
        var vm = new ProgressViewModel();
        Assert.Equal(ProgressStatusKind.Preparing, vm.StatusKind);
        Assert.Equal("IconArchiveClock", vm.StatusIconKey);
    }

    [Fact]
    public void StatusKind_FirstProgress_TransitionsToRunning()
    {
        var vm = new ProgressViewModel();
        vm.BeginPreparing();
        vm.SetProgress(new ArchiveProgress { PercentComplete = 10, CurrentFile = "a.txt" });
        Assert.Equal(ProgressStatusKind.Running, vm.StatusKind);
        Assert.Equal("IconLightning", vm.StatusIconKey);
    }

    [Fact]
    public void StatusKind_PauseResume()
    {
        var vm = new ProgressViewModel();
        vm.BeginPreparing();
        vm.SetProgress(new ArchiveProgress { PercentComplete = 10 });

        vm.TogglePauseCommand.Execute(null);
        Assert.Equal(ProgressStatusKind.Paused, vm.StatusKind);
        Assert.Equal("IconPause", vm.StatusIconKey);

        vm.TogglePauseCommand.Execute(null);
        Assert.Equal(ProgressStatusKind.Running, vm.StatusKind);
    }

    [Fact]
    public void StatusKind_SetComplete_Completed()
    {
        var vm = new ProgressViewModel();
        vm.SetComplete("done");
        Assert.Equal(ProgressStatusKind.Completed, vm.StatusKind);
        Assert.Equal("IconCheckmark", vm.StatusIconKey);
    }

    [Fact]
    public void StatusKind_SetErrorSummary_Failed()
    {
        var vm = new ProgressViewModel();
        vm.SetErrorSummary("boom");
        Assert.Equal(ProgressStatusKind.Failed, vm.StatusKind);
        Assert.Equal("IconWarning", vm.StatusIconKey);
        Assert.Equal("ThemeStatusErrorBrush", vm.StatusBrushKey);
    }

    [Fact]
    public void StatusKind_Cancel_RunningToCancelled_ButNotAfterComplete()
    {
        var vm = new ProgressViewModel();
        vm.BeginPreparing();
        vm.SetProgress(new ArchiveProgress { PercentComplete = 10 });
        vm.CancelCommand.Execute(null);
        Assert.Equal(ProgressStatusKind.Cancelled, vm.StatusKind);
        Assert.Equal("IconProhibited", vm.StatusIconKey);

        // 已完成窗口点击「关闭」（同一命令）不得改写状态
        var vm2 = new ProgressViewModel();
        vm2.SetComplete("done");
        vm2.CancelCommand.Execute(null);
        Assert.Equal(ProgressStatusKind.Completed, vm2.StatusKind);
    }

    [Fact]
    public void StatusKind_BatchFailed_Failed()
    {
        var vm = new ProgressViewModel();
        vm.InitBatchMode(new[] { "a.zip" });
        vm.UpdateBatchItemStatus(0, BatchItemStatus.Failed, "x");
        Assert.Equal(ProgressStatusKind.Failed, vm.StatusKind);
    }

    // ===== 语言即时刷新 + 退订（CultureChanged） =====

    /// <summary>
    /// 切换语言应触发 LocalizedStrings 的 PropertyChanged，且字典值与 T() 当前值一致。
    /// try/finally 快速恢复语言（xUnit 跨类并行，静态 CultureChanged 不得外泄）。
    /// </summary>
    // 切换语言会改全局静态 LocalizationManager.CurrentLanguage，并触发 CultureChanged；
    // 泄漏的 Avalonia 绑定（如其它测试残留的 ProgressWindow）会在该事件上更新显示，
    // 若在非 UI 线程发出会抛「The calling thread cannot access this object」。
    // 故这两个用例走 AvaloniaFact（headless UI 线程）。
    [AvaloniaFact]
    public void CultureChanged_RefreshesLocalizedStrings()
    {
        var original = LocalizationManager.CurrentLanguage;
        try
        {
            var vm = new ProgressViewModel();
            bool raised = false;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProgressViewModel.LocalizedStrings)) raised = true;
            };

            // 切到与当前不同的语言（保证触发 CultureChanged）
            LocalizationManager.CurrentLanguage =
                original == AppLanguage.English ? AppLanguage.Chinese : AppLanguage.English;

            Assert.True(raised, "CultureChanged 后应触发 LocalizedStrings PropertyChanged");
            Assert.Equal(
                LocalizationManager.T("Progress_Cancel"),
                vm.LocalizedStrings["Progress_Cancel"]);
        }
        finally
        {
            LocalizationManager.CurrentLanguage = original;
        }
    }

    /// <summary>
    /// DetachLocalization 后不应再响应 CultureChanged（防止窗口关闭后 VM 被静态事件根引用泄漏）。
    /// </summary>
    [AvaloniaFact]
    public void DetachLocalization_PreventsCultureChangedRefresh()
    {
        var original = LocalizationManager.CurrentLanguage;
        try
        {
            var vm = new ProgressViewModel();
            vm.DetachLocalization();
            bool raised = false;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProgressViewModel.LocalizedStrings)) raised = true;
            };

            LocalizationManager.CurrentLanguage =
                original == AppLanguage.English ? AppLanguage.Chinese : AppLanguage.English;

            Assert.False(raised, "Detach 后不应再响应 CultureChanged");
        }
        finally
        {
            LocalizationManager.CurrentLanguage = original;
        }
    }

    // ═══ 终态摘要 + 计时冻结（issue #46 反馈：完成后「已用时间」仍在走）═══

    /// <summary>Reflection 读取私有字段 `_finalElapsed`：判断计时是否已冻结（唯一直接证据，文本断言测不出 1s 内差）。</summary>
    private static TimeSpan? ReadFinalElapsed(ProgressViewModel vm)
    {
        var field = typeof(ProgressViewModel)
            .GetField("_finalElapsed", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (TimeSpan?)field!.GetValue(vm);
    }

    [Fact]
    public void SetComplete_FreezesElapsedAndBuildsSummary()
    {
        var vm = new ProgressViewModel();
        vm.SetProgress(new ArchiveProgress
        {
            TotalBytes = 4 * 1024 * 1024,
            ProcessedBytes = 4 * 1024 * 1024,
            TotalFiles = 3,
            ProcessedFiles = 3,
            PercentComplete = 100,
        });
        vm.RefreshTimeDisplay();
        Assert.Null(ReadFinalElapsed(vm)); // 进行中：未冻结

        vm.SetComplete("done");

        Assert.NotNull(ReadFinalElapsed(vm)); // 完成后：计时基线冻结
        Assert.True(vm.HasCompletionSummary);
        var lines = vm.CompletionSummaryText.Split(Environment.NewLine);
        Assert.True(lines.Length >= 3, $"摘要至少应含 文件数/压缩包数/用时 三行，实际 {lines.Length} 行：{vm.CompletionSummaryText}");
        Assert.Contains("3", lines[0]); // 文件数行携带计数（文案随语言，数字不变）
        // 准确值不是 0:00:00（耗时极小也不影响断言：FormatDuration 至少给出 0:00:xx 形式，这里只验稳定性）
        var frozen = vm.ElapsedText;
        vm.RefreshTimeDisplay();
        Assert.Equal(frozen, vm.ElapsedText); // 冻结后重复 tick 结果恒定
    }

    [Fact]
    public void SetErrorSummary_AlsoFreezesClock()
    {
        var vm = new ProgressViewModel();
        vm.SetErrorSummary("boom", "detail");

        Assert.NotNull(ReadFinalElapsed(vm));
        Assert.True(vm.HasCompletionSummary);
    }

    [Fact]
    public void NewOperation_ClearsFrozenTerminalState()
    {
        var vm = new ProgressViewModel();
        vm.SetComplete("done");
        Assert.True(vm.HasCompletionSummary);
        Assert.NotNull(ReadFinalElapsed(vm));

        // 同一 VM 复用：新进度到达即视为新操作 → 冻结值与摘要清空
        vm.SetProgress(new ArchiveProgress { PercentComplete = 5, TotalBytes = 100, ProcessedBytes = 5 });

        Assert.Null(ReadFinalElapsed(vm));
        Assert.False(vm.HasCompletionSummary);
        Assert.Equal(string.Empty, vm.CompletionSummaryText);
    }

    [Fact]
    public void BatchPartialFailure_SummaryShowsSuccessOverTotal()
    {
        var vm = new ProgressViewModel();
        vm.InitBatchMode(new[] { "a.zip", "b.zip" });
        vm.SetCurrentBatchItem(0);
        vm.SetProgress(new ArchiveProgress { TotalBytes = 1024, ProcessedBytes = 1024, TotalFiles = 1, ProcessedFiles = 1 });
        vm.UpdateBatchItemStatus(0, BatchItemStatus.Completed);
        vm.SetCurrentBatchItem(1);
        vm.UpdateBatchItemStatus(1, BatchItemStatus.Failed, "boom");
        vm.FinalizeBatch();
        vm.CompleteWithErrors();

        Assert.True(vm.HasCompletionSummary);
        // 1 成功 / 2 共 —— 部分失败文案里必须出现该比例（两侧数字与分隔符与语言无关）
        Assert.Contains("1 / 2", vm.CompletionSummaryText);
    }
}
