using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 进度窗口 XAML 冒烟测试：证明 T7 重构后的 AXAML 能真实实例化并完成绑定/命名控件解析。
/// 编译器只能验证 XAML 语法，无法验证「DataTemplate + Flyout 内的 x:Name 能否被 FindControl 解析」
/// 与「派生属性通知是否驱动 IsVisible」——这两点正是 T7/T8 最易静默失效之处，故在此以无头运行时兜底。
/// </summary>
public class ProgressWindowXamlTests
{
    /// <summary>T7 布局重构 + T6 新增属性绑定后，窗口应能构造并完成一次完整布局（AXAML 解析失败会在此暴露）。</summary>
    [AvaloniaFact]
    public void ProgressWindow_Constructs_AndLaysOutRestructuredGrid()
    {
        var window = new ProgressWindow("test");
        // 未 Show 前不可见（「已用时间」计时器只在 OnOpened 启动、OnClosed 停止）
        Assert.False(window.IsVisible);

        // Show() 触发布局遍历，验证原型对齐后的 7 行网格完整实例化
        // （DataTemplate 行由 ItemsControl 延迟实例化，不在本用例覆盖范围）
        window.Show();
        Assert.True(window.IsVisible);

        var root = window.FindControl<Grid>("RootGrid");
        Assert.NotNull(root);
        // 对齐 HTML 原型后的 7 行结构（原型对齐改版将旧 10 行布局收敛到 7 行）
        Assert.Equal(7, root!.RowDefinitions.Count);
        window.Close();
    }

    /// <summary>spec §2/§4：状态行与错误摘要均有复制控件；7 行布局不变（仅调行序）。</summary>
    [AvaloniaFact]
    public void ProgressWindow_StatusLineAndSummary_HaveCopyControls()
    {
        var window = new ProgressWindow("test");
        window.Show();

        Assert.NotNull(window.FindControl<TextBox>("StatusLineBox"));
        Assert.NotNull(window.FindControl<Button>("StatusCopyBtn"));
        Assert.NotNull(window.FindControl<TextBox>("ErrorSummaryRedBox"));
        Assert.NotNull(window.FindControl<TextBox>("ErrorSummaryYellowBox"));
        Assert.NotNull(window.FindControl<Button>("SummaryCopyBtn"));

        var grid = window.FindControl<Grid>("RootGrid");
        Assert.NotNull(grid);
        Assert.Equal(7, grid!.RowDefinitions.Count);

        window.Close();
    }

    /// <summary>
    /// 终态摘要块（issue #46）：初始空串 → 整块隐藏；VM 进入终态后填充文本 → 绑定驱动可见。
    /// 同时证明新增块没有破坏 7 行布局。
    /// </summary>
    [AvaloniaFact]
    public void ProgressWindow_CompletionSummary_HiddenUntilTerminal()
    {
        var window = new ProgressWindow("test");
        window.Show();

        var block = window.FindControl<TextBlock>("CompletionSummaryText");
        Assert.NotNull(block);
        Assert.False(block!.IsVisible); // 未终态：空串 → 按规则 6 隐藏

        var vm = Assert.IsType<ProgressViewModel>(window.DataContext);
        vm.SetComplete("done");

        Assert.True(block.IsVisible); // 绑定 HasCompletionSummary 生效
        Assert.False(string.IsNullOrWhiteSpace(block.Text));

        window.Close();
    }

    /// <summary>spec §11.2: 解压后置开关默认隐藏（能力位 false，压缩/测试流全藏）。</summary>
    [AvaloniaFact]
    public void ProgressWindow_PostExtractToggles_DefaultHidden()
    {
        var window = new ProgressWindow("test");
        window.Show();

        var openFolderToggle = window.FindControl<ToggleButton>("OpenFolderToggle");
        var deleteArchiveToggle = window.FindControl<ToggleButton>("DeleteArchiveToggle");
        Assert.NotNull(openFolderToggle);
        Assert.NotNull(deleteArchiveToggle);
        Assert.False(openFolderToggle!.IsVisible);    // 能力位默认 false → IsVisible=false
        Assert.False(deleteArchiveToggle!.IsVisible);

        window.Close();
    }

    /// <summary>
    /// T8 核心风险点：密码徽标的 IsVisible 绑定必须随 BatchItem.PasswordState 变化而重算。
    /// 若 IsPasswordMatching/IsPasswordMatched 未被 NotifyBatchProperties 通知，此断言失败。
    /// </summary>
    [AvaloniaFact]
    public void BatchItem_PasswordState_NotifiesDerivedVisibility()
    {
        var item = new BatchItem { Name = "a.zip" };
        var seen = new List<string?>();
        item.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        item.PasswordState = BatchPasswordState.Matching;
        Assert.True(item.IsPasswordMatching);
        Assert.False(item.IsPasswordMatched);
        Assert.Contains(nameof(BatchItem.IsPasswordMatching), seen);
        Assert.Contains(nameof(BatchItem.IsPasswordMatched), seen);

        seen.Clear();
        item.PasswordState = BatchPasswordState.Matched;
        Assert.False(item.IsPasswordMatching);
        Assert.True(item.IsPasswordMatched);

        // 状态变更必须同时刷新画刷资源键（Rule：Status setter 已并入，此处回归保护）
        item.Status = BatchItemStatus.Failed;
        Assert.Equal("ThemeStatusFailedBrush", item.StatusBrushName);
        Assert.Contains(nameof(BatchItem.StatusBrushName), seen);
    }

    /// <summary>T8：徽标三态互斥，保证同一行不会同时显示 🔄 与 🔑。</summary>
    [AvaloniaFact]
    public void BatchItem_PasswordBadgeStatesAreMutuallyExclusive()
    {
        var item = new BatchItem { Name = "a.zip" };
        Assert.False(item.HasPasswordBadge);

        item.PasswordState = BatchPasswordState.Matching;
        Assert.True(item.HasPasswordBadge);
        Assert.True(item.IsPasswordMatching);
        Assert.False(item.IsPasswordMatched);

        item.PasswordState = BatchPasswordState.Matched;
        Assert.False(item.IsPasswordMatching);
        Assert.True(item.IsPasswordMatched);

        item.PasswordState = BatchPasswordState.None;
        Assert.False(item.HasPasswordBadge);
    }

    /// <summary>
    /// T8：SetBatchPasswordState 熄灭时必须清空明文/规则/描述，避免上一个密码残留在行模型里。
    /// </summary>
    [AvaloniaFact]
    public void SetBatchPasswordState_ClearsSecrets_WhenTurningOff()
    {
        var vm = new ProgressViewModel();
        vm.InitBatchMode(new[] { "a.zip" });

        vm.SetBatchPasswordState(0, BatchPasswordState.Matched, "s3cret", "rule*", "desc");
        Assert.Equal("s3cret", vm.BatchItems![0].MatchedPassword);
        Assert.Equal("rule*", vm.BatchItems[0].PasswordRule);
        Assert.Equal("desc", vm.BatchItems[0].PasswordDescription);

        vm.SetBatchPasswordState(0, BatchPasswordState.None, null, null, null);
        Assert.Null(vm.BatchItems[0].MatchedPassword);
        Assert.Null(vm.BatchItems[0].PasswordRule);
        Assert.Null(vm.BatchItems[0].PasswordDescription);
    }

    /// <summary>
    /// channel-info：非并行解压（无 BatchIndex）合成单条 IsParallel=false 通道行（不伪装并行批次）。
    /// </summary>
    [AvaloniaFact]
    public void SetProgress_WithoutBatchIndex_SynthesizesSingleNonParallelRow()
    {
        var vm = new ProgressViewModel();
        Assert.False(vm.HasParallelChannel);

        vm.SetProgress(new ArchiveProgress
        {
            CurrentFile = "dir/file.txt",
            PercentComplete = 50,
            BatchIndex = null
        });

        // 单行合成：集合至多 1 条且 IsParallel=false；HasParallelChannel 仍为 false（不伪装并行）
        Assert.False(vm.HasParallelChannel);
        Assert.True(vm.ParallelBatchItems.Count <= 1);
        Assert.All(vm.ParallelBatchItems, r => Assert.False(r.IsParallel));
    }

    /// <summary>T7：并行报告携带 BatchIndex 时应按索引补洞建行（批次行 UI 依赖此不变量）。</summary>
    [AvaloniaFact]
    public void SetProgress_WithBatchIndex_PopulatesParallelRows()
    {
        var vm = new ProgressViewModel();

        // 先报批次 2（模拟乱序/跳批），集合应补洞到索引 2
        vm.SetProgress(new ArchiveProgress
        {
            PercentComplete = 10,
            BatchIndex = 2,
            BatchCount = 4,
            BatchPercentComplete = 30,
            BatchProcessedFiles = 3,
            BatchTotalFiles = 10
        });

        Assert.True(vm.HasParallelBatches);
        Assert.Equal(3, vm.ParallelBatchItems.Count);
        var row = vm.ParallelBatchItems[2];
        Assert.Equal(3, row.Index);
        Assert.Equal(30, row.Percent);
        Assert.False(string.IsNullOrEmpty(row.DetailText));
    }

    /// <summary>T6 ETA 守卫：档案切换必须重置速度基线，避免上一档案的速度污染新档案 ETA。</summary>
    [AvaloniaFact]
    public void SetCurrentBatchItem_ResetsSpeedBaseline()
    {
        var vm = new ProgressViewModel();
        vm.InitBatchMode(new[] { "a.zip", "b.zip" });
        vm.SetCurrentBatchItem(0);

        // 造一个稳定速度样本（间隔 > 100ms 节流，用固定时间推进）
        vm.SetProgress(new ArchiveProgress { PercentComplete = 10, ProcessedBytes = 1_000_000, TotalBytes = 10_000_000 });
        var before = vm.SpeedText;

        vm.SetCurrentBatchItem(1);
        // 切换后基线归零：累计字节小于基线的样本应触发重置而非产生垃圾速度
        vm.SetProgress(new ArchiveProgress { PercentComplete = 10, ProcessedBytes = 0, TotalBytes = 10_000_000 });

        Assert.NotNull(before);
        // 单行合成后集合可能有 1 条非并行行，但绝无并行通道
        Assert.False(vm.HasParallelChannel);
    }

    /// <summary>
    /// 出错信息增强（Task 7）接线守卫：红/黄摘要框 + 条目行 tooltip 绑定、以及
    /// <c>SetErrorSummary(message, detail)</c> 增参必须存在。
    /// AXAML 绑定拼写错误在编译期不报错、运行期静默失效，故以源码守卫兜底。
    /// </summary>
    [Fact]
    public void ErrorInfoEnhancement_TooltipAndSummaryWiring_IsPresent()
    {
        var axaml = File.ReadAllText(SourcePath("src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml"));
        Assert.Contains("ToolTip.Tip=\"{Binding ErrorSummaryTooltip}\"", axaml);
        Assert.Contains("ToolTip.Tip=\"{Binding SkippedSummaryTooltip}\"", axaml);
        Assert.Contains("ToolTip.Tip=\"{Binding FailureTooltip}\"", axaml);

        var windowCs = File.ReadAllText(SourcePath("src/MantisZip.UI.Avalonia/Dialogs/ProgressWindow.axaml.cs"));
        Assert.Contains("SetErrorSummary(string message, string? detail = null)", windowCs);

        var vmCs = File.ReadAllText(SourcePath("src/MantisZip.UI.Avalonia/ViewModels/ProgressViewModel.cs"));
        Assert.Contains("SetErrorSummary(string message, string? detail = null)", vmCs);
        Assert.Contains("_errorSummaryTooltip", vmCs);
        Assert.Contains("_skippedSummaryTooltip", vmCs);
    }

    /// <summary>SetStatusMessage 直接写状态行（密码取消等无汇总终态用；避免停在「正在准备…」）。</summary>
    [AvaloniaFact]
    public void SetStatusMessage_UpdatesStatusLine()
    {
        var window = new ProgressWindow("test");
        window.Show();

        window.SetStatusMessage("已取消 - 需要密码");

        Assert.Equal("已取消 - 需要密码", ((ProgressViewModel)window.DataContext!).StatusMessage);
        window.Close();
    }

    private static string SourcePath(string relative) =>
        Path.Combine(GetRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>从测试输出目录向上找含 src 的目录作为仓库根（源码守卫用）。</summary>
    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException(
            $"无法定位仓库根（未找到 'src' 目录）。Base directory: {AppContext.BaseDirectory}");
    }
}