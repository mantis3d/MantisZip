using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 进度窗口「钉住（📌）」相关设置与行为。
///
/// 背景：设置页原本有一个 `CloseAfterCompress` 勾选框（「压缩完成后关闭对话框」），
/// 从 v0.2.0 引入起就只存不读——真正的关窗行为由 `AutoCloseDelaySeconds` + 📌 决定。
/// 该勾选框已删除，改为「外观 → 进度窗口」子页签里的「默认钉住」设置；
/// 同时修掉「手动等待中解钉不恢复倒计时」的行为缺口（D11：按钮状态 = 实际行为）。
/// </summary>
public class ProgressWindowPinTests
{
    /// <summary>死开关的字段必须彻底消失，否则又会出现「存了不生效」的配置项。</summary>
    [Fact]
    public void AppSettings_NoLongerExposesCloseAfterCompress()
    {
        var prop = typeof(AppSettings).GetProperty("CloseAfterCompress", BindingFlags.Public | BindingFlags.Instance);
        Assert.Null(prop);
    }

    /// <summary>新开关存在于设置模型，且默认不钉住（保持既有的倒计时自动关窗体验）。</summary>
    [Fact]
    public void AppSettings_ProgressPinByDefault_DefaultsToFalse()
    {
        var prop = typeof(AppSettings).GetProperty("ProgressPinByDefault", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(prop);
        var value = new AppSettings().ProgressPinByDefault;
        Assert.False(value);
    }

    /// <summary>VM 自身不读设置（保持 `new ProgressViewModel()` 的单测确定性），默认不钉住。</summary>
    [Fact]
    public void ProgressViewModel_KeepOpenOnComplete_DefaultsToFalse()
    {
        var vm = new ProgressViewModel();
        Assert.False(vm.KeepOpenOnComplete);
    }

    /// <summary>压钉时倒计时组必须立即隐藏（与既有 OnKeepOpenOnCompleteChanged 契约一致）。</summary>
    [Fact]
    public void ProgressViewModel_Pinning_HidesCountdown()
    {
        var vm = new ProgressViewModel { AutoCloseCountdownVisible = true };
        vm.KeepOpenOnComplete = true;
        Assert.False(vm.AutoCloseCountdownVisible);
    }

    /// <summary>窗口构造时把设置的「默认钉住」写进 VM（设置 → 行为贯通的唯一入口）。</summary>
    [AvaloniaFact]
    public void ProgressWindow_AppliesDefaultPinFromSettings()
    {
        var expected = AppSettings.Load().ProgressPinByDefault;
        var window = new ProgressWindow("test");
        var vm = Assert.IsType<ProgressViewModel>(window.DataContext);
        Assert.Equal(expected, vm.KeepOpenOnComplete);
        window.Close();
    }

    /// <summary>
    /// 核心回归：钉住态下解钉必须恢复倒计时并最终关窗。
    /// 旧实现的 `WaitForManualCloseAsync` 只等 Closed 事件，解钉后仍无限期干等（按钮状态与实际行为脱节）。
    /// </summary>
    [AvaloniaFact]
    public async Task UnpinningDuringManualWait_ResumesCountdown_AndCloses()
    {
        var window = new ProgressWindow("test");
        window.Show();
        var vm = Assert.IsType<ProgressViewModel>(window.DataContext);

        // 钉住 = 完成后进手动等待；把剩余毫秒调小，解钉后 0.3s 内就该关（不依赖真实设置里的 3 秒）
        vm.KeepOpenOnComplete = true;
        var field = typeof(ProgressWindow).GetField(
            "_countdownRemainingMs", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field.SetValue(window, 300);

        var closed = new TaskCompletionSource();
        var wait = window.AutoCloseOrWaitAsync(() => closed.TrySetResult());

        // 让循环进入手动等待：此时不应关窗
        await Task.Delay(150);
        Assert.False(closed.Task.IsCompleted);

        // 解钉 → 恢复倒计时 → 到点关窗
        vm.KeepOpenOnComplete = false;
        var finished = await Task.WhenAny(closed.Task, Task.Delay(4000));
        Assert.Same(closed.Task, finished);
        Assert.True(closed.Task.IsCompleted, "解钉后应立即恢复倒计时并关窗");

        window.Close();
        await wait;
    }

    /// <summary>
    /// 回归（issue #66「新建压缩包按钮用一次就永久灰」）：
    /// 钉住等待期间窗口「不可见了但没走正常关闭事件」时，不能永久等待。
    ///
    /// 旧实现是「先查 IsVisible → 再订阅 Closed → 阻塞等信号量」：事件若在订阅之前已经发过
    /// （或窗口被宿主直接收起而不触发 Closed），等待永不返回 ⇒ 呈现器 → 会话 → 命令整条链卡住
    /// ⇒ 按钮永久灰化，只能重启。新实现先订阅再复查 + 250ms 心跳兜底，最迟一个心跳周期内返回。
    /// </summary>
    [AvaloniaFact]
    public async Task ManualWait_ReturnsWhenWindowBecomesInvisible()
    {
        var window = new ProgressWindow("test");
        window.Show();
        var vm = Assert.IsType<ProgressViewModel>(window.DataContext);
        vm.KeepOpenOnComplete = true;

        var closed = new TaskCompletionSource();
        var wait = window.AutoCloseOrWaitAsync(() => closed.TrySetResult());

        // 已进入手动等待：窗口仍可见，不应关窗
        await Task.Delay(150);
        Assert.False(closed.Task.IsCompleted);

        // 模拟「窗口已不可见但没有触发 Closed」（关闭事件错过 / 宿主收起窗口）
        window.Hide();

        var finished = await Task.WhenAny(closed.Task, Task.Delay(4000));
        Assert.Same(closed.Task, finished);
        Assert.True(closed.Task.IsCompleted, "窗口不可见后应在一个心跳周期内结束等待并收尾");

        await wait;
        window.Close();
    }

    /// <summary>
    /// 设置窗口源码守卫：死开关控件已删除，「进度窗口」子页签与「默认钉住」绑定已就位。
    /// （XAML 里删了控件却留下绑定属性/文案，编译期不会报错，只能靠文本守卫兜住。）
    /// </summary>
    [Fact]
    public void SettingsWindowXaml_NoCloseAfterCompress_AndHasProgressSubTab()
    {
        var xaml = ReadRepoFile(Path.Combine(
            "src", "MantisZip.UI.Avalonia", "Views", "SettingsWindow.axaml"));

        Assert.DoesNotContain("CloseAfterCompress", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings_Compress_CloseAfterDone", xaml, StringComparison.Ordinal);
        // 两个子页签标题 + 新开关 + 延时说明都必须绑定到 VM
        Assert.Contains("TabAppearanceGeneralHeader", xaml, StringComparison.Ordinal);
        Assert.Contains("TabAppearanceProgressHeader", xaml, StringComparison.Ordinal);
        Assert.Contains("ProgressPinByDefault", xaml, StringComparison.Ordinal);
        Assert.Contains("AppearanceAutoCloseDelayHelpText", xaml, StringComparison.Ordinal);
    }

    /// <summary>三语 key 集必须同步（新增 4 个 / 删除 1 个后仍三份一致）。</summary>
    [Fact]
    public void Localization_HasProgressWindowKeysInAllLanguages()
    {
        foreach (var lang in new[] { "zh-CN", "en", "zh-TW" })
        {
            var json = ReadRepoFile(Path.Combine(
                "src", "MantisZip.UI.Avalonia", "Localization", $"strings.{lang}.json"));
            Assert.Contains("Settings_Tab_Appearance_Progress", json, StringComparison.Ordinal);
            Assert.Contains("Settings_Progress_PinByDefault", json, StringComparison.Ordinal);
            Assert.Contains("Settings_Progress_PinByDefault_Help", json, StringComparison.Ordinal);
            Assert.Contains("Settings_Progress_AutoCloseDelay_Help", json, StringComparison.Ordinal);
            Assert.DoesNotContain("Settings_Compress_CloseAfterDone", json, StringComparison.Ordinal);
        }
    }

    // ── helpers ──

    /// <summary>从测试可执行目录向上找到仓库根（含 src 与 tests 两级目录）。</summary>
    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(Directory.Exists(Path.Combine(dir.FullName, "src"))
                                      && Directory.Exists(Path.Combine(dir.FullName, "tests"))))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, relativePath);
        Assert.True(File.Exists(path), $"找不到文件：{path}");
        return File.ReadAllText(path);
    }
}
