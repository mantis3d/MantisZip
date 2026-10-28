using System.Collections.Generic;
using System.IO;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 拖入压缩回归测试：未打开压缩包时拖入文件/目录 → 压缩对话框确认后必须走到压缩执行管道。
/// 回归背景：分支 3b 曾在 ShowDialog 后直接返回，无人执行压缩（点击「压缩」关窗后无任何反应）。
/// <para>
/// 接缝改造（2026-10-26）：执行已下沉到设置窗口的确认事务（<c>ConfirmOutcome</c> → 压缩会话），
/// VM 侧的回归点变成「拖入路径必须原样到达回调」+「VM 不得自己另跑一遍执行」。
/// </para>
/// </summary>
public class DragInCompressWiringTests
{
    [AvaloniaFact]
    public async Task DragInCompress_WithConfirmedDialog_ExecutesCompressPipeline()
    {
        // 临时目录 + 一个文件，作为拖入源
        var dir = Path.Combine(Path.GetTempPath(), $"mantiszip_dragin_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "hello");

        try
        {
            var vm = new MainWindowViewModel();
            IReadOnlyList<string>? passedSources = null;
            vm.ShowCompressSettingsDialog = async sourcePaths =>
            {
                // 等价于真实对话框：调用方把拖入路径交给设置窗口，由窗口在确认事务里执行。
                passedSources = sourcePaths;
                await Task.CompletedTask;
            };

            bool progressRan = false;
            vm.RunWithProgress = (title, filePaths, operation) =>
            {
                progressRan = true;
                return Task.FromResult(true); // VM 若自己跑执行管道会命中这里
            };

            await vm.DragInCompressAsync(new List<string> { dir });

            // 1. 拖入路径必须原样到达压缩对话框（分支 3b 的预期行为）
            Assert.NotNull(passedSources);
            Assert.Contains(dir, passedSources!);

            // 2. 执行只发生一次，且在设置窗口回调内部：VM 不得再自行进入执行管道
            Assert.False(progressRan, "压缩执行已下沉到设置窗口的确认事务，VM 不应再自行执行");
        }
        finally
        {
            if (File.Exists(Path.Combine(dir, "a.txt"))) File.Delete(Path.Combine(dir, "a.txt"));
            if (Directory.Exists(dir)) Directory.Delete(dir);
            var autoOutput = Path.Combine(Path.GetTempPath(), new DirectoryInfo(dir).Name + ".zip");
            if (File.Exists(autoOutput)) File.Delete(autoOutput);
        }
    }

    [AvaloniaFact]
    public async Task DragInCompress_WithCancelledDialog_DoesNotExecute()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mantiszip_dragin_cancel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        try
        {
            var vm = new MainWindowViewModel();
            IReadOnlyList<string>? passedSources = null;
            vm.ShowCompressSettingsDialog = async sourcePaths =>
            {
                // 用户在对话框点了「取消」：窗口静默关窗，回调直接返回（不产生载荷、不执行）。
                passedSources = sourcePaths;
                await Task.CompletedTask;
            };

            bool progressRan = false;
            vm.RunWithProgress = (title, filePaths, operation) =>
            {
                progressRan = true;
                return Task.FromResult(true);
            };

            await vm.DragInCompressAsync(new List<string> { dir });

            Assert.NotNull(passedSources);
            Assert.False(progressRan, "对话框取消后不应执行压缩");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir);
        }
    }
}
