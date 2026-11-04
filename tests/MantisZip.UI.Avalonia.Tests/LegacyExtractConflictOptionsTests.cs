using Avalonia.Headless.XUnit;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using Avalonia.Headless.XUnit;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// KI-012 回归（v0.5.7）：三条「原地解压」legacy 路径
/// （<c>ExtractArchiveHere</c> / <c>ExtractArchiveToName</c> / <c>SmartExtract</c>）
/// 曾经不传 <see cref="ArchiveOptions"/> ⇒ 恒按引擎默认「静默覆盖」执行。
/// 现在它们共用 <see cref="MainWindowViewModel.CreateLegacyExtractOptions"/>：
/// 必须带默认策略 + Ask 弹窗端口 + 进度窗口的实时策略源。
/// </summary>
public class LegacyExtractConflictOptionsTests
{
    [Fact]
    public void CreateLegacyExtractOptions_WiresLivePolicyAndDialog()
    {
        var options = MainWindowViewModel.CreateLegacyExtractOptions("ask", nameEncoding: null);

        // 实时源接上了（这是「不静默覆盖」的核心：策略不再躲在请求里）
        Assert.NotNull(options.ConflictActionProvider);
        // Ask 弹窗端口接上了（没有它，Ask 会被 Core 当成「跳过」）
        Assert.NotNull(options.ConflictResolverAsync);
        // 没有可见进度窗口时回退 Ask —— 绝不静默覆盖
        Assert.Equal(FileConflictAction.Ask, options.ConflictActionProvider!());
    }

    [AvaloniaFact]
    public void CreateLegacyExtractOptions_ReadsPolicyFromVisibleProgressWindow()
    {
        var window = new ProgressWindow("test");
        window.SetConflictPolicyControl(ConflictPolicyFlow.Extract, "skip");
        window.Show();

        try
        {
            var options = MainWindowViewModel.CreateLegacyExtractOptions(
                "ask", nameEncoding: null);

            // 运行中改下拉（这里直接改初值）⇒ 实时源立刻读到，不需要重开任务
            Assert.Equal(FileConflictAction.Skip, options.ConflictActionProvider!());

            window.SyncConflictPolicyFromDialog("overwrite");
            Assert.Equal(FileConflictAction.Overwrite, options.ConflictActionProvider!());
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void CreateLegacyExtractOptions_KeepsNameEncoding()
    {
        var options = MainWindowViewModel.CreateLegacyExtractOptions(
            "skip", nameEncoding: "gbk");

        Assert.Equal("gbk", options.ReadEncoding);
        Assert.NotNull(options.ConflictActionProvider);
    }
}
