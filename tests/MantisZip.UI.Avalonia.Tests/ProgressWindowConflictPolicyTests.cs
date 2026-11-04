using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 进度窗口「冲突时:」下拉（v0.5.7，progress-window-conflict-policy）：
/// 证明能力位、选项档数与策略码映射真正接通 —— 编译只能验证 XAML 语法，
/// 「下拉改了但执行层读不到」这类死开关只有在运行期跑一遍才暴露。
/// </summary>
public class ProgressWindowConflictPolicyTests
{
    /// <summary>未调用 SetConflictPolicyControl 时整组隐藏（删除条目 / 测试压缩包等无冲突概念的流程）。</summary>
    [AvaloniaFact]
    public void ConflictPolicy_Default_HidesControl()
    {
        var window = new ProgressWindow("test");
        window.Show();

        var panel = window.FindControl<StackPanel>("ConflictPolicyPanel");
        Assert.NotNull(panel);
        Assert.False(panel!.IsVisible);

        var combo = window.FindControl<ComboBox>("ConflictPolicyCombo");
        Assert.NotNull(combo);
        Assert.Equal(0, combo!.ItemCount);

        window.Close();
    }

    /// <summary>传 None（无冲突概念的流程）同样隐藏，即使给了初值。</summary>
    [AvaloniaFact]
    public void ConflictPolicy_NoneFlow_HidesControl()
    {
        var window = new ProgressWindow("test");
        window.SetConflictPolicyControl(ConflictPolicyFlow.None, "ask");
        window.Show();

        var panel = window.FindControl<StackPanel>("ConflictPolicyPanel");
        Assert.NotNull(panel);
        Assert.False(panel!.IsVisible);
        Assert.Equal(FileConflictAction.Ask, window.ConflictPolicy);

        window.Close();
    }

    /// <summary>解压流程：6 档选项 + 初值回填 + 标签本地化非空。</summary>
    [AvaloniaFact]
    public void ConflictPolicy_ExtractFlow_ShowsSixOptionsAndInitialCode()
    {
        var window = new ProgressWindow("test");
        window.SetConflictPolicyControl(ConflictPolicyFlow.Extract, "skip");
        window.Show();

        var panel = window.FindControl<StackPanel>("ConflictPolicyPanel");
        Assert.NotNull(panel);
        Assert.True(panel!.IsVisible);

        var combo = window.FindControl<ComboBox>("ConflictPolicyCombo");
        Assert.NotNull(combo);
        // 询问 / 覆盖 / 仅覆盖较旧 / 仅覆盖较小 / 重命名 / 跳过
        Assert.Equal(6, combo!.ItemCount);
        Assert.Equal("skip", window.ConflictPolicyCode);
        Assert.Equal(FileConflictAction.Skip, window.ConflictPolicy);
        Assert.Same(combo.SelectedItem, combo.ItemsView[5]);

        var label = window.FindControl<TextBlock>("ConflictPolicyLabelText");
        Assert.NotNull(label);
        Assert.False(string.IsNullOrWhiteSpace(label!.Text));

        window.Close();
    }

    /// <summary>压缩流程：5 档选项（含「追加到已有包」，没有「仅覆盖较旧 / 较小」）。</summary>
    [AvaloniaFact]
    public void ConflictPolicy_CompressFlow_ShowsFiveOptions()
    {
        var window = new ProgressWindow("test");
        window.SetConflictPolicyControl(ConflictPolicyFlow.Compress, "add");
        window.Show();

        var combo = window.FindControl<ComboBox>("ConflictPolicyCombo");
        Assert.NotNull(combo);
        Assert.Equal(5, combo!.ItemCount);
        Assert.Equal("add", window.ConflictPolicyCode);

        window.Close();
    }

    /// <summary>弹窗勾「应用到全部」后回写下拉：策略码与 Core 动作同步更新（单一事实源 = 下拉）。</summary>
    [AvaloniaFact]
    public void ConflictPolicy_SyncFromDialog_UpdatesCodeAndAction()
    {
        var window = new ProgressWindow("test");
        window.SetConflictPolicyControl(ConflictPolicyFlow.Extract, "ask");
        window.Show();

        Assert.Equal(FileConflictAction.Ask, window.ConflictPolicy);

        window.SyncConflictPolicyFromDialog("overwrite");
        Assert.Equal("overwrite", window.ConflictPolicyCode);
        Assert.Equal(FileConflictAction.Overwrite, window.ConflictPolicy);

        var combo = window.FindControl<ComboBox>("ConflictPolicyCombo");
        Assert.NotNull(combo);
        Assert.Same(combo!.ItemsView[1], combo.SelectedItem);

        // 非法码不改变当前策略
        window.SyncConflictPolicyFromDialog("no-such-code");
        Assert.Equal("overwrite", window.ConflictPolicyCode);

        window.Close();
    }

    /// <summary>两端各自的档数与实际动作映射（解压侧含「仅覆盖较旧 / 较小」两个特殊档）。</summary>
    [AvaloniaFact]
    public void ConflictPolicy_ExtractSpecialActions_MapToCoreActions()
    {
        var window = new ProgressWindow("test");
        window.SetConflictPolicyControl(ConflictPolicyFlow.Extract, "overwrite-if-older");
        window.Show();

        Assert.Equal(FileConflictAction.OverwriteIfOlder, window.ConflictPolicy);

        window.SyncConflictPolicyFromDialog("overwrite-if-smaller");
        Assert.Equal(FileConflictAction.OverwriteIfSmaller, window.ConflictPolicy);

        window.SyncConflictPolicyFromDialog("rename");
        Assert.Equal(FileConflictAction.Rename, window.ConflictPolicy);

        window.Close();
    }
}
