using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// 压缩命令处理器 — Compress 相关方法的 partial 定义
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 处理 --compress 模式。多选文件时 Windows 会为每个文件启动一个进程，
    /// 第一个进程作为收集器，后续进程通过命名管道把路径传过来后退出。
    /// </summary>
    private static void HandleCompress(string[] paths)
    {
        LogStartup($"HandleCompress: paths=[{string.Join(";", paths)}]");
        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (myPaths.Count == 0)
        {
            // 没有有效路径，还是打开压缩窗口让用户手动添加
            ShowCompressWindow(myPaths);
            return;
        }

        bool firstInstance;
        _instanceMutex = new Mutex(true, CompressMutexName, out firstInstance);

        if (firstInstance)
        {
            // 第一个实例：在后台收集其他实例的路径（非阻塞）
            var allPaths = new List<string>(myPaths);
            var cts = new CancellationTokenSource();

            // 立即显示"正在收集文件"小窗口，避免 IPC 期间用户无反馈
            var overlay = new Window
            {
                Width = 320,
                Height = 90,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(240, 248, 248, 248)),
                AllowsTransparency = true,
                ShowInTaskbar = false,
                Content = new TextBlock
                {
                    Text = L.T(L.App_CompressCollecting),
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(16),
                }
            };
            overlay.Show();

            _compressPipeReady.Reset();
            StartCompressPipeServer(allPaths, cts.Token);

            // 等待管道服务器就绪后再启动计时器，消除竞态条件窗口期
            if (!_compressPipeReady.Wait(3000))
                LogStartup("HandleCompress: WARNING pipe server did not signal ready within 3s, continuing anyway");

            // 使用 DispatcherTimer 延迟 800ms 后显示窗口，不阻塞 UI 线程
            LogStartup("HandleCompress: 启动 DispatcherTimer 800ms");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                cts.Cancel();
                overlay.Hide();
                try
                {
                    LogStartup("HandleCompress: DispatcherTimer 触发，调用 ShowCompressWindow");
                    ShowCompressWindow(allPaths);
                }
                catch (Exception ex)
                {
                    LogStartup($"HandleCompress: DispatcherTimer 回调异常: {ex.Message}");
                    AppMessageBox.Show(L.TF(L.App_CompressWindowFailed, ex.Message), L.T(L.App_StartupErrorTitle),
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Current.Shutdown();
                }
                finally
                {
                    overlay.Close();
                }
            };
            timer.Start();
        }
        else
        {
            // 后续实例：把路径传给第一个实例后退出
            SendPathsToFirstInstance(myPaths);
            Current.Shutdown();
        }
    }
    private static void HandleCompressSeparate(string[] paths)
    {
        LogStartup($"HandleCompressSeparate: entered with {paths.Length} CLI args");
        var app = Current;
        if (app == null) return;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        LogStartup($"HandleCompressSeparate: after filter, {myPaths.Count} valid paths out of {paths.Length}");
        if (myPaths.Count == 0)
        {
            LogStartup("HandleCompressSeparate: no valid paths, shutting down");
            app.Shutdown();
            return;
        }

        bool firstInstance;
        var mutex = new Mutex(true, CompressSeparateMutexName, out firstInstance);
        LogStartup($"HandleCompressSeparate: firstInstance={firstInstance}");

        if (firstInstance)
        {
            var allPaths = new List<string>(myPaths);
            var cts = new CancellationTokenSource();

            // 立即显示进度窗口，避免 IPC 收集期间用户无反馈
            var progressWindow = new ProgressWindow();
            progressWindow.InitCancellation();
            progressWindow.Show();
            app.MainWindow = progressWindow;
            progressWindow.SetProgress(0, L.T(L.App_CompressCollecting));
            // IPC 收集期间取消窗口 → 同步终止管道
            progressWindow.CancellationToken.Register(() => cts.Cancel());

            _compressSeparatePipeReady.Reset();
            StartPipeServer(allPaths, cts.Token, CompressSeparatePipeName, _compressSeparatePipeReady);

            if (!_compressSeparatePipeReady.Wait(3000))
                LogStartup("HandleCompressSeparate: WARNING pipe server did not signal ready within 3s");

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                cts.Cancel();
                mutex.Dispose();
                LogStartup($"HandleCompressSeparate: timer fired, proceeding with {allPaths.Count} total paths");
                RunCompressSeparateBatch(allPaths, progressWindow);
            };
            timer.Start();
        }
        else
        {
            LogStartup($"HandleCompressSeparate: subsequent instance, sending {myPaths.Count} paths through pipe");
            SendPathsThroughPipe(myPaths, CompressSeparatePipeName);
            app.Shutdown();
        }
    }
    private static void RunCompressSeparateBatch(List<string> allPaths, ProgressWindow? existingWindow = null)
    {
        LogStartup($"RunCompressSeparateBatch: starting with {allPaths.Count} paths");
        var app = Current;
        if (app == null) return;

        var settings = AppSettings.Instance;

        var progressWindow = existingWindow ?? new ProgressWindow();
        if (existingWindow == null)
        {
            progressWindow.InitCancellation();
            progressWindow.Show();
        }
        app.MainWindow = progressWindow;
        progressWindow.SetProgress(0, L.T(L.App_CompressPreparing));

        var request = new CompressRequest
        {
            SourcePaths = allPaths,
            Mode = CompressOutputMode.Separate,
            Format = settings.DefaultFormat,
            CompressionLevel = settings.DefaultLevel,
            KeepOriginalExtension = settings.KeepOriginalExtension,
            PreserveDirectoryRoot = settings.PreserveDirectoryRoot,
        };
        var outputPaths = CompressService.GetOutputPaths(request);
        progressWindow.InitBatchMode(outputPaths);

        var ct = progressWindow.CancellationToken;

        Task.Run(async () =>
        {
            try
            {
                bool applyToAll = false;
                Core.Abstractions.CompressConflictAction? chosenAction = null;

                var rawProgress = ProgressWindow.CreateBackgroundProgress(progressWindow);

                var progress = progressWindow.CreatePauseAwareProgress(rawProgress);

                var result = await CompressService.CompressAsync(
                    request,
                    conflictResolver: info =>
                    {
                        return progressWindow.Dispatcher.Invoke(() =>
                        {
                            if (applyToAll && chosenAction.HasValue)
                                return new CompressConflictResolution(chosenAction.Value, null);

                            var dlg = new CompressConflictDialog(info.OutputPath, info.CanAdd, info.SuggestedName);
                            var shown = dlg.ShowDialog() == true;
                            if (dlg.ApplyToAll)
                            {
                                applyToAll = true;
                                chosenAction = (Core.Abstractions.CompressConflictAction)dlg.ResultAction;
                            }
                            return new CompressConflictResolution(
                                shown ? (Core.Abstractions.CompressConflictAction)dlg.ResultAction : Core.Abstractions.CompressConflictAction.Cancel,
                                dlg.CustomName);
                        });
                    },
                    progress,
                    ct,
                    onItemStatus: (index, status) =>
                    {
                        progressWindow.SetCurrentBatchItem(index);
                        progressWindow.UpdateBatchItemStatus(index, status);
                    });

                progressWindow.FinalizeBatch();

                if (result.Failed > 0)
                {
                    await progressWindow.Dispatcher.InvokeAsync(() => progressWindow.CompleteWithErrors());
                    await progressWindow.Dispatcher.InvokeAsync(() =>
                    {
                        progressWindow.CancelButton.Content = L.T(L.Progress_Button_Close);
                    });
                    await Task.Run(() =>
                    {
                        var closed = new ManualResetEventSlim(false);
                        EventHandler handler = null!;
                        handler = (_, _) => { closed.Set(); progressWindow.Closed -= handler; };
                        progressWindow.Closed += handler;
                        closed.Wait();
                    });
                    await progressWindow.Dispatcher.InvokeAsync(() => app.Shutdown());
                }
                else
                {
                    await progressWindow.Dispatcher.InvokeAsync(() =>
                        progressWindow.SetComplete(L.T(L.App_CompressComplete)));
                    await progressWindow.AutoCloseOrWaitAsync(2500, () => progressWindow.Dispatcher.Invoke(() => app.Shutdown()));
                }
            }
            catch (OperationCanceledException)
            {
                await progressWindow.Dispatcher.InvokeAsync(() => app.Shutdown());
            }
        });
    }
    private static void HandleCompressCombined(string[] paths)
    {
        LogStartup($"HandleCompressCombined: entered with {paths.Length} CLI args");
        var app = Current;
        if (app == null) return;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        LogStartup($"HandleCompressCombined: after filter, {myPaths.Count} valid paths out of {paths.Length}");
        if (myPaths.Count == 0)
        {
            LogStartup("HandleCompressCombined: no valid paths, shutting down");
            app.Shutdown();
            return;
        }

        bool firstInstance;
        var mutex = new Mutex(true, CompressCombinedMutexName, out firstInstance);
        LogStartup($"HandleCompressCombined: firstInstance={firstInstance}");

        if (firstInstance)
        {
            var allPaths = new List<string>(myPaths);
            var cts = new CancellationTokenSource();

            // 立即显示进度窗口，避免 IPC 收集期间用户无反馈
            var progressWindow = new ProgressWindow();
            progressWindow.InitCancellation();
            progressWindow.Show();
            app.MainWindow = progressWindow;
            progressWindow.SetProgress(0, L.T(L.App_CompressCollecting));
            progressWindow.CancellationToken.Register(() => cts.Cancel());

            _compressCombinedPipeReady.Reset();
            StartPipeServer(allPaths, cts.Token, CompressCombinedPipeName, _compressCombinedPipeReady);

            if (!_compressCombinedPipeReady.Wait(3000))
                LogStartup("HandleCompressCombined: WARNING pipe server did not signal ready within 3s");

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                cts.Cancel();
                mutex.Dispose();
                LogStartup($"HandleCompressCombined: timer fired, proceeding with {allPaths.Count} total paths");
                RunCompressCombined(allPaths, progressWindow);
            };
            timer.Start();
        }
        else
        {
            LogStartup($"HandleCompressCombined: subsequent instance, sending {myPaths.Count} paths through pipe");
            SendPathsThroughPipe(myPaths, CompressCombinedPipeName);
            app.Shutdown();
        }
    }
    private static void RunCompressCombined(List<string> allPaths, ProgressWindow? existingWindow = null)
    {
        LogStartup($"RunCompressCombined: starting with {allPaths.Count} paths");
        var app = Current;
        if (app == null) return;
        var settings = AppSettings.Instance;

        var progressWindow = existingWindow ?? new ProgressWindow();
        if (existingWindow == null)
        {
            progressWindow.InitCancellation();
            progressWindow.Show();
        }
        else
        {
            progressWindow.SetProgress(0, L.T(L.App_CompressPreparing));
        }
        app.MainWindow = progressWindow;

        // 确定公共父目录
        var commonParent = FindCommonParent(allPaths);
        string? parentDir;
        string archiveName;

        if (commonParent != null && !IsDriveRoot(commonParent))
        {
            parentDir = commonParent;
            archiveName = Path.GetFileName(commonParent.TrimEnd('\\', '/'));
        }
        else
        {
            // 无公共父目录或根目录 → 弹输入框
            var firstParent = Path.GetDirectoryName(allPaths[0].TrimEnd('\\', '/'))
                ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var defaultName = Path.GetFileNameWithoutExtension(allPaths[0].TrimEnd('\\', '/'));

            var nameResult = app.Dispatcher.Invoke(() =>
            {
                var dlg = new ArchiveNameDialog(defaultName);
                return dlg.ShowDialog() == true ? dlg.ArchiveName : null;
            });

            if (string.IsNullOrEmpty(nameResult))
            {
                app.Shutdown();
                return;
            }

            parentDir = firstParent;
            archiveName = nameResult;
        }

        var ext = settings.DefaultFormat == "tar.gz" ? ".tar.gz" : "." + settings.DefaultFormat;
        var finalPath = Path.Combine(parentDir, archiveName + ext);

        Log("--compress-combined: {0} paths → {1}", allPaths.Count, finalPath);

        progressWindow.SetProgress(0, L.T(L.App_CompressPreparing));

        var request = new CompressRequest
        {
            SourcePaths = allPaths,
            Mode = CompressOutputMode.Combined,
            Format = settings.DefaultFormat,
            CompressionLevel = settings.DefaultLevel,
            OutputPath = finalPath,
            PreserveDirectoryRoot = settings.PreserveDirectoryRoot,
        };
        var outputPaths = CompressService.GetOutputPaths(request);
        progressWindow.InitBatchMode(outputPaths);
        progressWindow.SetCurrentBatchItem(0);

        var ct = progressWindow.CancellationToken;

        Task.Run(async () =>
        {
            try
            {
                bool applyToAll = false;
                Core.Abstractions.CompressConflictAction? chosenAction = null;

                var result = await CompressService.CompressAsync(
                    request,
                    conflictResolver: info =>
                    {
                        return progressWindow.Dispatcher.Invoke(() =>
                        {
                            if (applyToAll && chosenAction.HasValue)
                                return new CompressConflictResolution(chosenAction.Value, null);

                            var dlg = new CompressConflictDialog(info.OutputPath, info.CanAdd, info.SuggestedName);
                            var shown = dlg.ShowDialog() == true;
                            if (dlg.ApplyToAll)
                            {
                                applyToAll = true;
                                chosenAction = (Core.Abstractions.CompressConflictAction)dlg.ResultAction;
                            }
                            return new CompressConflictResolution(
                                shown ? (Core.Abstractions.CompressConflictAction)dlg.ResultAction : Core.Abstractions.CompressConflictAction.Cancel,
                                dlg.CustomName);
                        });
                    },
                    ProgressWindow.CreateBackgroundProgress(progressWindow.Dispatcher, p =>
                    {
                        progressWindow.SetProgress(p);
                    }),
                    ct);

                progressWindow.FinalizeBatch();

                if (result.Failed > 0)
                {
                    await progressWindow.Dispatcher.InvokeAsync(() =>
                    {
                        AppMessageBox.Show(L.TF(L.App_CompressFailed, "See details in log"), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                        app.Shutdown();
                    });
                }
                else
                {
                    await progressWindow.Dispatcher.InvokeAsync(() =>
                        progressWindow.SetComplete(L.T(L.App_CompressComplete)));
                    await progressWindow.AutoCloseOrWaitAsync(2500, () => progressWindow.Dispatcher.Invoke(() => app.Shutdown()));
                }
            }
            catch (OperationCanceledException)
            {
                await progressWindow.Dispatcher.InvokeAsync(() => app.Shutdown());
            }
            catch (Exception ex)
            {
                Log("--compress-combined 失败: {0}", ex.Message);
                await progressWindow.Dispatcher.InvokeAsync(() =>
                {
                    AppMessageBox.Show(L.TF(L.App_CompressFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                    app.Shutdown();
                });
            }
        });
    }
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
        return trimmed.Length == 2 && trimmed[1] == ':'; // e.g., "C:", "D:"
    }
    /// <summary>
    /// 显示压缩窗口（--compress 专用，无主窗口）。
    /// </summary>
    private static void ShowCompressWindow(List<string> paths)
    {
        LogStartup($"ShowCompressWindow: paths=[{string.Join(";", paths)}]");
        var win = new CompressSettingsWindow { StandaloneMode = true };
        foreach (var p in paths)
        {
            if (File.Exists(p) || Directory.Exists(p))
                win.AddSourcePath(p);
        }

        // 自动填充输出路径：与被压缩文件同目录
        if (paths.Count > 0)
        {
            var first = paths[0];
            string? dir = null;
            if (File.Exists(first))
                dir = Path.GetDirectoryName(first);
            else if (Directory.Exists(first))
                dir = Path.GetDirectoryName(first.TrimEnd('\\', '/'));
            dir ??= Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var s = AppSettings.Instance;
            var name = s.KeepOriginalExtension
                ? Path.GetFileName(first.TrimEnd('\\', '/'))
                : Path.GetFileNameWithoutExtension(first.TrimEnd('\\', '/'));
            var ext = s.DefaultFormat == "tar.gz" ? ".tar.gz" : "." + s.DefaultFormat;
            win.OutputPathTextBox.Text = Path.Combine(dir, name + ext);
        }

        win.Closed += (_, _) =>
        {
            _instanceMutex?.Dispose();
            Current.Shutdown();
        };

        win.Show();
    }
}
