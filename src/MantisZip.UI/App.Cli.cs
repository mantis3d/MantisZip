using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// CLI 命令处理器 — 所有 --compress / --extract / --open 等命令的处理方法
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
                var progress = progressWindow.CreatePauseAwareProgress(
                    ProgressWindow.CreateBackgroundProgress(progressWindow.Dispatcher, p =>
                    {
                        if (p.TotalFiles > 0 && p.ProcessedFiles > 0)
                        {
                            var itemIndex = p.ProcessedFiles - 1;
                            if (itemIndex >= 0 && itemIndex < allPaths.Count)
                            {
                                progressWindow.SetCurrentBatchItem(itemIndex);
                            }
                        }
                        rawProgress.Report(p);
                    }));

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
                    onItemStatus: (index, status) => progressWindow.UpdateBatchItemStatus(index, status));

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
            catch (OperationCanceledException) { }
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
                        if (p.TotalFiles > 0 && p.ProcessedFiles > 0)
                        {
                            var itemIndex = p.ProcessedFiles - 1;
                            progressWindow.SetCurrentBatchItem(itemIndex);
                        }
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
    /// L.T(L.Pwd_ShowBtn)L.T(L.Shell_Compress)窗口（--compress 专用，无主窗口）。
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
    /// <summary>
    /// 处理 --extract-here 模式：解压到压缩包所在目录。
    /// </summary>
    private static void HandleExtractHere(string[] paths)
    {
        LogStartup($"HandleExtractHere: paths=[{string.Join(";", paths)}]");
        var app = Current;
        if (app == null) return;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var myPaths = paths.Where(File.Exists).ToList();
        if (myPaths.Count == 0) { app.Shutdown(); return; }

        bool firstInstance;
        var mutex = new Mutex(true, ExtractMutexName, out firstInstance);

        if (firstInstance)
        {
            var allPaths = new List<string>(myPaths);
            var cts = new CancellationTokenSource();
            _extractPipeReady.Reset();
            StartPipeServer(allPaths, cts.Token, ExtractPipeName, _extractPipeReady);

            if (!_extractPipeReady.Wait(3000))
                LogStartup("HandleExtractHere: WARNING pipe server did not signal ready within 3s");

            LogStartup("HandleExtractHere: 启动 DispatcherTimer 800ms");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                cts.Cancel();
                mutex.Dispose();
                LogStartup("HandleExtractHere: DispatcherTimer 触发，调用 HandleExtractBatch");
                try
                {
                    HandleExtractBatch(allPaths, "here");
                    LogStartup("HandleExtractHere: HandleExtractBatch 返回");
                }
                catch (Exception ex)
                {
                    LogStartup($"HandleExtractHere: DispatcherTimer 回调异常: {ex.Message}\n{ex.StackTrace ?? ""}");
                    try { AppMessageBox.Show(L.TF(L.App_ExtractFailed, ex.Message), L.T(L.App_StartupErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
                    Current.Shutdown();
                }
            };
            timer.Start();
        }
        else
        {
            SendPathsThroughPipe(myPaths, ExtractPipeName);
            app.Shutdown();
        }
    }
    /// <summary>
    /// 处理 --extract-to-name 模式：解压到压缩包名命名的子目录。
    /// </summary>
    private static void HandleExtractToNamed(string[] paths)
    {
        LogStartup($"HandleExtractToNamed: paths=[{string.Join(";", paths)}]");
        var app = Current;
        if (app == null) return;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var myPaths = paths.Where(File.Exists).ToList();
        if (myPaths.Count == 0) { app.Shutdown(); return; }

        bool firstInstance;
        var mutex = new Mutex(true, ExtractMutexName, out firstInstance);

        if (firstInstance)
        {
            var allPaths = new List<string>(myPaths);
            var cts = new CancellationTokenSource();
            _extractPipeReady.Reset();
            StartPipeServer(allPaths, cts.Token, ExtractPipeName, _extractPipeReady);

            if (!_extractPipeReady.Wait(3000))
                LogStartup("HandleExtractToNamed: WARNING pipe server did not signal ready within 3s");

            LogStartup("HandleExtractToNamed: 启动 DispatcherTimer 800ms");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                cts.Cancel();
                mutex.Dispose();
                LogStartup("HandleExtractToNamed: DispatcherTimer 触发，调用 HandleExtractBatch");
                try
                {
                    HandleExtractBatch(allPaths, "toname");
                    LogStartup("HandleExtractToNamed: HandleExtractBatch 返回");
                }
                catch (Exception ex)
                {
                    LogStartup($"HandleExtractToNamed: DispatcherTimer 回调异常: {ex.Message}\n{ex.StackTrace ?? ""}");
                    try { AppMessageBox.Show(L.TF(L.App_ExtractFailed, ex.Message), L.T(L.App_StartupErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
                    Current.Shutdown();
                }
            };
            timer.Start();
        }
        else
        {
            SendPathsThroughPipe(myPaths, ExtractPipeName);
            app.Shutdown();
        }
    }
    /// <summary>
    /// 处理 --extract-smart 模式：分析压缩包结构后自动选择解压方式。
    /// 支持多文件批量处理，通过 IPC 合并路径。
    /// </summary>
    private static void HandleExtractSmart(string[] paths)
    {
        LogStartup($"HandleExtractSmart: paths=[{string.Join(";", paths)}]");
        var app = Current;
        if (app == null) return;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var myPaths = paths.Where(File.Exists).ToList();
        if (myPaths.Count == 0) { app.Shutdown(); return; }

        bool firstInstance;
        var mutex = new Mutex(true, ExtractMutexName, out firstInstance);

        if (firstInstance)
        {
            var allPaths = new List<string>(myPaths);
            var cts = new CancellationTokenSource();
            _extractPipeReady.Reset();
            StartPipeServer(allPaths, cts.Token, ExtractPipeName, _extractPipeReady);

            if (!_extractPipeReady.Wait(3000))
                LogStartup("HandleExtractSmart: WARNING pipe server did not signal ready within 3s");

            LogStartup("HandleExtractSmart: 启动 DispatcherTimer 800ms");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                cts.Cancel();
                mutex.Dispose();
                LogStartup("HandleExtractSmart: DispatcherTimer 触发，调用 HandleExtractBatch");
                try
                {
                    HandleExtractBatch(allPaths, "smart");
                    LogStartup("HandleExtractSmart: HandleExtractBatch 返回");
                }
                catch (Exception ex)
                {
                    LogStartup($"HandleExtractSmart: DispatcherTimer 回调异常: {ex.Message}\n{ex.StackTrace ?? ""}");
                    try { AppMessageBox.Show(L.TF(L.App_ExtractFailed, ex.Message), L.T(L.App_StartupErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
                    Current.Shutdown();
                }
            };
            timer.Start();
        }
        else
        {
            SendPathsThroughPipe(myPaths, ExtractPipeName);
            app.Shutdown();
        }
    }
    private static void HandleExtract(string[] paths)
    {
        LogStartup($"HandleExtract: paths=[{string.Join(";", paths)}]");
        var app = Current;
        if (app == null) return;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var myPaths = paths.Where(File.Exists).ToList();
        if (myPaths.Count == 0) { app.Shutdown(); return; }

        bool firstInstance;
        var mutex = new Mutex(true, ExtractMutexName, out firstInstance);

        if (firstInstance)
        {
            var allPaths = new List<string>(myPaths);
            var cts = new CancellationTokenSource();
            _extractPipeReady.Reset();
            StartPipeServer(allPaths, cts.Token, ExtractPipeName, _extractPipeReady);

            if (!_extractPipeReady.Wait(3000))
                LogStartup("HandleExtract: WARNING pipe server did not signal ready within 3s");

            LogStartup("HandleExtract: 启动 DispatcherTimer 800ms");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                cts.Cancel();
                mutex.Dispose();
                LogStartup("HandleExtract: DispatcherTimer 触发，调用 HandleExtractBatch");
                try
                {
                    HandleExtractBatch(allPaths, "extract");
                    LogStartup("HandleExtract: HandleExtractBatch 返回");
                }
                catch (Exception ex)
                {
                    LogStartup($"HandleExtract: DispatcherTimer 回调异常: {ex.Message}\n{ex.StackTrace ?? ""}");
                    try { AppMessageBox.Show(L.TF(L.App_ExtractFailed, ex.Message), L.T(L.App_StartupErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
                    Current.Shutdown();
                }
            };
            timer.Start();
        }
        else
        {
            SendPathsThroughPipe(myPaths, ExtractPipeName);
            app.Shutdown();
        }
    }
    /// <summary>
    /// 批量解压调度入口。mode="extract" 时弹出 ExtractSettingsWindow 让用户选择输出模式；
    /// mode="here"/"smart"/"toname" 时直接按对应模式批量处理。
    /// </summary>
    private static void HandleExtractBatch(List<string> allPaths, string mode)
    {
        LogStartup($"HandleExtractBatch: mode={mode}, paths=[{string.Join(";", allPaths)}]");
        var app = Current;
        if (app == null) { LogStartup("HandleExtractBatch: app is null"); return; }

        // mode=extract 弹出 ExtractSettingsWindow
        if (mode == "extract")
        {
            ExtractOutputMode selectedMode = ExtractOutputMode.ToName;
            string? customDest = null;
            LogStartup("HandleExtractBatch: 准备弹出 ExtractSettingsWindow");
            var ok = app.Dispatcher.Invoke(() =>
            {
                var dlg = new ExtractSettingsWindow(allPaths);
                LogStartup("HandleExtractBatch: ExtractSettingsWindow 已创建，准备 ShowDialog");
                var result = dlg.ShowDialog();
                LogStartup($"HandleExtractBatch: ExtractSettingsWindow 返回 DialogResult={result}");
                if (result != true) return false;
                selectedMode = dlg.OutputMode;
                customDest = dlg.CustomDestination;
                LogStartup($"HandleExtractBatch: 用户选择 mode={selectedMode}, dest={customDest}");
                return true;
            });

            if (!ok) { app.Shutdown(); return; }

            string effectiveMode = selectedMode switch
            {
                ExtractOutputMode.Here => "here",
                ExtractOutputMode.Smart => "smart",
                ExtractOutputMode.ToName => "toname",
                ExtractOutputMode.Manual => "manual",
                _ => "toname"
            };
            if (effectiveMode == "manual")
                HandleExtractBatchCore(allPaths, effectiveMode, app, customDest);
            else
                HandleExtractBatchCore(allPaths, effectiveMode, app, null);
            return;
        }

        // here/smart/toname: 直接批量解压
        HandleExtractBatchCore(allPaths, mode, app, null);
    }

    /// <summary>
    /// 批量解压核心循环。遍历 allPaths，对每个文件按 mode 决定目标目录后调用 engine.ExtractAsync。
    /// 支持取消。完成后自动退出。
    /// </summary>
    private static void HandleExtractBatchCore(List<string> allPaths, string mode, Application app, string? manualDest)
    {
        LogStartup($"HandleExtractBatchCore: mode={mode}, count={allPaths.Count}, manualDest={manualDest}");
        var settings = AppSettings.Instance;

        LogStartup("HandleExtractBatchCore: 创建 ProgressWindow");
        var progressWindow = new ProgressWindow();
        LogStartup("HandleExtractBatchCore: ProgressWindow 已创建，调用 InitCancellation");
        progressWindow.InitCancellation();
        LogStartup("HandleExtractBatchCore: 显示 ProgressWindow");
        progressWindow.Show();
        app.MainWindow = progressWindow;
        progressWindow.InitBatchMode(allPaths);
        progressWindow.SetProgress(0, L.T(L.App_ExtractingProgress));

        var ct = progressWindow.CancellationToken;
        var total = allPaths.Count;

        // 在循环外创建 ExtractOptions，使 ConflictResolver 闭包的 applyToAll 状态可以跨 archive 保持
        var batchOptions = CreateExtractOptions();

        Task.Run(async () =>
        {
        int succeeded = 0, failed = 0, skipped = 0;
            try
            {
                for (int i = 0; i < total; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var archivePath = allPaths[i];
                    await progressWindow.Dispatcher.InvokeAsync(() =>
                        progressWindow.SetCurrentBatchItem(i));

                    // 确定目标路径（所有分支均返回非 null 值）
                    var dest = mode switch
                    {
                        "here" => Path.GetDirectoryName(archivePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        "toname" => Path.Combine(
                            Path.GetDirectoryName(archivePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                            Path.GetFileNameWithoutExtension(archivePath)),
                        "manual" => manualDest ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        "smart" => ResolveSmartDest(archivePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        _ => Path.GetDirectoryName(archivePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                    };

                    // 解压
                    try
                    {
                        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
                        if (engine == null)
                        {
                            failed++;
                            await progressWindow.Dispatcher.InvokeAsync(() =>
                                progressWindow.UpdateBatchItemStatus(i, BatchItemStatus.Failed, "Unsupported format"));
                            continue;
                        }

                        // 密码匹配：对加密压缩包尝试已保存密码，无匹配则弹出密码输入框
                        string? password = null;
                        if (HasEncryptedEntries(archivePath, engine))
                        {
                            // 1. 尝试已保存密码
                            var match = TryMatchPassword(archivePath, engine, progressWindow, true, out _);
                            if (match.HasValue)
                            {
                                password = match.Value.Password;
                                Log("--extract batch: password matched for '{0}'", archivePath);
                            }
                            else
                            {
                                // 2. 弹密码输入框（需在 UI 线程）
                                var promptResult = await progressWindow.Dispatcher.InvokeAsync(() =>
                                    PromptForPassword(archivePath, progressWindow, null));
                                if (promptResult == null)
                                {
                                    failed++;
                                    await progressWindow.Dispatcher.InvokeAsync(() =>
                                        progressWindow.UpdateBatchItemStatus(i, BatchItemStatus.Failed,
                                            L.T(L.App_ExtractFailed)));
                                    continue;
                                }
                                password = promptResult.Value.Password;

                                // 验证用户输入的密码
                                if (password == null || !QuickVerifyPassword(archivePath, password, engine))
                                {
                                    failed++;
                                    await progressWindow.Dispatcher.InvokeAsync(() =>
                                        progressWindow.UpdateBatchItemStatus(i, BatchItemStatus.Failed,
                                            L.T(L.App_WrongPassword)));
                                    continue;
                                }

                                // 如果用户勾选了"记住"，保存到 PasswordManager
                                if (promptResult.Value.Remember)
                                {
                                    try
                                    {
                                        var savePatterns = promptResult.Value.Patterns?.Count > 0
                                            ? promptResult.Value.Patterns
                                            : new List<string> { Path.GetFileName(archivePath) };
                                        PasswordManager.Instance.AddPassword(password,
                                            promptResult.Value.Description ?? "", savePatterns);
                                        Log("--extract batch: password saved for '{0}'", archivePath);
                                    }
                                    catch (Exception pwdEx)
                                    {
                                        Log("--extract batch: save password failed: {0}", pwdEx.Message);
                                    }
                                }
                            }
                        }

                        var progress = progressWindow.CreatePauseAwareProgress(
                            ProgressWindow.CreateBackgroundProgress(progressWindow));

                        await engine.ExtractAsync(archivePath, dest, password, progress, ct, batchOptions);

                        succeeded++;
                        await progressWindow.Dispatcher.InvokeAsync(() =>
                            progressWindow.UpdateBatchItemStatus(i, BatchItemStatus.Completed));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Log("--extract batch: item failed ({0}): {1}", archivePath, ex.Message);
                        failed++;
                        await progressWindow.Dispatcher.InvokeAsync(() =>
                            progressWindow.UpdateBatchItemStatus(i, BatchItemStatus.Failed, ex.Message));
                    }
                }
            }
            catch (OperationCanceledException) { }

            // 完成
            if (failed > 0)
            {
                await progressWindow.Dispatcher.InvokeAsync(() =>
                    progressWindow.CompleteWithErrors());
                // Wait for user to close
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
                    progressWindow.SetComplete(L.T(L.App_ExtractComplete)));
                // 全部成功：最后一个解压的目录用于打开资源管理器（仅单文件模式）
                if (settings.OpenFolderAfterExtract && allPaths.Count == 1)
                {
                    var lastDest = Path.GetDirectoryName(allPaths[0])
                        ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    OpenInExplorerStatic(lastDest);
                }
                await progressWindow.AutoCloseOrWaitAsync(2500, () => progressWindow.Dispatcher.Invoke(() => app.Shutdown()));
            }
        });
    }

    /// <summary>
    /// 智能解压路径分析：分析压缩包结构后返回目标目录。
    /// 若所有文件在同一根目录下 → 返回压缩包所在目录；
    /// 否则返回压缩包名子目录。
    /// </summary>
    private static string? ResolveSmartDest(string archivePath)
    {
        var parentDir = Path.GetDirectoryName(archivePath);
        var archiveName = Path.GetFileNameWithoutExtension(archivePath);
        if (string.IsNullOrEmpty(parentDir))
            parentDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        if (engine == null) return Path.Combine(parentDir, archiveName);

        try
        {
            var items = engine.ListEntriesAsync(archivePath).GetAwaiter().GetResult();
            if (items == null || items.Count == 0)
                return Path.Combine(parentDir, archiveName);

            return ArchiveStructureAnalyzer.HasSingleRootDirectory(items)
                ? parentDir
                : Path.Combine(parentDir, archiveName);
        }
        catch
        {
            return Path.Combine(parentDir, archiveName);
        }
    }

    /// <summary>
    /// 公共L.T(L.Settings_Tab_Extract)逻辑：获取引擎、L.T(L.Pwd_ShowBtn)进度窗口、执行L.T(L.Settings_Tab_Extract)，L.T(L.Progress_Done)后退出。
    /// 支持从 PasswordManager 加载已L.T(L.PwdEdit_Save)密码，以及在加密时弹出密码输入框。
    /// </summary>
    private static void RunExtractStatic(string archivePath, string dest)
    {
        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        if (engine == null)
        {
            AppMessageBox.Show(L.T(L.Main_DragFormatUnsupported), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
            return;
        }

        var settings = AppSettings.Instance;

        // 显示进度窗口
        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();
        progressWindow.SetProgress(0, L.T(L.Main_Status_Extracting));

        var progress = progressWindow.CreatePauseAwareProgress(
            ProgressWindow.CreateBackgroundProgress(progressWindow));

        Log("--extract: {0} → {1}", archivePath, dest);

        // 后台L.T(L.Settings_Tab_Extract)，L.T(L.Progress_Done)后自动退出
        var appRef = Current; // capture for lambdas
        Task.Run(async () =>
        {
            try
            {
                bool hasEncrypted = HasEncryptedEntries(archivePath, engine);
                bool showPwd = hasEncrypted && AppSettings.Instance.ShowPasswordMatchNotification;

                // 先试已保存密码
                var match = TryMatchPassword(archivePath, engine, progressWindow, showPwd, out var limitReached);
                if (match != null)
                {
                    var (pwd, desc) = match.Value;
                    LogStartup($"RunExtractStatic: matched saved password desc={desc}");

                    if (showPwd) progressWindow.ShowPasswordMatched(pwd, desc);
                    var opts = CreateExtractOptions();
                    await engine.ExtractAsync(archivePath, dest, pwd, progress, progressWindow.CancellationToken, opts);

                    await progressWindow.Dispatcher.InvokeAsync(() =>
                    {
                        appRef.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                        progressWindow.SetComplete(L.T(L.App_ExtractComplete));
                    });
                    if (settings.OpenFolderAfterExtract) OpenInExplorerStatic(dest);
                    await progressWindow.AutoCloseOrWaitAsync(2500, () => progressWindow.Dispatcher.Invoke(() => appRef.Shutdown()));
                    return;
                }

                // 自动尝试达到上限 → 提示用户
                if (limitReached)
                {
                    LogStartup("RunExtractStatic: auto-try limit reached, notifying user");
                    await progressWindow.Dispatcher.InvokeAsync(() =>
                    {
                        AppMessageBox.Show(
                            L.TF(L.PwdMgr_AutoTry_LimitReached, 100),
                            L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                }

                // 所有已保存密码失败 → 弹密码输入框
                if (!hasEncrypted)
                {
                    // 非加密压缩包：直接解压，不需要密码
                    LogStartup("RunExtractStatic: no saved passwords and not encrypted, extracting without password");
                    var opts = CreateExtractOptions();
                    await engine.ExtractAsync(archivePath, dest, null, progress, progressWindow.CancellationToken, opts);
                    await progressWindow.Dispatcher.InvokeAsync(() =>
                    {
                        appRef.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                        progressWindow.SetComplete(L.T(L.App_ExtractComplete));
                    });
                    if (settings.OpenFolderAfterExtract) OpenInExplorerStatic(dest);
                    await progressWindow.AutoCloseOrWaitAsync(2500, () => progressWindow.Dispatcher.Invoke(() => appRef.Shutdown()));
                    return;
                }

                LogStartup("RunExtractStatic: all saved passwords failed, showing PasswordDialog");
                var pwdResult = PromptForPassword(archivePath, progressWindow, null);
                if (pwdResult == null)
                {
                    await progressWindow.Dispatcher.InvokeAsync(() => appRef.Shutdown());
                    return;
                }

                var (userPwd, remember, pwdDesc, pwdPatterns) = pwdResult.Value;
                if (string.IsNullOrEmpty(userPwd))
                {
                    await progressWindow.Dispatcher.InvokeAsync(() => appRef.Shutdown());
                    return;
                }
                LogStartup($"RunExtractStatic: user entered password (remember={remember})");

                // QuickVerify + L.T(L.Settings_Tab_Extract)（带L.T(L.PwdEdit_Save)L.T(L.PwdMgr_Col_Password)）
                bool showPwdManual = AppSettings.Instance.ShowPasswordMatchNotification;
                if (!await ExtractWithPasswordAsync(archivePath, dest, engine, userPwd, L.T(L.Main_ForceLoadPwd),
                        progressWindow, progress, progressWindow.CancellationToken, showPwdManual, remember,
                        pwdDesc, pwdPatterns))
                {
                    await progressWindow.Dispatcher.InvokeAsync(() =>
                    {
                        AppMessageBox.Show(L.T(L.Main_Status_WrongPwd), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                        appRef.Shutdown();
                    });
                    return;
                }

                await progressWindow.Dispatcher.InvokeAsync(() =>
                {
                    appRef.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    progressWindow.SetComplete(L.T(L.App_ExtractComplete));
                });
                if (settings.OpenFolderAfterExtract) OpenInExplorerStatic(dest);
                await progressWindow.AutoCloseOrWaitAsync(2500, () => progressWindow.Dispatcher.Invoke(() => appRef.Shutdown()));
            }
            catch (OperationCanceledException)
            {
                await progressWindow.Dispatcher.InvokeAsync(() => appRef.Shutdown());
            }
            catch (Exception ex)
            {
                LogStartup($"RunExtractStatic: exception: {ex.Message}");
                Log("--extract 失败: {0}", ex.Message);
                await progressWindow.Dispatcher.InvokeAsync(() =>
                {
                    AppMessageBox.Show(L.TF(L.App_ExtractFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                    appRef.Shutdown();
                });
            }
        });
    }
    /// <summary>
    /// 处理 --open 模式：启动主窗口并加载L.T(L.Compress_Archive_Group)供L.T(L.Settings_Advanced_Browse)。
    /// </summary>
    private static void HandleOpen(string? archivePath)
    {
        var mainWin = new MainWindow();

        if (!string.IsNullOrEmpty(archivePath) && File.Exists(archivePath))
        {
            mainWin.Loaded += async (_, _) =>
            {
                await Task.Delay(200);
                await mainWin.LoadArchiveAsync(archivePath);
            };
        }

        mainWin.Show();
    }
    /// <summary>
    /// 处理 --compress-quick 模式：不L.T(L.Pwd_ShowBtn)L.T(L.Settings_Title)窗口，使用 AppSettings 默认值直接L.T(L.Shell_Compress)。
    /// </summary>
    private static void HandleCompressQuick(string[] paths)
    {
        try
        {
            LogStartup($"HandleCompressQuick: paths=[{string.Join(";", paths)}]");
            var app = Current;
            if (app == null) return;

            // 手动控制生命周期
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (myPaths.Count == 0)
            {
                LogStartup("HandleCompressQuick: no valid paths, shutting down");
                app.Shutdown();
                return;
            }

            var settings = AppSettings.Instance;

            // 自动确定输出路径
            var first = myPaths[0];
            string? dir;
            if (File.Exists(first))
                dir = Path.GetDirectoryName(first);
            else
                dir = Path.GetDirectoryName(first.TrimEnd('\\', '/'));
            dir ??= Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var baseName = settings.KeepOriginalExtension
                ? Path.GetFileName(first.TrimEnd('\\', '/'))
                : Path.GetFileNameWithoutExtension(first.TrimEnd('\\', '/'));

            var ext = settings.DefaultFormat == "tar.gz" ? ".tar.gz" : "." + settings.DefaultFormat;
            var outputPath = Path.Combine(dir, baseName + ext);
            LogStartup($"HandleCompressQuick: baseName={baseName}, ext={ext}, outputPath={outputPath}");

        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();
        app.MainWindow = progressWindow;
        progressWindow.SetProgress(0, L.T(L.App_CompressPreparing));

        var request = new CompressRequest
        {
            SourcePaths = myPaths,
            Mode = CompressOutputMode.Manual,
            Format = settings.DefaultFormat,
            CompressionLevel = settings.DefaultLevel,
            KeepOriginalExtension = settings.KeepOriginalExtension,
            OutputPath = outputPath,
            PreserveDirectoryRoot = settings.PreserveDirectoryRoot,
        };
        var outputPaths = CompressService.GetOutputPaths(request);
        progressWindow.InitBatchMode(outputPaths);

        bool applyToAll = false;
        Core.Abstractions.CompressConflictAction? chosenAction = null;

        var rawProgress = ProgressWindow.CreateBackgroundProgress(progressWindow);
        var progress = ProgressWindow.CreateBackgroundProgress(progressWindow.Dispatcher, p =>
        {
            if (p.TotalFiles > 0 && p.ProcessedFiles > 0)
            {
                var itemIndex = p.ProcessedFiles - 1;
                if (itemIndex >= 0 && itemIndex < myPaths.Count)
                {
                    progressWindow.SetCurrentBatchItem(itemIndex);
                }
            }
            rawProgress.Report(p);
        });
        var ct = progressWindow.CancellationToken;

            Task.Run(async () =>
            {
                try
                {
                    LogStartup("HandleCompressQuick: starting via CompressService");

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
                        ct);

                    progressWindow.FinalizeBatch();

                    if (result.Failed > 0)
                    {
                        LogStartup($"HandleCompressQuick: failed");
                        await progressWindow.Dispatcher.InvokeAsync(() =>
                        {
                            AppMessageBox.Show(L.TF(L.App_CompressFailed, "See details in log"), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                            app.Shutdown();
                        });
                    }
                    else
                    {
                        LogStartup("HandleCompressQuick: completed successfully");
                        await progressWindow.Dispatcher.InvokeAsync(() =>
                            progressWindow.SetComplete(L.T(L.App_CompressComplete)));
                        await progressWindow.AutoCloseOrWaitAsync(800, () => progressWindow.Dispatcher.Invoke(() => app.Shutdown()));
                    }
                }
                catch (OperationCanceledException)
                {
                    LogStartup("HandleCompressQuick: cancelled");
                    await progressWindow.Dispatcher.InvokeAsync(() => app.Shutdown());
                }
                catch (Exception ex)
                {
                    LogStartup($"HandleCompressQuick: failed: {ex.Message}");
                    Log("--compress-quick 失败: {0}", ex.Message);
                    await progressWindow.Dispatcher.InvokeAsync(() =>
                    {
                        AppMessageBox.Show(L.TF(L.App_CompressFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                        app.Shutdown();
                    });
                }
            });
        }
        catch (Exception ex)
        {
            LogStartup($"HandleCompressQuick: unexpected error: {ex.Message}\n{ex.StackTrace}");
            AppMessageBox.Show(L.TF(L.App_QuickCompressFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            Current?.Shutdown();
        }
    }

}