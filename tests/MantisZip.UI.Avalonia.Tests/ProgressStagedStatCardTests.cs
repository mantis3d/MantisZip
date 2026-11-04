using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 「暂存」统计卡（v0.5.6 后追加）：两段式批量解压解码期间，条目已解到暂存目录但还没落到最终位置，
/// 「已处理」一直是 0，用户看不到任何推进。这里锁住 Core 上报 <see cref="ArchiveProgress.StagedFiles"/>
/// 之后窗口确实多出一张卡、数字与大小跟着进度走，并且旧上报（不带该字段）不会把卡片抖没。
/// </summary>
public class ProgressStagedStatCardTests
{
    [AvaloniaFact]
    public void StagedReport_ShowsStagedCard_WithCountAndSize()
    {
        var window = new ProgressWindow("test");
        window.Show();
        var vm = Assert.IsType<ProgressViewModel>(window.DataContext);

        Assert.False(vm.HasStatsStaged);

        vm.SetProgress(new ArchiveProgress
        {
            CurrentFile = "part-00.bin",
            PercentComplete = 25,
            TotalFiles = 4,
            ProcessedFiles = 0,
            StagedFiles = 2,
            StagedBytes = 3L * 1024 * 1024,
        });

        Assert.True(vm.HasStatsStaged);
        Assert.Equal("2", vm.StatsStagedCount);
        Assert.False(string.IsNullOrWhiteSpace(vm.StatsStagedSize));
        Assert.False(string.IsNullOrWhiteSpace(vm.StatsStagedLabel));

        window.Close();
    }

    /// <summary>不带该字段的上报（逐条目事件、收尾 100% 等）不得把卡片隐藏或清零，避免数字来回闪。</summary>
    [AvaloniaFact]
    public void ReportWithoutStagedField_KeepsLastStagedValues()
    {
        var window = new ProgressWindow("test");
        window.Show();
        var vm = Assert.IsType<ProgressViewModel>(window.DataContext);

        vm.SetProgress(new ArchiveProgress { PercentComplete = 40, StagedFiles = 3, StagedBytes = 6L * 1024 * 1024 });
        Assert.Equal("3", vm.StatsStagedCount);

        vm.SetProgress(new ArchiveProgress { PercentComplete = 50 });
        Assert.True(vm.HasStatsStaged);
        Assert.Equal("3", vm.StatsStagedCount);

        window.Close();
    }

    /// <summary>「暂存」只在两段式批量解压里出现；逐条目路径不带该字段时应保持隐藏。</summary>
    [AvaloniaFact]
    public void FreshWindow_HidesStagedCard()
    {
        var window = new ProgressWindow("test");
        window.Show();
        var vm = Assert.IsType<ProgressViewModel>(window.DataContext);

        Assert.False(vm.HasStatsStaged);

        // 逐条目路径的常规上报（不带 StagedFiles）不应把卡片翻出来
        vm.SetProgress(new ArchiveProgress { PercentComplete = 10, TotalFiles = 3, ProcessedFiles = 1 });
        Assert.False(vm.HasStatsStaged);

        window.Close();
    }
}
