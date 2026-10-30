using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using MantisZip.Core.Services;
using MantisZip.UI.Avalonia.Dialogs;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 自动更新检测的 UI 回归测试（无头 Avalonia）。
/// 覆盖三件容易悄悄坏掉的事：①「关于」窗口真的有第 5 个「更新」页签；
/// ②更新提示窗的 Completion 契约在「用户直接关窗」时也必须给出结果（否则调用方永久挂起）；
/// ③提示窗必须有公共无参构造（AVLN3000：x:Class 类型缺无参构造在增量构建下会被掩盖）。
/// </summary>
public class UpdateUiTests
{
    /// <summary>「关于」窗口页签数应为 5（关于 / 作者 / 依赖 / 致谢 / 更新），且第 5 个页签标题走本地化。</summary>
    [AvaloniaFact]
    public void AboutWindow_HasFiveTabs_IncludingUpdatesTab()
    {
        var window = new AboutWindow();

        window.Show();

        var tabControl = window.GetVisualDescendants().OfType<TabControl>().Single();
        Assert.Equal(5, tabControl.ItemCount);

        var tabs = tabControl.Items.OfType<TabItem>().ToList();
        Assert.Equal(5, tabs.Count);
        Assert.False(string.IsNullOrWhiteSpace(tabs[4].Header as string));

        window.Close();
    }

    /// <summary>
    /// 更新提示窗：未点任何按钮直接关闭时，Completion 必须立即完成且结果为「稍后提醒」，
    /// 保证启动检查的 await 不会永久挂起。
    /// </summary>
    [AvaloniaFact]
    public async Task UpdateAvailableDialog_ClosingWithoutChoice_ResolvesRemindLater()
    {
        var info = new UpdateInfo(
            LatestVersion: "9.9.9",
            DownloadUrl: "https://example.invalid/releases/latest",
            ReleaseNotesUrl: "https://example.invalid/releases/latest",
            ReleaseDate: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        var dialog = new UpdateAvailableDialog(info);

        dialog.Show();
        Assert.False(dialog.Completion.IsCompleted);

        dialog.Close();

        var choice = await dialog.Completion;
        Assert.Equal(UpdatePromptChoice.RemindLater, choice);
    }

    /// <summary>
    /// 提示窗必须能用无参构造创建（AVLN3000 约束），且此时 Completion 同样在关窗后给出结果。
    /// </summary>
    [AvaloniaFact]
    public async Task UpdateAvailableDialog_DefaultConstructor_IsUsable()
    {
        var dialog = new UpdateAvailableDialog();

        dialog.Show();
        dialog.Close();

        Assert.Equal(UpdatePromptChoice.RemindLater, await dialog.Completion);
    }
}
