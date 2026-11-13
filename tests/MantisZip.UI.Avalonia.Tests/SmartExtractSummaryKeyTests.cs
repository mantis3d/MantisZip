using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 智能解压完成后状态栏文案的映射回归。
/// <para>
/// 文案必须按**实际发生的动作**选，不能按包结构选：旧实现按 <c>hasSingleRoot</c> 二分，
/// 给单根包配的却是「已剥离根文件夹」—— 而那段代码从来没剥过任何东西，
/// 用户会以为解出来的层数与上一版不同。
/// </para>
/// </summary>
public class SmartExtractSummaryKeyTests
{
    [Fact]
    public void AddedRoot_ReportsAddedRoot()
        => Assert.Equal("Status_SmartExtractAddedRoot",
            MainWindowViewModel.SmartExtractSummaryKey(stripRoot: null, addRoot: "backup"));

    [Fact]
    public void StrippedRoot_ReportsStrippedRoot()
        => Assert.Equal("Status_SmartExtractStrippedRoot",
            MainWindowViewModel.SmartExtractSummaryKey(stripRoot: "photos", addRoot: null));

    [Fact]
    public void NoStructuralChange_ReportsUnchanged()
        => Assert.Equal("Status_SmartExtractUnchanged",
            MainWindowViewModel.SmartExtractSummaryKey(stripRoot: null, addRoot: null));

    [Fact]
    public void BothGiven_PrefersAddedRoot()
        => Assert.Equal("Status_SmartExtractAddedRoot",
            MainWindowViewModel.SmartExtractSummaryKey(stripRoot: "photos", addRoot: "backup"));
}
