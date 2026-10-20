using System.Collections.Generic;
using System.IO;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 拖入压缩回归测试：未打开压缩包时拖入文件/目录 → 压缩对话框确认后必须走到压缩执行管道。
/// 回归背景：分支 3b 曾在 ShowDialog 后直接返回，无人执行压缩（点击「压缩」关窗后无任何反应）。
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
            CompressSettingsViewModel? passedToDialog = null;
            vm.ShowCompressSettingsDialog = async cvm =>
            {
                passedToDialog = cvm;
                // 等价于真实对话框「点压缩」前的守卫：等预览构建（B 数据集）就绪
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (cvm.GetPlanForExecution() == null && DateTime.UtcNow < deadline)
                    await Task.Delay(50);
                return true; // 用户在压缩对话框点了「压缩」
            };

            bool progressRan = false;
            IReadOnlyList<string>? targetPaths = null;
            vm.RunWithProgress = (title, filePaths, operation) =>
            {
                progressRan = true;
                targetPaths = filePaths;
                return Task.FromResult(true); // 不真正执行压缩，仅验证管道被调用
            };

            await vm.DragInCompressAsync(new List<string> { dir });

            // 1. 拖入路径必须预填进对话框 VM（分支 3b 的预期行为）
            Assert.NotNull(passedToDialog);
            Assert.Contains(dir, passedToDialog!.SelectedPaths);

            // 2. 对话框确认后必须执行压缩管道（进度窗口回调被调用，请求非空）
            Assert.True(progressRan, "对话框确认后应进入压缩执行管道（RunWithProgress）");
            Assert.NotNull(vm.PendingCompressRequest);
            Assert.NotEmpty(targetPaths!);
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
            vm.ShowCompressSettingsDialog = async cvm =>
            {
                await Task.CompletedTask;
                return false; // 用户点了「取消」
            };

            bool progressRan = false;
            vm.RunWithProgress = (title, filePaths, operation) =>
            {
                progressRan = true;
                return Task.FromResult(true);
            };

            await vm.DragInCompressAsync(new List<string> { dir });

            Assert.False(progressRan, "对话框取消后不应执行压缩");
            Assert.Null(vm.PendingCompressRequest);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir);
        }
    }
}
