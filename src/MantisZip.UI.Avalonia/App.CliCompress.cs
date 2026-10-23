using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Views;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace MantisZip.UI.Avalonia;

/// <summary>
/// App 的 CLI 压缩族部分（自 App.axaml.cs 拆分，纯移动）。
/// </summary>
public partial class App : Application
{
    // ════════════════════════════════════════════════════════════════
    //  --compress : IPC + CompressSettingsWindow
    // ════════════════════════════════════════════════════════════════

    private static void HandleCompress(List<string> paths, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (myPaths.Count == 0) { AppShutdown.Request(desktop.Shutdown); return; }

        // 显式管理退出：对话框关闭时默认 ShutdownMode(OnLastWindowClose) 会立即退出进程，
        // 但压缩流程需要在对话框关闭后继续显示 ProgressWindow（对齐 WPF HandleCompress 的
        // app.ShutdownMode = OnExplicitShutdown 用法）
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (OperatingSystem.IsWindows())
        {
            bool firstInstance;
            var mutex = new Mutex(true, CompressMutexName, out firstInstance);

            if (firstInstance)
            {
                var allPaths = new List<string>(myPaths);
                var cts = new CancellationTokenSource();

                // 立即显示「正在收集文件」纯文字弹窗，避免 IPC 收集期间用户无反馈。
                // 不用 ProgressWindow：其按钮/进度条/批处理列表会让用户误以为压缩已开始
                var collectingWindow = new CollectingWindow();
                collectingWindow.Show();
                desktop.MainWindow = collectingWindow;

                var pipeReady = new ManualResetEventSlim(false);
                StartPipeServer(allPaths, cts.Token, CompressPipeName, pipeReady);
                pipeReady.Wait(3000);

                _ = Task.Delay(800).ContinueWith(_ =>
                {
                    cts.Cancel();
                    mutex.Dispose();
                    Dispatcher.UIThread.Post(async () =>
                    {
                        try
                        {
                            collectingWindow.Close();
                            await ShowCompressDialogAndRun(allPaths, desktop);
                        }
                        catch (Exception ex) { App.DebugLog($"HandleCompress: ShowCompressDialogAndRun 异常: {ex.Message}"); AppShutdown.Request(desktop.Shutdown); }
                    });
                });
            }
            else
            {
                SendPathsThroughPipe(myPaths, CompressPipeName);
                AppShutdown.Request(desktop.Shutdown);
            }
        }
        else
        {
            // Non-Windows: show dialog directly (no IPC)
            Dispatcher.UIThread.Post(async () =>
            {
                try { await ShowCompressDialogAndRun(myPaths, desktop); }
                catch (Exception ex) { App.DebugLog($"HandleCompress: ShowCompressDialogAndRun 异常: {ex.Message}"); AppShutdown.Request(desktop.Shutdown); }
            });
        }
    }

    private static async Task ShowCompressDialogAndRun(List<string> paths, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var dlg = new CompressSettingsWindow(paths);
        Services.NativeSplash.Hook(dlg); // startup-native-splash：终端窗口出现即关 splash

        // 压缩流程是否已接管（接管后由 CompressWithProgress 负责退出进程，这里不再退出）
        bool compressStarted = false;

        // 对话框关闭（用户点 X 或取消）→ 退出进程（对齐 WPF ShowCompressWindow 的 win.Closed += Shutdown）
        dlg.Closed += (_, _) =>
        {
            if (!compressStarted)
                AppShutdown.Request(desktop.Shutdown);
        };

        // Intercept CloseAction: on "Compress", run the compression
        dlg.ViewModel.CloseAction = async (result) =>
        {
            if (result)
            {
                // 此入口覆盖了窗口内部 CloseAction，需显式快照高级选项（仅本次压缩生效）
                dlg.SnapshotFormatOptionsToViewModel();
                var vm = dlg.ViewModel;

                // 统一构建（含文件过滤），与主窗口 ExecuteCompressFromSettings 共用 CompressFlow。
                // StartCompress 已等待 B 数据集就绪，但 BuildRequest 仍可能返回 null（如过滤后全部无匹配）：
                // 此时保持窗口打开并提示（对齐主窗口路径的 Compress_FilteredAllSkipped 提示），
                // 而不是静默关闭窗口并退出进程（原 bug：request==null → AppShutdown.Request(desktop.Shutdown) 无任何反馈）。
                var request = CompressFlow.BuildRequest(vm);
                if (request == null)
                {
                    await AppMessageBox.Show(
                        LocalizationManager.T("Compress_FilteredAllSkipped"),
                        LocalizationManager.T("Compress_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning,
                        dlg);
                    return;
                }

                compressStarted = true;
                dlg.Close();

                await CompressWithProgress(request, LocalizationManager.T("Cli_Compress"), desktop);
            }
            else
            {
                // 取消 → 关闭对话框，Closed 事件触发退出
                dlg.Close();
            }
            await Task.CompletedTask;
        };

        dlg.Show();
        desktop.MainWindow = dlg;
    }

    // ════════════════════════════════════════════════════════════════
    //  --compress-quick : defaults + ProgressWindow, then exit
    // ════════════════════════════════════════════════════════════════

    private static void HandleCompressQuick(List<string> paths, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (myPaths.Count == 0) { AppShutdown.Request(desktop.Shutdown); return; }

        // 显式管理退出：进度窗口是 CLI 模式下唯一窗口，默认 OnLastWindowClose 会在用户
        // 点击 X 时立即退出进程，后台压缩被强杀留下损坏的压缩包（对齐 WPF 的 OnExplicitShutdown 用法）
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var settings = AppSettings.Load();

        // Auto-determine output path from first source
        var first = myPaths[0];
        var dir = File.Exists(first)
            ? Path.GetDirectoryName(first)
            : Path.GetDirectoryName(first.TrimEnd('\\', '/'));
        dir ??= Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var baseName = Path.GetFileNameWithoutExtension(first.TrimEnd('\\', '/'));
        var ext = settings.DefaultFormat == "tar.gz" ? ".tar.gz" : "." + settings.DefaultFormat;
        var outputPath = Path.Combine(dir, baseName + ext);

        var request = new CompressRequest
        {
            SourcePaths = myPaths,
            Mode = CompressOutputMode.Manual,
            Format = settings.DefaultFormat,
            CompressionLevel = settings.DefaultLevel,
            OutputPath = outputPath,
            PreserveDirectoryRoot = true,
            // 文件冲突默认策略（ask = 弹窗；来自设置，仅本次压缩生效）
            ConflictAction = settings.CompressFileConflictAction,
            // 源文件读取错误（被占用等）→ 弹 ErrorDialog（重试/跳过/中止）
            ErrorResolver = CompressFlow.CreateErrorResolver(),
            // Phase 2 / Task 10：自适应覆盖规则 + 用户自定义格式（CLI 与 GUI 同口径）
            AdaptiveOverrides = new List<AdaptiveOverrideRule>(settings.AdaptiveOverrides ?? new List<AdaptiveOverrideRule>()),
            CustomFormats = new List<FormatDefinition>(settings.CustomFormats ?? new List<FormatDefinition>()),
        };

        _ = CompressWithProgress(request, LocalizationManager.T("Cli_QuickCompress"), desktop);
    }

    // ════════════════════════════════════════════════════════════════
    //  --compress-separate : IPC + per-item batch compress
    // ════════════════════════════════════════════════════════════════

    private static void HandleCompressSeparate(List<string> paths, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (myPaths.Count == 0) { AppShutdown.Request(desktop.Shutdown); return; }

        // 显式管理退出：进度窗口是 CLI 模式下唯一窗口，默认 OnLastWindowClose 会在用户
        // 点击 X 时立即退出进程，后台压缩被强杀留下损坏的压缩包（对齐 WPF 的 OnExplicitShutdown 用法）
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (OperatingSystem.IsWindows())
        {
            bool firstInstance;
            var mutex = new Mutex(true, CompressSeparateMutexName, out firstInstance);

            if (firstInstance)
            {
                var allPaths = new List<string>(myPaths);
                var cts = new CancellationTokenSource();
                var pipeReady = new ManualResetEventSlim(false);

        // 立即显示进度窗口，避免 IPC 收集期间用户无反馈（对齐 WPF HandleCompressSeparate）
        var progressWindow = new ProgressWindow(LocalizationManager.T("Progress_Batch_Title"));
        Services.NativeSplash.Hook(progressWindow); // startup-native-splash：进度窗即终端窗口
        progressWindow.InitCancellation();
        progressWindow.Show();
        desktop.MainWindow = progressWindow;
        // 收集期预填充首个实例的路径，列表始终可见
        progressWindow.InitBatchMode(myPaths);
        progressWindow.SetProgress(0, LocalizationManager.T("App_CompressCollecting"));
                // IPC 收集期间点取消 → 同步终止管道收集
                progressWindow.CancellationToken.Register(() => cts.Cancel());

                StartPipeServer(allPaths, cts.Token, CompressSeparatePipeName, pipeReady);
                pipeReady.Wait(3000);

                _ = Task.Delay(800).ContinueWith(_ =>
                {
                    cts.Cancel();
                    mutex.Dispose();
                    Dispatcher.UIThread.Post(async () =>
                    {
                        try { await RunCompressSeparate(allPaths, desktop, progressWindow); }
                        finally { AppShutdown.Request(desktop.Shutdown); }
                    });
                });
            }
            else
            {
                SendPathsThroughPipe(myPaths, CompressSeparatePipeName);
                AppShutdown.Request(desktop.Shutdown);
            }
        }
        else
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try { await RunCompressSeparate(myPaths, desktop); }
                finally { AppShutdown.Request(desktop.Shutdown); }
            });
        }
    }

    private static async Task RunCompressSeparate(
        List<string> paths,
        IClassicDesktopStyleApplicationLifetime desktop,
        ProgressWindow? existingWindow = null)
    {
        var settings = AppSettings.Load();

        var request = new CompressRequest
        {
            SourcePaths = paths,
            Mode = CompressOutputMode.Separate,
            Format = settings.DefaultFormat,
            CompressionLevel = settings.DefaultLevel,
            KeepOriginalExtension = false,
            PreserveDirectoryRoot = true,
            // 文件冲突默认策略（ask = 弹窗；来自设置，仅本次压缩生效）
            ConflictAction = settings.CompressFileConflictAction,
            // 源文件读取错误（被占用等）→ 弹 ErrorDialog（重试/跳过/中止）
            ErrorResolver = CompressFlow.CreateErrorResolver(),
            // Phase 2 / Task 10：自适应覆盖规则 + 用户自定义格式（CLI 与 GUI 同口径）
            AdaptiveOverrides = new List<AdaptiveOverrideRule>(settings.AdaptiveOverrides ?? new List<AdaptiveOverrideRule>()),
            CustomFormats = new List<FormatDefinition>(settings.CustomFormats ?? new List<FormatDefinition>()),
        };

        // 标题用批处理专用标题（InitBatchMode 不再覆盖标题，由调用方传入）
        await CompressWithProgress(
            request,
            LocalizationManager.T("Progress_Batch_Title"),
            desktop,
            existingWindow);
    }

    // ════════════════════════════════════════════════════════════════
    //  --compress-combined : IPC + single combined archive
    // ════════════════════════════════════════════════════════════════

    private static void HandleCompressCombined(List<string> paths, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (myPaths.Count == 0) { AppShutdown.Request(desktop.Shutdown); return; }

        // 显式管理退出：进度窗口是 CLI 模式下唯一窗口，默认 OnLastWindowClose 会在用户
        // 点击 X 时立即退出进程，后台压缩被强杀留下损坏的压缩包（对齐 WPF 的 OnExplicitShutdown 用法）
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (OperatingSystem.IsWindows())
        {
            bool firstInstance;
            var mutex = new Mutex(true, CompressCombinedMutexName, out firstInstance);

            if (firstInstance)
            {
                var allPaths = new List<string>(myPaths);
                var cts = new CancellationTokenSource();
                var pipeReady = new ManualResetEventSlim(false);
                StartPipeServer(allPaths, cts.Token, CompressCombinedPipeName, pipeReady);
                pipeReady.Wait(3000);

                _ = Task.Delay(800).ContinueWith(_ =>
                {
                    cts.Cancel();
                    mutex.Dispose();
                    Dispatcher.UIThread.Post(async () =>
                    {
                        try { await RunCompressCombined(allPaths, desktop); }
                        finally { AppShutdown.Request(desktop.Shutdown); }
                    });
                });
            }
            else
            {
                SendPathsThroughPipe(myPaths, CompressCombinedPipeName);
                AppShutdown.Request(desktop.Shutdown);
            }
        }
        else
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try { await RunCompressCombined(myPaths, desktop); }
                finally { AppShutdown.Request(desktop.Shutdown); }
            });
        }
    }

    private static async Task RunCompressCombined(List<string> paths, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var settings = AppSettings.Load();

        // Determine common parent for archive name
        var commonParent = FindCommonParent(paths);
        string parentDir;
        string archiveName;

        if (commonParent != null && !IsDriveRoot(commonParent))
        {
            parentDir = commonParent;
            archiveName = Path.GetFileName(commonParent.TrimEnd('\\', '/'));
        }
        else
        {
            // No common parent: use first file's directory
            var first = paths[0];
            parentDir = Path.GetDirectoryName(first.TrimEnd('\\', '/'))
                ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            archiveName = Path.GetFileNameWithoutExtension(first.TrimEnd('\\', '/'));
        }

        var ext = settings.DefaultFormat == "tar.gz" ? ".tar.gz" : "." + settings.DefaultFormat;
        var outputPath = Path.Combine(parentDir, archiveName + ext);

        var request = new CompressRequest
        {
            SourcePaths = paths,
            Mode = CompressOutputMode.Combined,
            Format = settings.DefaultFormat,
            CompressionLevel = settings.DefaultLevel,
            OutputPath = outputPath,
            PreserveDirectoryRoot = true,
            // 文件冲突默认策略（ask = 弹窗；来自设置，仅本次压缩生效）
            ConflictAction = settings.CompressFileConflictAction,
            // 源文件读取错误（被占用等）→ 弹 ErrorDialog（重试/跳过/中止）
            ErrorResolver = CompressFlow.CreateErrorResolver(),
            // Phase 2 / Task 10：自适应覆盖规则 + 用户自定义格式（CLI 与 GUI 同口径）
            AdaptiveOverrides = new List<AdaptiveOverrideRule>(settings.AdaptiveOverrides ?? new List<AdaptiveOverrideRule>()),
            CustomFormats = new List<FormatDefinition>(settings.CustomFormats ?? new List<FormatDefinition>()),
        };

        await CompressWithProgress(request, LocalizationManager.T("Cli_CombinedCompress"), desktop);
    }

    // ════════════════════════════════════════════════════════════════
    //  Shared: compress with ProgressWindow
    // ════════════════════════════════════════════════════════════════

    private static async Task CompressWithProgress(
        CompressRequest request,
        string title,
        IClassicDesktopStyleApplicationLifetime desktop,
        ProgressWindow? existingWindow = null)
    {
        // IPC 收集阶段用户已点 X 关闭窗口：不再启动压缩，直接退出（OnExplicitShutdown 下进程不会被
        // OnLastWindowClose 自动终止，必须显式 Shutdown，否则后台压缩会继续在不可见窗口上运行）
        if (existingWindow != null && !existingWindow.IsVisible)
        {
            AppShutdown.Request(desktop.Shutdown);
            return;
        }

        var progressWindow = existingWindow ?? new ProgressWindow(title);
        Services.NativeSplash.Hook(progressWindow); // startup-native-splash：进度窗即终端窗口
        // Task 6: 压缩流程标记 → 详细面板显示「压缩通道」说明行（Store 类文件不占通道）
        progressWindow.IsCompressFlow = true;
        if (existingWindow == null)
        {
            progressWindow.InitCancellation();
            progressWindow.Show();
        }
        desktop.MainWindow = progressWindow;

        // 列表播种：压缩流程枚举全量条目（后台；失败/取消/超阈值放弃，终态 upsert 兜底）
        CompressFlow.TrySeedEntryItemsInBackground(progressWindow, request, progressWindow.CancellationToken);

        // 始终显示文件列表（单文件也显示）：批处理列表项 = 输出路径
        var outputPaths = AvaloniaCompressService.GetOutputPaths(request);
        if (outputPaths is { Count: > 0 })
            progressWindow.InitBatchMode(outputPaths);

        var doneEvent = new ManualResetEventSlim(false);
        Exception? captureException = null;
        bool cancelled = false;
        bool partialFailure = false;

        _ = Task.Run(async () =>
        {
            try
            {
                var rawProgress = progressWindow.CreatePauseAwareProgress(
                    ProgressViewModel.CreateBackgroundProgress(
                        progressWindow, p => progressWindow.SetProgress(p)));

                var avCompress = new AvaloniaCompressService();
                var result = await avCompress.CompressAsync(
                    request,
                    rawProgress,
                    progressWindow.CancellationToken,
                    // 冲突处理统一走 CompressFlow（弹窗 + ApplyToAll 记忆），与主窗口共用
                    conflictResolver: CompressFlow.CreateResolver(
                        info => CompressFlow.ShowConflictDialogAsync(progressWindow, info),
                        request.ConflictAction),
                    onItemStatus: (index, status) =>
                    {
                        // 逐项状态更新驱动批处理文件列表（对齐 WPF CompressAsync onItemStatus 接线）
                        progressWindow.SetCurrentBatchItem(index);
                        progressWindow.UpdateBatchItemStatus(index, status);
                    },
                    onItemOutputPath: (index, outputPath) =>
                    {
                        // 冲突解决后（自动重命名/手输包名）把列表里的旧包名换成实际包名
                        progressWindow.UpdateBatchItemName(index, outputPath);
                    });
                progressWindow.FinalizeBatch();

                partialFailure = result.Failed > 0;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (partialFailure)
                    {
                        // 部分条目失败（如权限不足）：显示错误汇总（对齐 WPF result.Failed > 0 → CompleteWithErrors 分支）
                        progressWindow.SetErrorSummary(
                            LocalizationManager.T("Progress_WriteFailed_Fmt", result.Failed, request.OutputPath));
                        progressWindow.CompleteWithErrors();
                    }
                    else
                    {
                        progressWindow.SetComplete(LocalizationManager.T("Cli_StatusDone"));
                    }
                });
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                captureException = ex;
                Console.Error.WriteLine($"Compression failed: {ex.Message}");
            }
            finally
            {
                doneEvent.Set();
            }
        });

        // Wait for completion (non-blocking via the ongoing main loop)
        await Task.Run(() => doneEvent.Wait());

        if (cancelled)
        {
            await Dispatcher.UIThread.InvokeAsync(() => progressWindow.Close());
            AppShutdown.Request(desktop.Shutdown);
            return;
        }

        if (captureException != null)
        {
            // 失败：标记批次项为 Failed（FinalizeBatch 已把 InProgress 置为 Completed，这里覆盖为失败）
            progressWindow.UpdateBatchItemStatus(0, BatchItemStatus.Failed, captureException.Message);
            // 显示错误汇总并等待用户手动关闭窗口（对齐 WPF 失败分支 CompleteWithErrors + wait Closed）
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var (wholeErrorText, wholeErrorDetail) = WholeArchiveErrorLocalizer.ToUserText(captureException!);
                progressWindow.SetErrorSummary(wholeErrorText, wholeErrorDetail);
                progressWindow.CompleteWithErrors();
            });
            await progressWindow.AutoCloseOrWaitAsync(() => progressWindow.Close());
            AppShutdown.Request(desktop.Shutdown);
            return;
        }

        if (partialFailure)
        {
            // 部分失败：等待用户手动关闭窗口后退出
            await progressWindow.AutoCloseOrWaitAsync(() => progressWindow.Close());
            AppShutdown.Request(desktop.Shutdown);
            return;
        }

        // 成功：自动关闭（延迟来自设置 AutoCloseDelaySeconds）或等待（📌 KeepOpenOnComplete 生效），窗口关闭后退出进程
        // 压缩完成后把输出目录写入路径历史（含部分条目失败——输出已落盘；取消/异常路径不会走到此处）
        CompressFlow.RecordOutputHistory(request);
        await progressWindow.AutoCloseOrWaitAsync(() => AppShutdown.Request(desktop.Shutdown));
    }

    // ════════════════════════════════════════════════════════════════
    //  Path utilities
    // ════════════════════════════════════════════════════════════════

    internal static string? FindCommonParent(List<string> paths)
    {
        if (paths.Count == 0) return null;
        var parents = paths.Select(p =>
        {
            var trimmed = p.TrimEnd('\\', '/');
            return File.Exists(trimmed)
                ? Path.GetDirectoryName(trimmed) ?? ""
                : Path.GetDirectoryName(trimmed) ?? "";
        }).ToList();

        if (parents.Any(string.IsNullOrEmpty)) return null;

        var common = parents[0];
        for (int i = 1; i < parents.Count; i++)
        {
            while (!parents[i].StartsWith(common, StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(common);
                if (parent == null) return null;
                common = parent;
            }
        }
        return common;
    }

    internal static bool IsDriveRoot(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        return trimmed.Length == 2 && trimmed[1] == ':';
    }
}
