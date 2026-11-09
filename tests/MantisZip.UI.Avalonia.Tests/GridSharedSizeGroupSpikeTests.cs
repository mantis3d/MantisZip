using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// T3 前置 spike（thumbnail-card-badge-and-row-bars 风险 #2）结论锚：
/// Avalonia 12 的 <c>ColumnDefinition.SharedSizeGroup</c> **不跨 Grid 共享列宽**
///（实测 2026-11-07：同一 StackPanel 下两个 Grid，长标签列 51px vs 短标签列 27px，
///「修改日期:」「大小:」没有对齐——Avalonia 无 WPF 的 SharedSizeScope 概念）。
/// ⇒ Content 模式元数据行标签列对齐按计划兜底方案执行：**标签列定宽常量**
///（行与假列头两侧用同一个宽度键，对齐精度由常量一致性保证）。
/// 本文件保留对照组用例，防止未来 Avalonia 升级改变行为后无人察觉。
/// </summary>
public class GridSharedSizeGroupSpikeTests
{
    [AvaloniaFact]
    public void SharedSizeGroup_Does_NOT_ShareWidth_AcrossSiblingGrids()
    {
        var defs1 = new ColumnDefinitions("Auto") { [0] = { SharedSizeGroup = "LabelColumn" } };
        var grid1 = new Grid { ColumnDefinitions = defs1 };
        grid1.Children.Add(new TextBlock { Text = "修改日期:" });   // 长标签

        var defs2 = new ColumnDefinitions("Auto") { [0] = { SharedSizeGroup = "LabelColumn" } };
        var grid2 = new Grid { ColumnDefinitions = defs2 };
        grid2.Children.Add(new TextBlock { Text = "大小:" });      // 短标签（同组）

        var panel = new StackPanel { Children = { grid1, grid2 } };
        var window = new Window { Width = 480, Height = 120, Content = panel };
        window.Show();
        for (var i = 0; i < 6; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(480, 120));
            window.Arrange(new Rect(0, 0, 480, 120));
            window.UpdateLayout();
        }

        // 钉住「不共享」的现状：若未来升级开始共享，这两列宽度会相等 → 用例报警，届时可切回主案
        Assert.True(grid2.ColumnDefinitions[0].ActualWidth < grid1.ColumnDefinitions[0].ActualWidth);
    }
}
