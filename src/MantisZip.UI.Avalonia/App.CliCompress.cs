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
        // CLI 宿主差异：没有属主窗口（Owner = null）、进度窗口本身就是终端窗口，
        // 因此用 CLI 呈现器；退出进程由本方法在拿到结局后显式收口。
        var presenter = new CliProgressPresenter();
        var session = new CompressSession();
        var context = new CompressRunContext(
            Owner: null,
            Presenter: presenter,
            ExitPolicy: ExitPolicy.None,
            IsCompressFlow: true);

        var dialog = new CompressSettingsWindow(session, context, paths);
        Services.NativeSplash.Hook(dialog); // startup-native-splash：终端窗口出现即关 splash

        // CLI 模式没有主窗口，无法用 ShowDialog(owner)，改用非模态 Show + Completion 等待结果。
        // ShutdownMode 已由 HandleCompress 改为 OnExplicitShutdown：默认 OnLastWindowClose 会在
        // 弹窗（唯一窗口）关闭瞬间同步触发应用退出，导致确认后的压缩 continuation 来不及执行。
        dialog.Show();
        desktop.MainWindow = dialog;

        var confirmation = await dialog.Completion;
        if (confirmation is not Confirmed<CompressSettings> confirmed)
        {
            // 取消 / 用户点 X：静默退出（对齐旧 Closed 事件「未开始压缩即退出」的语义）
            AppShutdown.Request(desktop.Shutdown);
            return;
        }

        var outcome = await dialog.RunConfirmedAsync(confirmed.Value);
        if (outcome.IsNotStarted)
        {
            // 前置不成立（如过滤器把条目全裁掉）没有进度窗可承载原因：必须显式提示再退出。
            await AppMessageBox.Show(outcome.Reason, LocalizationManager.T("Compress_Title"),
                MessageBoxButton.OK, MessageBoxImage.Warning, dialog);
        }

        // 成功/取消/失败都要显式退出：CLI 隐藏主窗口后无人再关进程，挂后台等于永不返回。
        AppShutdown.Request(desktop.Shutdown);
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
            // Phase 2 / Task 10：自适应三开关 + 覆盖规则 + 用户自定义格式（CLI 与 GUI 同口径）
            // 注意：只传规则而不传 AdaptiveCompression 时 ResolveLevel 会直接返回全局级别，规则等于没生效。
            AdaptiveCompression = settings.AdaptiveCompression,
            AdaptiveSmartDetect = settings.AdaptiveSmartDetect,
            MultiThreadedCompression = settings.MultiThreadedCompression,
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
            // Phase 2 / Task 10：自适应三开关 + 覆盖规则 + 用户自定义格式（CLI 与 GUI 同口径）
            // 注意：只传规则而不传 AdaptiveCompression 时 ResolveLevel 会直接返回全局级别，规则等于没生效。
            AdaptiveCompression = settings.AdaptiveCompression,
            AdaptiveSmartDetect = settings.AdaptiveSmartDetect,
            MultiThreadedCompression = settings.MultiThreadedCompression,
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
            // Phase 2 / Task 10：自适应三开关 + 覆盖规则 + 用户自定义格式（CLI 与 GUI 同口径）
            // 注意：只传规则而不传 AdaptiveCompression 时 ResolveLevel 会直接返回全局级别，规则等于没生效。
            AdaptiveCompression = settings.AdaptiveCompression,
            AdaptiveSmartDetect = settings.AdaptiveSmartDetect,
            MultiThreadedCompression = settings.MultiThreadedCompression,
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

        // 窗口生命周期交给 CLI 呈现器：已有的收集窗不重复 Show，操作放线程池执行；
        // 本方法只保留「部分失败怎么呈现」「路径历史何时写」「进程何时退」这三个业务决策。
        var presenter = new CliProgressPresenter(
            existingWindow,
            window => desktop.MainWindow = window,
            runOperationOffUiThread: true);

        int failedCount = 0;
        try
        {
            await presenter.RunAsync(new ProgressRunRequest
            {
                Title = title,
                // 始终显示文件列表（单文件也显示）：批处理列表项 = 输出路径
                DisplayPaths = AvaloniaCompressService.GetOutputPaths(request),
                ConfigureWindow = window =>
                {
                    // Task 6: 压缩流程标记 → 详细面板显示「压缩通道」说明行（Store 类文件不占通道）
                    window.IsCompressFlow = true;
                    // 列表播种：压缩流程枚举全量条目（后台；失败/取消/超阈值放弃，终态 upsert 兜底）
                    CompressFlow.TrySeedEntryItemsInBackground(window, request, window.CancellationToken);
                },
                Operation = async (progress, token) =>
                {
                    var result = await new AvaloniaCompressService().CompressAsync(
                        request,
                        progress,
                        token,
                        // 冲突处理统一走 CompressFlow（弹窗 + ApplyToAll 记忆），与主窗口共用
                        conflictResolver: CompressFlow.CreateResolver(
                            info => CompressFlow.ShowConflictDialogAsync(presenter.ActiveWindow!, info),
                            request.ConflictAction),
                        onItemStatus: (index, status) =>
                        {
                            // 逐项状态更新驱动批处理文件列表（对齐 WPF CompressAsync onItemStatus 接线）
                            if (presenter.ActiveWindow is { } window)
                            {
                                window.SetCurrentBatchItem(index);
                                window.UpdateBatchItemStatus(index, status);
                            }
                        },
                        onItemOutputPath: (index, outputPath) =>
                        {
                            // 冲突解决后（自动重命名/手输包名）把列表里的旧包名换成实际包名
                            presenter.ActiveWindow?.UpdateBatchItemName(index, outputPath);
                        });
                    failedCount = result.Failed;
                },
                OnCompleted = window =>
                {
                    if (failedCount > 0)
                    {
                        // 部分条目失败（如权限不足）：错误汇总 + 压钉等用户手动关闭
                        window.SetErrorSummary(
                            LocalizationManager.T("Progress_WriteFailed_Fmt", failedCount, request.OutputPath));
                        window.CompleteWithErrors();
                        return;
                    }

                    // 成功：压缩完成后把输出目录写入路径历史（**在自动关闭倒计时之前**，与原实现同刻）
                    CompressFlow.RecordOutputHistory(request);
                    window.SetComplete(LocalizationManager.T("Cli_StatusDone"));
                },
            });
        }
        catch (Exception ex)
        {
            // 呈现器已把错误摘要写进窗口并等用户关闭，这里只记录并退出（对齐旧 CLI 的 stderr 通道）
            Console.Error.WriteLine($"Compression failed: {ex.Message}");
        }

        // 成功/取消/失败/前置不成立都要显式退出进程（CLI 无主窗口兜底关闭）
        AppShutdown.Request(desktop.Shutdown);
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
