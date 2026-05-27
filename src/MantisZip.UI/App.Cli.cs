using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
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
        LogStartup($"HandleCompressSeparate: paths=[{string.Join(";", paths)}]");
        var app = Current;
        if (app == null) return;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (myPaths.Count == 0)
        {
            app.Shutdown();
            return;
        }

        bool firstInstance;
        var mutex = new Mutex(true, CompressSeparateMutexName, out firstInstance);

        if (firstInstance)
        {
            var allPaths = new List<string>(myPaths);
            var cts = new CancellationTokenSource();
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
                RunCompressSeparateBatch(allPaths);
            };
            timer.Start();
        }
        else
        {
            SendPathsThroughPipe(myPaths, CompressSeparatePipeName);
            app.Shutdown();
        }
    }
    private static void RunCompressSeparateBatch(List<string> allPaths)
    {
        var app = Current;
        if (app == null) return;

        var settings = AppSettings.Instance;

        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();
        app.MainWindow = progressWindow;
        progressWindow.SetProgress(0, L.T(L.App_CompressPreparing));

        var ct = progressWindow.CancellationToken;
        var total = allPaths.Count;
        int succeeded = 0, failed = 0;

        Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < total; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var item = allPaths[i];
                    var itemName = settings.KeepOriginalExtension
                        ? Path.GetFileName(item.TrimEnd('\\', '/'))
                        : Path.GetFileNameWithoutExtension(item.TrimEnd('\\', '/'));
                    string? parentDir;
                    if (File.Exists(item))
                        parentDir = Path.GetDirectoryName(item);
                    else
                        parentDir = Path.GetDirectoryName(item.TrimEnd('\\', '/'));
                    parentDir ??= Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                    var ext = settings.DefaultFormat == "tar.gz" ? ".tar.gz" : "." + settings.DefaultFormat;
                    var outputPath = Path.Combine(parentDir, itemName + ext);

                    // 更新进度显示
                    var progressMsg = L.TF(L.App_CompressSeparateProgress, i + 1, total);
                    await progressWindow.Dispatcher.InvokeAsync(() =>
                        progressWindow.SetProgress((int)((double)i / total * 100), progressMsg));

                    // 冲突处理
                    string finalPath = outputPath;
                    bool addMode = false;
                    if (File.Exists(outputPath))
                    {
                        var engine = ArchiveEngineFactory.GetEngineByExtension(outputPath);
                        bool canAdd = engine is not null and not TarGzEngine;

                        string? initialCustomName = null;
                        var conflictResult = await progressWindow.Dispatcher.InvokeAsync(() =>
                        {
                            var dlg = new CompressConflictDialog(outputPath, canAdd, Path.GetFileName(GetUniquePath(outputPath)));
                            var result = dlg.ShowDialog() == true
                                ? dlg.ResultAction
                                : CompressConflictAction.Cancel;
                            initialCustomName = dlg.CustomName;
                            return result;
                        });

                        switch (conflictResult)
                        {
                            case CompressConflictAction.Cancel:
                                failed++;
                                continue;
                            case CompressConflictAction.Rename:
                                finalPath = Path.Combine(parentDir, initialCustomName ?? Path.GetFileName(GetUniquePath(outputPath)));
                                break;
                            case CompressConflictAction.Add:
                                addMode = true;
                                break;
                            case CompressConflictAction.Overwrite:
                            default:
                                break;
                        }
                    }

                    try
                    {
                        var compressEngine = ArchiveEngineFactory.GetEngineByExtension(finalPath) ?? new ZipEngine();
                        var options = CreateCompressOptions();
                        options.CompressionLevel = settings.DefaultLevel;

                        if (addMode)
                        {
                            await compressEngine.AddToArchiveAsync(finalPath, [item], options,
                                ProgressWindow.CreateBackgroundProgress(progressWindow), ct);
                        }
                        else
                        {
                            await compressEngine.CompressAsync([item], finalPath, options,
                                ProgressWindow.CreateBackgroundProgress(progressWindow), ct);
                        }
                        succeeded++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Log("--compress-separate: item failed ({0}): {1}", item, ex.Message);
                        failed++;
                    }
                }
            }
            catch (OperationCanceledException) { }

            // 完成汇总
            await progressWindow.Dispatcher.InvokeAsync(() =>
            {
                if (failed > 0)
                    progressWindow.SetComplete(L.TF(L.App_CompressSeparateComplete, succeeded, failed));
                else
                    progressWindow.SetComplete(L.T(L.App_CompressComplete));
            });
            await Task.Delay(2500);
            await progressWindow.Dispatcher.InvokeAsync(() => app.Shutdown());
        });
    }
    private static void HandleCompressCombined(string[] paths)
    {
        LogStartup($"HandleCompressCombined: paths=[{string.Join(";", paths)}]");
        var app = Current;
        if (app == null) return;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (myPaths.Count == 0)
        {
            app.Shutdown();
            return;
        }

        bool firstInstance;
        var mutex = new Mutex(true, CompressCombinedMutexName, out firstInstance);

        if (firstInstance)
        {
            var allPaths = new List<string>(myPaths);
            var cts = new CancellationTokenSource();
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
                RunCompressCombined(allPaths);
            };
            timer.Start();
        }
        else
        {
            SendPathsThroughPipe(myPaths, CompressCombinedPipeName);
            app.Shutdown();
        }
    }
    private static void RunCompressCombined(List<string> allPaths)
    {
        var app = Current;
        if (app == null) return;
        var settings = AppSettings.Instance;

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

        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();
        app.MainWindow = progressWindow;
        progressWindow.SetProgress(0, L.T(L.App_CompressPreparing));

        var ct = progressWindow.CancellationToken;

        Task.Run(async () =>
        {
            try
            {
                // 冲突处理
                string outputPath = finalPath;
                if (File.Exists(finalPath))
                {
                    var engine = ArchiveEngineFactory.GetEngineByExtension(finalPath);
                    bool canAdd = engine is not null and not TarGzEngine;

                    string? initialCustomName = null;
                    var conflictResult = await progressWindow.Dispatcher.InvokeAsync(() =>
                    {
                        var dlg = new CompressConflictDialog(finalPath, canAdd, Path.GetFileName(GetUniquePath(finalPath)));
                        var result = dlg.ShowDialog() == true
                            ? dlg.ResultAction
                            : CompressConflictAction.Cancel;
                        initialCustomName = dlg.CustomName;
                        return result;
                    });

                    switch (conflictResult)
                    {
                        case CompressConflictAction.Cancel:
                            await progressWindow.Dispatcher.InvokeAsync(() => app.Shutdown());
                            return;
                        case CompressConflictAction.Rename:
                            outputPath = Path.Combine(parentDir, initialCustomName ?? Path.GetFileName(GetUniquePath(finalPath)));
                            break;
                        case CompressConflictAction.Add:
                            {
                                var addEngine = ArchiveEngineFactory.GetEngineByExtension(outputPath);
                                if (addEngine != null)
                                {
                                    var addOptions = CreateCompressOptions();
                                    addOptions.CompressionLevel = settings.DefaultLevel;
                                    await addEngine.AddToArchiveAsync(outputPath, allPaths.ToArray(), addOptions,
                                        ProgressWindow.CreateBackgroundProgress(progressWindow), ct);
                                }
                                await progressWindow.Dispatcher.InvokeAsync(() =>
                                {
                                    progressWindow.SetComplete(L.T(L.App_AddToArchiveComplete));
                                });
                                await Task.Delay(2500);
                                await progressWindow.Dispatcher.InvokeAsync(() => app.Shutdown());
                                return;
                            }
                    }
                }

                var compressEngine = ArchiveEngineFactory.GetEngineByExtension(outputPath) ?? new ZipEngine();
                var options = CreateCompressOptions();
                options.CompressionLevel = settings.DefaultLevel;

                await compressEngine.CompressAsync(allPaths.ToArray(), outputPath, options,
                    ProgressWindow.CreateBackgroundProgress(progressWindow), ct);

                await progressWindow.Dispatcher.InvokeAsync(() =>
                    progressWindow.SetComplete(L.T(L.App_CompressComplete)));
                await Task.Delay(2500);
                await progressWindow.Dispatcher.InvokeAsync(() => app.Shutdown());
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
    private static void HandleExtractHere(string? archivePath)
    {
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
        {
            AppMessageBox.Show(L.T(L.App_FileNotFound), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
            return;
        }

        // L.T(L.Settings_Tab_Extract)到L.T(L.Settings_Extract_Dest_SameDir)
        var dest = Path.GetDirectoryName(archivePath);
        if (string.IsNullOrEmpty(dest))
            dest = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        RunExtractStatic(archivePath, dest);
    }
    /// <summary>
    /// 处理 --extract-to-name 模式：解压到压缩包名命名的子目录。
    /// </summary>
    private static void HandleExtractToNamed(string? archivePath)
    {
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
        {
            AppMessageBox.Show(L.T(L.App_FileNotFound), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
            return;
        }

        // L.T(L.Settings_Tab_Extract)到 L.T(L.Settings_Extract_Dest_SameDir)\L.T(L.Compress_Archive_Group)L.T(L.Main_Col_Name)\ 下
        var parentDir = Path.GetDirectoryName(archivePath);
        var archiveName = Path.GetFileNameWithoutExtension(archivePath);
        if (string.IsNullOrEmpty(parentDir))
            parentDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var dest = Path.Combine(parentDir, archiveName);

        RunExtractStatic(archivePath, dest);
    }
    /// <summary>
    /// 处理 --extract-smart 模式：分析压缩包结构后自动选择解压方式。
    /// 若所有文件在同一根目录下 → 直接解压到压缩包所在目录；
    /// 若文件分散在多层或在根目录 → 创建压缩包名前缀的子目录。
    /// 条目列表可能因加密失败，此时回退到压缩包名子目录（安全默认值）。
    /// 密码处理完全委托给 RunExtractStatic。
    /// </summary>
    private static void HandleExtractSmart(string? archivePath)
    {
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
        {
            AppMessageBox.Show(L.T(L.App_FileNotFound), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
            return;
        }

        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        if (engine == null)
        {
            AppMessageBox.Show(L.T(L.Main_DragFormatUnsupported), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
            return;
        }

        var parentDir = Path.GetDirectoryName(archivePath);
        var archiveName = Path.GetFileNameWithoutExtension(archivePath);
        if (string.IsNullOrEmpty(parentDir))
            parentDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        // 尝试列出条目分析结构（加密压缩包可能失败 → 使用安全默认值）
        IReadOnlyList<Core.Abstractions.ArchiveItem>? items = null;
        try
        {
            items = engine.ListEntriesAsync(archivePath).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (IsPasswordErrorStatic(ex))
        {
            Log("--extract-smart: ListEntriesAsync failed with crypto error, falling back to named-folder: {0}", ex.Message);
        }

        // 智能决策目标目录
        string dest;
        if (items == null || items.Count == 0)
        {
            // 无法分析或空压缩包 → 使用压缩包名子目录
            dest = Path.Combine(parentDir, archiveName);
        }
        else
        {
            dest = ArchiveStructureAnalyzer.HasSingleRootDirectory(items)
                ? parentDir                                       // 单一根目录 → 直接解压
                : Path.Combine(parentDir, archiveName);            // 分散结构 → 建子目录
        }

        Log("--extract-smart: {0} items, dest={1}", items?.Count ?? 0, dest);
        RunExtractStatic(archivePath, dest);
    }
    /// <summary>
    /// 判断异常是否与密码相关，用于 --extract-smart 的加密回退流程。
    /// </summary>
    private static bool IsPasswordErrorStatic(Exception ex)
    {
        var msg = ex.Message.ToLower();
        return msg.Contains("password") || msg.Contains("encrypted") ||
               msg.Contains("decrypt") || msg.Contains("encryption");
    }
    private static void HandleExtract(string? archivePath)
    {
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
        {
            AppMessageBox.Show(L.T(L.App_FileNotFound), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
            return;
        }

        var settings = AppSettings.Instance;

        // L.T(L.MsgBox_Ok)L.T(L.Settings_Tab_Extract)目标目录
        var dest = ResolveExtractDestinationStatic(archivePath, settings);
        if (dest == null)
        {
            Current.Shutdown();
            return;
        }

        RunExtractStatic(archivePath, dest);
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
                    await Task.Delay(2500);
                    await progressWindow.Dispatcher.InvokeAsync(() => appRef.Shutdown());
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
                    await Task.Delay(2500);
                    await progressWindow.Dispatcher.InvokeAsync(() => appRef.Shutdown());
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
                await Task.Delay(2500);
                await progressWindow.Dispatcher.InvokeAsync(() => appRef.Shutdown());
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

            // 手动控制生命周期：防止 OnStartup 返回后 WPF 自动L.T(L.Progress_Button_Close)窗口
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (myPaths.Count == 0)
        {
            LogStartup("HandleCompressQuick: no valid paths, shutting down");
            app.Shutdown();
            return;
        }

        var settings = AppSettings.Instance;

        // 自动L.T(L.MsgBox_Ok)输出路径：与第一个L.T(L.Compress_Source_Group)/目录同目录
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

        // L.T(L.CompressConflict_Title) → 弹冲突对话框
        string finalPath = outputPath;
        bool addMode = false;
        if (File.Exists(outputPath))
        {
            LogStartup("HandleCompressQuick: output file exists, showing conflict dialog");
            var engine = ArchiveEngineFactory.GetEngineByExtension(outputPath);
            bool canAdd = engine is not null and not TarGzEngine;
            var dispatcher = app.Dispatcher;
            if (dispatcher == null) { LogStartup("HandleCompressQuick: no dispatcher, abort"); app.Shutdown(); return; }

            // 预计算L.T(L.CompressConflict_Rename)的建议路径，供对话框预填
            var suggestedName = Path.GetFileName(GetUniquePath(outputPath));

            var conflictResult = dispatcher.Invoke(() =>
            {
                var dlg = new CompressConflictDialog(outputPath, canAdd, suggestedName);
                var shown = dlg.ShowDialog() == true;
                return (Action: shown ? dlg.ResultAction : CompressConflictAction.Cancel,
                        CustomName: dlg.CustomName);
            });

            LogStartup($"HandleCompressQuick: conflictResult={conflictResult.Action}");

            switch (conflictResult.Action)
            {
                case CompressConflictAction.Cancel:
                    LogStartup("HandleCompressQuick: user cancelled");
                    app.Shutdown();
                    return;
                case CompressConflictAction.Rename:
                    finalPath = Path.Combine(
                        Path.GetDirectoryName(outputPath) ?? ".",
                        conflictResult.CustomName ?? suggestedName);
                    LogStartup($"HandleCompressQuick: renamed to {finalPath}");
                    break;
                case CompressConflictAction.Add:
                    LogStartup("HandleCompressQuick: adding to existing archive");
                    addMode = true;
                    break;
                case CompressConflictAction.Overwrite:
                default:
                    LogStartup("HandleCompressQuick: overwriting");
                    break;
            }
        }

        // 选择添加到已存在的L.T(L.Compress_Archive_Group)
        if (addMode)
        {
            LogStartup("HandleCompressQuick: entering add-to-archive mode");
            var addEngine = ArchiveEngineFactory.GetEngineByExtension(finalPath);
            if (addEngine == null) { LogStartup("HandleCompressQuick: no engine for add, abort"); app.Shutdown(); return; }

            var pw = new ProgressWindow();
            pw.InitCancellation();
            pw.Show();
            app.MainWindow = pw; // 显式设为 MainWindow
            LogStartup("HandleCompressQuick: add-mode ProgressWindow shown");
            pw.SetProgress(0, L.T(L.App_AddToArchiveProgress));

            var addProgress = ProgressWindow.CreateBackgroundProgress(pw);

            // 注意：必须在 UI 线程上捕获 CancellationToken
            var addCt = pw.CancellationToken;
            Task.Run(async () =>
            {
                try
                {
                    LogStartup($"HandleCompressQuick add: AddToArchiveAsync starting, finalPath={finalPath}");
                    await addEngine.AddToArchiveAsync(finalPath, myPaths.ToArray(),
                        new ArchiveOptions { CompressionLevel = settings.DefaultLevel },
                        addProgress, addCt);
                    LogStartup("HandleCompressQuick add: completed");
                    await pw.Dispatcher.InvokeAsync(() => pw.SetComplete(L.T(L.App_AddToArchiveComplete)));
                    await Task.Delay(800);
                    await pw.Dispatcher.InvokeAsync(() => app.Shutdown());
                }
                catch (OperationCanceledException)
                {
                    LogStartup("HandleCompressQuick add: cancelled");
                    await pw.Dispatcher.InvokeAsync(() => app.Shutdown());
                }
                catch (Exception ex)
                {
                    LogStartup($"HandleCompressQuick add: failed: {ex.Message}");
                    Log("--compress-quick add failed: {0}", ex.Message);
                    await pw.Dispatcher.InvokeAsync(() =>
                    {
                        AppMessageBox.Show(L.TF(L.App_AddToArchiveFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                        app.Shutdown();
                    });
                }
            });
            return;
        }

        // 直接压缩
        LogStartup($"HandleCompressQuick: starting compression, finalPath={finalPath}");
        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();
        app.MainWindow = progressWindow; // 显式设为 MainWindow，防止 WPF 自动L.T(L.Progress_Button_Close)
        LogStartup("HandleCompressQuick: ProgressWindow shown");
        progressWindow.SetProgress(0, L.T(L.App_CompressPreparing));

        var options = App.CreateCompressOptions();
        options.CompressionLevel = settings.DefaultLevel;

        var progress = ProgressWindow.CreateBackgroundProgress(progressWindow);

        var compressEngine = ArchiveEngineFactory.GetEngineByExtension(finalPath) ?? new ZipEngine();
        Log("--compress-quick: {0} → {1}", string.Join(", ", myPaths), finalPath);

        // 异步L.T(L.Shell_Compress)，L.T(L.Progress_Done)后自动退出
        // 注意：必须在 UI 线程上捕获 CancellationToken，L.T(L.MsgBox_No)则窗口L.T(L.Progress_Button_Close)后 _cts 被释放导致异常
        var ct = progressWindow.CancellationToken;
        Task.Run(async () =>
        {
            try
            {
                LogStartup($"HandleCompressQuick: CompressAsync starting: finalPath={finalPath}");
                await compressEngine.CompressAsync(myPaths.ToArray(), finalPath, options, progress, ct);
                LogStartup("HandleCompressQuick: CompressAsync completed");
                await progressWindow.Dispatcher.InvokeAsync(() => progressWindow.SetComplete(L.T(L.App_CompressComplete)));
                await Task.Delay(800);
                await progressWindow.Dispatcher.InvokeAsync(() => app.Shutdown());
            }
            catch (OperationCanceledException)
            {
                LogStartup("HandleCompressQuick: compression cancelled");
                await progressWindow.Dispatcher.InvokeAsync(() => app.Shutdown());
            }
            catch (Exception ex)
            {
                LogStartup($"HandleCompressQuick: compression failed: {ex.Message}");
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