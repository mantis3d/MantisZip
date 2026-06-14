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
/// 解压命令处理器 — Extract 相关方法的 partial 定义
/// </summary>
public partial class App : Application
{
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
                dlg.Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
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
                        "smart" => await ResolveSmartDestAsync(archivePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
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

                // 如果窗口已经被用户关闭（例如点击取消按钮时 Close() 已被调用），
                // Closed 事件已触发，不应再等待它——否则 closed.Wait() 将永远阻塞，
                // 导致 app.Shutdown() 不会被调用，进程留在后台。
                bool windowOpen = await progressWindow.Dispatcher.InvokeAsync(() =>
                    progressWindow.IsVisible);

                if (windowOpen)
                {
                    // 窗口仍可见 → 等待用户手动关闭
                    await Task.Run(() =>
                    {
                        var closed = new ManualResetEventSlim(false);
                        EventHandler handler = null!;
                        handler = (_, _) => { closed.Set(); progressWindow.Closed -= handler; };
                        progressWindow.Closed += handler;
                        closed.Wait();
                    });
                }

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
    private static async Task<string?> ResolveSmartDestAsync(string archivePath)
    {
        var parentDir = Path.GetDirectoryName(archivePath);
        var archiveName = Path.GetFileNameWithoutExtension(archivePath);
        if (string.IsNullOrEmpty(parentDir))
            parentDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        if (engine == null) return Path.Combine(parentDir, archiveName);

        try
        {
            var items = await engine.ListEntriesAsync(archivePath).ConfigureAwait(false);
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
    /// 公共解压逻辑：获取引擎、创建进度窗口、执行解压，完成后退出。
    /// 支持从 PasswordManager 加载已保存密码，以及在加密时弹出密码输入框。
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

        // 后台解压，完成后自动退出
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

                // QuickVerify + 解压（带保存密码）
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
}
