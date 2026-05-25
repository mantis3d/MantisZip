using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MantisZip", "debug.log");
    // StartupLog 已合并到 LogFile（同一个文件）
    private static string StartupLog => LogFile;
    private static Mutex? _instanceMutex;

    private static string CompressMutexName = "MantisZip-CompressMutex";
    private static string CompressPipeName = "MantisZip-Compress";
    private static readonly ManualResetEventSlim _compressPipeReady = new(false);

    private static string CompressSeparateMutexName = "MantisZip-CompressSeparateMutex";
    private static string CompressSeparatePipeName = "MantisZip-CompressSeparate";
    private static readonly ManualResetEventSlim _compressSeparatePipeReady = new(false);

    private static string CompressCombinedMutexName = "MantisZip-CompressCombinedMutex";
    private static string CompressCombinedPipeName = "MantisZip-CompressCombined";
    private static readonly ManualResetEventSlim _compressCombinedPipeReady = new(false);

    /// <summary>
    /// 全局初始化：所有入口（主窗口、--compress、--extract 等）都先经过这里。
    /// 用于L.T(L.Settings_Title)编码、全局静态变量等。
    /// </summary>
    private static void InitializeApp()
    {
        // 注册编码提供程序，支持 GBK 等非系统预装编码
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // 不再全局设置 ZipStrings.CodePage。
        // ZipEngine 会按压缩包检测 UTF-8 标记，自动选择合适的编码。

        // 从用户设置加载 7z.exe 路径，覆盖 SevenZipEngine 的默认值
        try
        {
            var s = AppSettings.Instance;
            if (!string.IsNullOrEmpty(s.SevenZipPath))
            {
                SevenZipEngine.SevenZipPath = s.SevenZipPath;
                LogDebug("InitializeApp: SevenZipPath set to {0}", s.SevenZipPath);
            }
        }
        catch (Exception initEx)
        {
            TraceLog("InitializeApp: failed to load AppSettings: {0}", initEx.Message);
            // 使用默认 7z 路径继续运行
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局初始化（所有入口最先运行）
        InitializeApp();

        // 初始化语言管理器（需在 InitializeApp 之后，因为初始化加载 AppSettings）
        LanguageManager.Instance.Initialize();

        // 应用已保存的主题设置
        ApplyTheme(AppSettings.Instance.Theme);

        // 初始化 CoreLog 日志脱敏委托。此后所有 CoreLog 写入自动脱敏。
        CoreLog.RedactOverride = msg =>
            LogRedactor.RedactPaths(msg, LogRedactor.ParseMode(AppSettings.Instance.LogPrivacyMode));

        // ===== 统一日志 =====
        // 所有日志（启动日志、调试日志、CoreLog 追踪日志）都写入同一个文件：
        // %LOCALAPPDATA%\MantisZip\debug.log
        // 该文件持久化保留，不被自动删除。
        LogStartup($"START BaseDir={AppDomain.CurrentDomain.BaseDirectory} Args=[{string.Join(" ", e.Args)}]");

        // WPF 跟踪监听器（写入同一个文件）
        var listener = new TextWriterTraceListener(LogFile);
        listener.Name = "FileLogger";
        Trace.Listeners.Add(listener);
        Trace.AutoFlush = true;

        LogDebug("LogDebug: debug.log will be appended");

        LogStartup($"启动参数: {string.Join(" ", e.Args)}");
        TraceLog("OnStartup: after args log");

        try
        {
            if (e.Args.Length > 0)
            {
                switch (e.Args[0])
                {
                    case "--install-shell":
                        ShellIntegration.Install();
                        AppMessageBox.Show(L.T(L.App_ShellInstalled),
                            L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
                        Shutdown();
                        return;

                    case "--uninstall-shell":
                        ShellIntegration.Uninstall();
                        AppMessageBox.Show(L.T(L.App_ShellUninstalled), L.T(L.App_MantisZipTitle),
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        Shutdown();
                        return;

                    case "--install-assoc":
                        ShellIntegration.InstallAssociations();
                        AppMessageBox.Show(L.T(L.App_AssocInstalled),
                            L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
                        Shutdown();
                        return;

                    case "--uninstall-assoc":
                        ShellIntegration.UninstallAssociations();
                        AppMessageBox.Show(L.T(L.Settings_Assoc_UninstalledMsg), L.T(L.App_MantisZipTitle),
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        Shutdown();
                        return;

                    case "--compress":
                        HandleCompress(e.Args.Skip(1).ToArray());
                        return; // HandleCompress 内部会 Shutdown 或继续运行

                    case "--extract":
                        HandleExtract(e.Args.Length > 1 ? e.Args[1] : null);
                        return;

                    case "--extract-here":
                        HandleExtractHere(e.Args.Length > 1 ? e.Args[1] : null);
                        return;

                    case "--extract-to-name":
                        HandleExtractToNamed(e.Args.Length > 1 ? e.Args[1] : null);
                        return;

                    case "--extract-smart":
                        HandleExtractSmart(e.Args.Length > 1 ? e.Args[1] : null);
                        return;

                    case "--compress-quick":
                        HandleCompressQuick(e.Args.Skip(1).ToArray());
                        return;

                    case "--compress-separate":
                        HandleCompressSeparate(e.Args.Skip(1).ToArray());
                        return;

                    case "--compress-combined":
                        HandleCompressCombined(e.Args.Skip(1).ToArray());
                        return;

                    case "--test":
                        LogStartup("--test 模式：L.T(L.Settings_Menu_Btn_Apply)启动成功");
                        AppMessageBox.Show(
                            L.TF(L.App_StartupSuccess, AppDomain.CurrentDomain.BaseDirectory, StartupLog),
                            L.T(L.App_StartupTestTitle), MessageBoxButton.OK, MessageBoxImage.Information);
                        Shutdown();
                        return;

                    case "--open":
                        HandleOpen(e.Args.Length > 1 ? e.Args[1] : null);
                        return;
                }
            }
        }
        catch (Exception ex)
        {
            Log("OnStartup 异常: {0}\n{1}", ex.Message, ex.StackTrace ?? "");
            AppMessageBox.Show(L.TF(L.App_StartupFailed, ex.Message), L.T(L.App_StartupErrorTitle),
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // 正常启动：手动创建主窗口（已L.T(L.Compress_Remove) StartupUri）
        TraceLog("OnStartup: creating MainWindow");
        var mainWin = new MainWindow();
        TraceLog("OnStartup: MainWindow created, showing");
        mainWin.Show();
        TraceLog("OnStartup: MainWindow shown");
        base.OnStartup(e);
        Log("程序启动");
        TraceLog("OnStartup: startup complete");
    }

    #region --compress 多实例 IPC

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

    private static void StartCompressPipeServer(List<string> allPaths, CancellationToken ct)
    {
        Task.Run(async () =>
        {
            try
            {
                _compressPipeReady.Set();
                // 循环接受多个客户端连接。Windows 每选一个文件启动一个独立进程，
                // 每个后续进程通过命名管道发送自己的路径。
                while (!ct.IsCancellationRequested)
                {
                    using var pipe = new NamedPipeServerStream(
                        CompressPipeName, PipeDirection.In, -1,
                        PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                    try
                    {
                        await pipe.WaitForConnectionAsync(ct);
                        using var reader = new StreamReader(pipe);
                        string? line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            lock (allPaths)
                            {
                                if (!allPaths.Contains(line) && (File.Exists(line) || Directory.Exists(line)))
                                    allPaths.Add(line);
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    finally { pipe.Dispose(); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception pipeEx) { LogDebug("StartCompressPipeServer: connection error: {0}", pipeEx.Message); }
        });
    }

    private static void SendPathsToFirstInstance(List<string> paths)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", CompressPipeName, PipeDirection.Out);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe);
            foreach (var p in paths)
                writer.WriteLine(p);
            writer.Flush();
        }
        catch (Exception ex)
        {
            Log("SendPathsToFirstInstance 失败: {0}", ex.Message);
        }
    }

    #endregion

    #region --compress-separate / --compress-combined IPC

    private static void StartPipeServer(List<string> allPaths, CancellationToken ct, string pipeName, ManualResetEventSlim readyEvent)
    {
        Task.Run(async () =>
        {
            try
            {
                readyEvent.Set();
                // 循环接受多个客户端连接。Windows 每选一个文件启动一个独立进程，
                // 每个后续进程通过命名管道发送自己的路径。
                while (!ct.IsCancellationRequested)
                {
                    using var pipe = new NamedPipeServerStream(
                        pipeName, PipeDirection.In, -1,
                        PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                    try
                    {
                        await pipe.WaitForConnectionAsync(ct);
                        using var reader = new StreamReader(pipe);
                        string? line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            lock (allPaths)
                            {
                                if (!allPaths.Contains(line) && (File.Exists(line) || Directory.Exists(line)))
                                    allPaths.Add(line);
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    finally { pipe.Dispose(); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception pipeEx) { LogDebug("PipeServer ({0}): connection error: {1}", pipeName, pipeEx.Message); }
        });
    }

    private static void SendPathsThroughPipe(List<string> paths, string pipeName)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe);
            foreach (var p in paths)
                writer.WriteLine(p);
            writer.Flush();
        }
        catch (Exception ex)
        {
            Log("SendPathsThroughPipe ({0}) 失败: {1}", pipeName, ex.Message);
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

    private static string? FindCommonParent(List<string> paths)
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

    private static bool IsDriveRoot(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        return trimmed.Length == 2 && trimmed[1] == ':'; // e.g., "C:", "D:"
    }

    #endregion

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

    #region 共享解压逻辑

    /// <summary>
    /// 从已保存密码中匹配并快速验证。返回 (密码, 描述) 或 null。
    /// limitReached 表示匹配到的密码超过上限（防暴力破解），已截断。
    /// </summary>
    internal static (string Password, string Description)? TryMatchPassword(
        string archivePath, IArchiveEngine engine, ProgressWindow? progressWindow,
        bool showPwdSection, out bool limitReached)
    {
        const int maxAttempts = 100;
        var allMatches = PasswordManager.Instance.FindMatchingPasswords(archivePath);
        limitReached = allMatches.Count > maxAttempts;
        var candidatePasswords = limitReached ? allMatches.Take(maxAttempts).ToList() : allMatches;
        var tried = new HashSet<string>();

        foreach (var entry in candidatePasswords)
        {
            var pwd = entry.Password;
            if (!tried.Add(pwd)) continue;

            var desc = !string.IsNullOrEmpty(entry.Description) ? entry.Description : pwd;
            if (showPwdSection) progressWindow?.ShowPasswordAttempt(desc);

            if (QuickVerifyPassword(archivePath, pwd, engine))
            {
                if (showPwdSection) progressWindow?.ShowPasswordMatched(pwd, desc);
                return (pwd, desc);
            }
        }
        return null;
    }

    /// <summary>
    /// 弹出密码输入框，返回 (密码, 是否记住, 描述, 规则列表) 或 null（用户取消）。
    /// 会隐藏并恢复 progressWindow 避免被挡住。
    /// </summary>
    internal static (string? Password, bool Remember, string? Description, List<string>? Patterns)? PromptForPassword(
        string archivePath, ProgressWindow progressWindow, Window? owner)
    {
        return progressWindow.Dispatcher.Invoke(() =>
        {
            progressWindow.Hide();
            var dialog = new PasswordDialog(Path.GetFileName(archivePath));
            dialog.Owner = owner;
            if (owner == null)
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.Topmost = true;
            }
            PasswordDialogResult? result = null;
            if (dialog.ShowDialog() == true)
            {
                result = new PasswordDialogResult
                {
                    Password = dialog.ResultPassword,
                    Remember = dialog.RememberPassword,
                    Description = dialog.Description,
                    Patterns = dialog.Patterns
                };
            }
            progressWindow.Show();
            return result != null
                ? (result.Password, result.Remember, result.Description, result.Patterns)
                : default((string? Password, bool Remember, string? Description, List<string>? Patterns)?);
        });
    }

    internal class PasswordDialogResult
    {
        public string? Password { get; set; }
        public bool Remember { get; set; }
        public string? Description { get; set; }
        public List<string> Patterns { get; set; } = new();
    }

    /// <summary>
    /// QuickVerify + 全量解压 + 密码区 UI 更新。
    /// 解压成功返回 true，密码错误弹窗后返回 false。
    /// </summary>
    internal static async Task<bool> ExtractWithPasswordAsync(
        string archivePath, string destinationPath, IArchiveEngine engine,
        string password, string description, ProgressWindow progressWindow,
        IProgress<ArchiveProgress> progress, CancellationToken ct,
        bool showPwdSection, bool? rememberPwd = null,
        string? pwdDesc = null, List<string>? pwdPatterns = null)
    {
        if (showPwdSection) progressWindow.ShowPasswordAttempt(description);
        if (!QuickVerifyPassword(archivePath, password, engine))
            return false;

        if (showPwdSection) progressWindow.ShowPasswordMatched(password, description);

        var opts = CreateExtractOptions();
        await engine.ExtractAsync(archivePath, destinationPath, password, progress, ct, opts);

        if (rememberPwd == true && !string.IsNullOrEmpty(password))
        {
            var savePatterns = (pwdPatterns != null && pwdPatterns.Count > 0)
                ? pwdPatterns
                : new List<string> { Path.GetFileName(archivePath) };
            var saveDesc = !string.IsNullOrEmpty(pwdDesc) ? pwdDesc : "";
            try { PasswordManager.Instance.AddPassword(password, saveDesc, savePatterns); }
            catch (Exception pwdEx) { Log("HandleExtractAsync: failed to save password: {0}", pwdEx.Message); }
        }
        return true;
    }

    #endregion

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
    /// 从 AppSettings 读取L.T(L.Settings_Tab_Extract)冲突策略。
    /// </summary>
    /// <summary>
    /// 创建解压选项，包含冲突处理设置和 Ask 弹窗回调。
    /// 回调会在后台线程调用，使用 Dispatcher 调度到 UI 线程显示对话框。
    /// </summary>
    internal static ArchiveOptions CreateExtractOptions()
    {
        bool applyToAll = false;
        FileConflictAction? chosenAction = null;

        return new ArchiveOptions
        {
            ConflictAction = GetConflictActionFromSettings(),
            ConflictResolver = info =>
            {
                // 已勾选"L.T(L.Settings_Menu_Btn_Apply)到全部" → 直接返回记忆的选择
                if (applyToAll && chosenAction.HasValue)
                    return chosenAction.Value;

                // 调度到 UI 线程L.T(L.Pwd_ShowBtn)模态对话框
                var dispatcher = Current?.Dispatcher;
                if (dispatcher == null) return FileConflictAction.Overwrite;

                var result = dispatcher.Invoke(() =>
                {
                    var dialog = new ConflictDialog(info);
                    dialog.ShowDialog();
                    info.CustomName = dialog.CustomName;
                    return (Action: dialog.ResultAction, All: dialog.ApplyToAll);
                });

                if (result.All)
                {
                    applyToAll = true;
                    chosenAction = result.Action;
                }

                return result.Action;
            }
        };
    }

    /// <summary>
    /// 创建压缩选项，包含文件读取错误的处理回调。
    /// </summary>
    internal static ArchiveOptions CreateCompressOptions()
    {
        bool applyToAll = false;
        FileErrorAction? chosenAction = null;

        return new ArchiveOptions
        {
            ErrorResolver = info =>
            {
                if (applyToAll && chosenAction.HasValue)
                    return chosenAction.Value;

                var dispatcher = Current?.Dispatcher;
                if (dispatcher == null) return FileErrorAction.Abort;

                var result = dispatcher.Invoke(() =>
                {
                    var dialog = new ErrorDialog(info);
                    dialog.ShowDialog();
                    return (Action: dialog.ResultAction, All: dialog.ApplyToAll);
                });

                if (result.All)
                {
                    applyToAll = true;
                    chosenAction = result.Action;
                }

                return result.Action;
            }
        };
    }

    /// <summary>
    /// 自动生成唯一的文件名：重复时加 (1),(2)... 后缀。
    /// 正确处理 .tar.gz 等双扩展名。
    /// </summary>
    private static string GetUniquePath(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        string name, ext;

        // 特别处理 .tar.gz 双扩展名
        if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            // "C:\path\archive.tar.gz" → name="archive", ext=".tar.gz"
            var withoutExt = path[..^7]; // 去掉 .tar.gz
            name = Path.GetFileName(withoutExt);
            ext = ".tar.gz";
        }
        else
        {
            name = Path.GetFileNameWithoutExtension(path);
            ext = Path.GetExtension(path);
        }

        for (int i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
            {
                LogDebug("GetUniquePath: {0} → {1}", path, candidate);
                return candidate;
            }
        }
        LogDebug("GetUniquePath: {0} → {0} (all names taken, fallback)", path);
        return path; // 999 个名字全被占用了，直接L.T(L.CompressConflict_Overwrite)原文件
    }

    internal static FileConflictAction GetConflictActionFromSettings()
    {
        return AppSettings.Instance.FileConflictAction switch
        {
            "overwrite" => FileConflictAction.Overwrite,
            "rename" => FileConflictAction.Rename,
            "skip" => FileConflictAction.Skip,
            "ask" => FileConflictAction.Ask,
            "overwrite-if-older" => FileConflictAction.OverwriteIfOlder,
            "overwrite-if-smaller" => FileConflictAction.OverwriteIfSmaller,
            _ => FileConflictAction.Overwrite
        };
    }

    /// <summary>
    /// 快速验证L.T(L.PwdMgr_Col_Password)L.T(L.MsgBox_Yes)L.T(L.MsgBox_No)正确——读第一个L.T(L.Main_Col_Encrypted)条目 1 字节，
    /// 密码不对时 SharpZipLib / SevenZipExtractor 会在读字节前抛异常。
    /// 只捕获密码相关异常，系统级错误（FileNotFoundException、UnauthorizedAccessException 等）向上传播。
    /// </summary>
    /// <summary>
    /// 快速检查压缩包是否有加密条目（不验证密码，只检查有无加密标志）。
    /// </summary>
    internal static bool HasEncryptedEntries(string archivePath, IArchiveEngine engine)
    {
        try
        {
            if (engine is ZipEngine)
            {
                using var zipFile = ZipEngine.OpenZipFile(archivePath);
                return zipFile.Cast<ZipEntry>().Any(e => e.IsCrypted || e.AESKeySize > 0);
            }
            if (engine is SevenZipEngine)
            {
                using var archiveFile = new global::SevenZipExtractor.ArchiveFile(archivePath);
                return archiveFile.Entries.Any(e => !e.IsFolder && e.IsEncrypted);
            }
            return false;
        }
        catch (Exception ex)
        {
            // 无法检查时保守返回 true（宁可多弹密码输入框，不可静默跳过密码导致解压失败）
            LogDebug("HasEncryptedEntries: 无法检查压缩包 '{0}'，保守假定有加密: {1}", archivePath, ex.Message);
            return true;
        }
    }

    internal static bool QuickVerifyPassword(string archivePath, string password, IArchiveEngine engine)
    {
        try
        {
            if (engine is ZipEngine)
            {
                using var zipFile = ZipEngine.OpenZipFile(archivePath, password);
                var entry = zipFile.Cast<ZipEntry>().FirstOrDefault(e => e.IsCrypted || e.AESKeySize > 0);
                if (entry == null) return true; // 没有加密条目（理论上不会发生）
                using var s = zipFile.GetInputStream(entry);
                s.ReadByte(); // 密码不对会在此抛异常
                return true;
            }
            else if (engine is SevenZipEngine)
            {
                using var archiveFile = new global::SevenZipExtractor.ArchiveFile(archivePath, password);
                // 7z 构造器传入密码后，访问 Entries 即可验证密码
                _ = archiveFile.Entries.Count;
                return true;
            }
            // TarGzEngine 不支持加密
            return true;
        }
        catch (Exception ex) when (IsPasswordError(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// 判断异常是否表示需要密码。
    /// 与 <see cref="MainWindow.ExtractAsync"/> 中的逻辑保持一致。
    /// </summary>
    private static bool IsPasswordError(Exception ex)
    {
        var msg = ex.Message.ToLower();
        // 检查消息中是否包含密码/加密相关关键词。
        // 移除之前的 blanket InvalidOperationException 捕获，防止误判其他场景的异常。
        return msg.Contains("password") || msg.Contains(L.T(L.PwdMgr_Col_Password)) ||
               msg.Contains("encrypted") || msg.Contains("decrypt") ||
               msg.Contains("encryption") ||
               (ex is ZipException && (msg.Contains("password") || msg.Contains("decrypt")));
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
    /// 根据设置或用户选择返回解压目标目录。返回 null 表示用户取消。
    /// </summary>
    internal static string? ResolveExtractDestinationStatic(string archivePath, AppSettings settings)
    {
        var destSetting = settings.ExtractDestination;

        if (destSetting == "same-dir")
        {
            var dir = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(dir)) return dir;
        }
        else if (destSetting == "desktop")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        // "ask" 或未知值 → 弹出选择对话框
        var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
        {
            Description = "选择L.T(L.Settings_Tab_Extract)目录"
        };
        return dialog.ShowDialog() == true ? dialog.SelectedPath : null;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static void OpenInExplorerStatic(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start("explorer.exe", path);
        }
        catch (Exception explorerEx) { TraceLog("OpenInExplorerStatic: failed for '{0}': {1}", path, explorerEx.Message); }
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

    protected override void OnExit(ExitEventArgs e)
    {
        // 清理预览临时文件
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), L.T(L.App_MantisZipTitle));
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
                Log(L.T(L.App_CleanedPreviewTemp));
            }
        }
        catch (Exception exitEx) { LogDebug("OnExit: temp cleanup failed: {0}", exitEx.Message); }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// 日志文件大小上限（10 MB）。超过时自动轮转。
    /// </summary>
    private const long MaxLogFileSize = 10L * 1024 * 1024;

    /// <summary>
    /// 检查日志文件大小，超过上限时自动轮转（添加时间戳后缀）。
    /// 线程安全：仅在 try-catch 内单次调用，不强制跨方法同步（日志写入不要求强一致性）。
    /// </summary>
    private static void RotateDebugLogIfNeeded(string logPath)
    {
        try
        {
            var fileInfo = new FileInfo(logPath);
            if (fileInfo.Exists && fileInfo.Length > MaxLogFileSize)
            {
                var backupPath = logPath + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak";
                File.Move(logPath, backupPath);
            }
        }
        catch { /* 轮转失败不影响继续写入 */ }
    }

    /// <summary>
    /// 启动/操作日志（写入统一日志文件 %LOCALAPPDATA%\MantisZip\debug.log，不被自动删除）。
    /// 已应用隐私脱敏。
    /// </summary>
    private static void LogStartup(string msg)
    {
        try
        {
            // LogStartup 在 AppSettings 加载之后调用，可以安全读取配置
            var mode = AppSettings.Instance != null
                ? LogRedactor.ParseMode(AppSettings.Instance.LogPrivacyMode)
                : LogPrivacyMode.Off;
            var redacted = LogRedactor.RedactPaths(msg, mode);

            var dir = Path.GetDirectoryName(StartupLog);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            RotateDebugLogIfNeeded(StartupLog);
            File.AppendAllText(StartupLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {redacted}\n");
        }
        catch { }
    }

    public static void Log(string msg)
    {
        try
        {
            if (!AppSettings.Instance.EnableDebugLogging) return;
            var redacted = LogRedactor.RedactPaths(msg,
                LogRedactor.ParseMode(AppSettings.Instance.LogPrivacyMode));
            var dir = Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            RotateDebugLogIfNeeded(LogFile);
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {redacted}\n");
            Debug.WriteLine(redacted);
        }
        catch { }
    }

    public static void Log(string fmt, params object[] args)
    {
        Log(string.Format(fmt, args));
    }

    /// <summary>
    /// 调试日志：仅当 AppSettings.EnableDebugLogging 开启时写入。
    /// 用于用户开启后帮助排查问题。
    /// </summary>
    public static void LogDebug(string msg)
    {
        try
        {
            if (!AppSettings.Instance.EnableDebugLogging) return;
            var redacted = LogRedactor.RedactPaths(msg,
                LogRedactor.ParseMode(AppSettings.Instance.LogPrivacyMode));
            var dir = Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            RotateDebugLogIfNeeded(LogFile);
            File.AppendAllText(LogFile, $"[DBG] [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {redacted}\n");
            Debug.WriteLine(redacted);
        }
        catch { }
    }

    public static void LogDebug(string fmt, params object[] args)
    {
        LogDebug(string.Format(fmt, args));
    }

    /// <summary>
    /// 无条件写入统一日志文件的追踪日志（不依赖 EnableDebugLogging 设置）。
    /// 用于调试进度条等难以复现的问题。已应用隐私脱敏。
    /// </summary>
    public static void TraceLog(string msg)
    {
        try
        {
            var redacted = LogRedactor.RedactPaths(msg,
                LogRedactor.ParseMode(AppSettings.Instance.LogPrivacyMode));
            var dir = Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            RotateDebugLogIfNeeded(LogFile);
            File.AppendAllText(LogFile, $"[TRACE] [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {redacted}\n");
        }
        catch { }
    }

    public static void TraceLog(string fmt, params object[] args)
    {
        TraceLog(string.Format(fmt, args));
    }

    /// <summary>
    /// 应用亮色/暗色主题。替换 Application 级别的主题 ResourceDictionary，
    /// 并直接设置 SystemColors 覆盖（WPF XAML 中的 x:Static 键有时与控件模板查找不匹配）。
    /// </summary>
    public static void ApplyTheme(string themeName)
    {
        var uri = new Uri($"Themes/{themeName}.xaml", UriKind.Relative);
        var newDict = new ResourceDictionary { Source = uri };

        var existing = Current.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString?.StartsWith("Themes/") == true);

        if (existing != null)
            Current.Resources.MergedDictionaries.Remove(existing);

        Current.Resources.MergedDictionaries.Add(newDict);

        // SystemColors 覆盖（用 C# 保证 key 对象精准匹配）
        bool isDark = themeName == "Dark";
        var res = Current.Resources;
        if (isDark)
        {
            res[SystemColors.ControlBrushKey]              = new SolidColorBrush(Gray(0x2D));
            res[SystemColors.ControlTextBrushKey]          = new SolidColorBrush(Gray(0xD4));
            res[SystemColors.WindowBrushKey]               = new SolidColorBrush(Gray(0x1E));
            res[SystemColors.WindowTextBrushKey]           = new SolidColorBrush(Gray(0xD4));
            res[SystemColors.MenuBrushKey]                 = new SolidColorBrush(Gray(0x2D));
            res[SystemColors.MenuTextBrushKey]             = new SolidColorBrush(Gray(0xD4));
            res[SystemColors.MenuBarBrushKey]              = new SolidColorBrush(Gray(0x2D));
            res[SystemColors.HighlightBrushKey]            = new SolidColorBrush(Rgb(0x4F, 0xC3, 0xF7));
            res[SystemColors.HighlightTextBrushKey]        = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.InactiveSelectionHighlightBrushKey]  = new SolidColorBrush(Rgb(0x26, 0x4F, 0x78));
            res[SystemColors.InactiveSelectionHighlightTextBrushKey] = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.GrayTextBrushKey]             = new SolidColorBrush(Gray(0x6E));
            res[SystemColors.ActiveBorderBrushKey]         = new SolidColorBrush(Gray(0x3E));
            res[SystemColors.ControlLightBrushKey]         = new SolidColorBrush(Gray(0x3E));
            res[SystemColors.ControlLightLightBrushKey]    = new SolidColorBrush(Gray(0x4A));
            res[SystemColors.ControlDarkBrushKey]          = new SolidColorBrush(Gray(0x25));
            res[SystemColors.ControlDarkDarkBrushKey]      = new SolidColorBrush(Gray(0x1A));
            res[SystemColors.AppWorkspaceBrushKey]         = new SolidColorBrush(Gray(0x1E));
            res[SystemColors.InfoBrushKey]                 = new SolidColorBrush(Gray(0x3E));
            res[SystemColors.InfoTextBrushKey]             = new SolidColorBrush(Gray(0xD4));
            res[SystemColors.ScrollBarBrushKey]            = new SolidColorBrush(Gray(0x2D));
            // ColorKey 变体（有的控件模板用 Color 而非 Brush）
            res[SystemColors.WindowColorKey]               = Rgb(0x1E, 0x1E, 0x1E);
            res[SystemColors.ControlColorKey]              = Gray(0x2D);
            res[SystemColors.ControlTextColorKey]          = Gray(0xD4);
            res[SystemColors.WindowTextColorKey]           = Gray(0xD4);
            res[SystemColors.GrayTextColorKey]             = Gray(0x6E);
            res[SystemColors.MenuColorKey]                 = Gray(0x2D);
            res[SystemColors.MenuTextColorKey]             = Gray(0xD4);
            res[SystemColors.MenuBarColorKey]              = Gray(0x2D);
            res[SystemColors.HighlightColorKey]            = Rgb(0x4F, 0xC3, 0xF7);
            res[SystemColors.HighlightTextColorKey]        = Rgb(0xFF, 0xFF, 0xFF);
            res[SystemColors.ActiveBorderColorKey]         = Gray(0x3E);
            res[SystemColors.ControlLightColorKey]         = Gray(0x3E);
            res[SystemColors.ControlLightLightColorKey]    = Gray(0x4A);
            res[SystemColors.ControlDarkColorKey]          = Gray(0x25);
            res[SystemColors.ControlDarkDarkColorKey]      = Gray(0x1A);
            res[SystemColors.AppWorkspaceColorKey]         = Gray(0x1E);
            res[SystemColors.InfoColorKey]                 = Gray(0x3E);
            res[SystemColors.InfoTextColorKey]             = Gray(0xD4);
            res[SystemColors.ScrollBarColorKey]            = Gray(0x2D);
        }
        else
        {
            res[SystemColors.ControlBrushKey]              = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.ControlTextBrushKey]          = new SolidColorBrush(Gray(0x33));
            res[SystemColors.WindowBrushKey]               = new SolidColorBrush(Rgb(0xF5, 0xF5, 0xF5));
            res[SystemColors.WindowTextBrushKey]           = new SolidColorBrush(Gray(0x33));
            res[SystemColors.MenuBrushKey]                 = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.MenuTextBrushKey]             = new SolidColorBrush(Gray(0x33));
            res[SystemColors.MenuBarBrushKey]              = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.HighlightBrushKey]            = new SolidColorBrush(Rgb(0x21, 0x96, 0xF3));
            res[SystemColors.HighlightTextBrushKey]        = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.InactiveSelectionHighlightBrushKey]  = new SolidColorBrush(Rgb(0xCC, 0xE0, 0xF0));
            res[SystemColors.InactiveSelectionHighlightTextBrushKey] = new SolidColorBrush(Rgb(0x00, 0x00, 0x00));
            res[SystemColors.GrayTextBrushKey]             = new SolidColorBrush(Gray(0x88));
            res[SystemColors.ActiveBorderBrushKey]         = new SolidColorBrush(Rgb(0xCC, 0xCC, 0xCC));
            res[SystemColors.ControlLightBrushKey]         = new SolidColorBrush(Rgb(0xE0, 0xE0, 0xE0));
            res[SystemColors.ControlLightLightBrushKey]    = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.ControlDarkBrushKey]          = new SolidColorBrush(Rgb(0xC0, 0xC0, 0xC0));
            res[SystemColors.ControlDarkDarkBrushKey]      = new SolidColorBrush(Gray(0x80));
            res[SystemColors.AppWorkspaceBrushKey]         = new SolidColorBrush(Rgb(0xF5, 0xF5, 0xF5));
            res[SystemColors.InfoBrushKey]                 = new SolidColorBrush(Rgb(0xFF, 0xFF, 0xFF));
            res[SystemColors.InfoTextBrushKey]             = new SolidColorBrush(Gray(0x33));
            res[SystemColors.ScrollBarBrushKey]            = new SolidColorBrush(Rgb(0xF0, 0xF0, 0xF0));
            // ColorKey 变体（有的控件模板用 Color 而非 Brush）
            res[SystemColors.WindowColorKey]               = Rgb(0xF5, 0xF5, 0xF5);
            res[SystemColors.ControlColorKey]              = Rgb(0xFF, 0xFF, 0xFF);
            res[SystemColors.ControlTextColorKey]          = Gray(0x33);
            res[SystemColors.WindowTextColorKey]           = Gray(0x33);
            res[SystemColors.GrayTextColorKey]             = Gray(0x88);
            res[SystemColors.MenuColorKey]                 = Rgb(0xFF, 0xFF, 0xFF);
            res[SystemColors.MenuTextColorKey]             = Gray(0x33);
            res[SystemColors.MenuBarColorKey]              = Rgb(0xFF, 0xFF, 0xFF);
            res[SystemColors.HighlightColorKey]            = Rgb(0x21, 0x96, 0xF3);
            res[SystemColors.HighlightTextColorKey]        = Rgb(0xFF, 0xFF, 0xFF);
            res[SystemColors.ActiveBorderColorKey]         = Rgb(0xCC, 0xCC, 0xCC);
            res[SystemColors.ControlLightColorKey]         = Rgb(0xE0, 0xE0, 0xE0);
            res[SystemColors.ControlLightLightColorKey]    = Rgb(0xFF, 0xFF, 0xFF);
            res[SystemColors.ControlDarkColorKey]          = Rgb(0xC0, 0xC0, 0xC0);
            res[SystemColors.ControlDarkDarkColorKey]      = Gray(0x80);
            res[SystemColors.AppWorkspaceColorKey]         = Rgb(0xF5, 0xF5, 0xF5);
            res[SystemColors.InfoColorKey]                 = Rgb(0xFF, 0xFF, 0xFF);
            res[SystemColors.InfoTextColorKey]             = Gray(0x33);
            res[SystemColors.ScrollBarColorKey]            = Rgb(0xF0, 0xF0, 0xF0);
        }
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static Color Gray(byte g) => Color.FromRgb(g, g, g);

    /// <summary>
    /// 在指定元素上应用彩色 Emoji 渲染模式（Grayscale vs ClearType）。
    /// 根据 AppSettings.UseColorEmoji 切换。
    /// </summary>
    public static void ApplyTextRenderingMode(DependencyObject element)
    {
        if (element == null) return;
        TextOptions.SetTextRenderingMode(element,
            AppSettings.Instance.UseColorEmoji
                ? TextRenderingMode.Grayscale
                : TextRenderingMode.ClearType);
    }

    /// <summary>
    /// 简单输入框对话框，用于 --compress-combined 无公共父目录时让用户输入压缩包名称。
    /// </summary>
    internal class ArchiveNameDialog : Window
    {
        public string? ArchiveName { get; private set; }
        private readonly TextBox _textBox;

        public ArchiveNameDialog(string defaultName)
        {
            Title = L.T(L.App_CompressCombinedPromptTitle);
            Width = 400; Height = 160;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;

            var stack = new StackPanel { Margin = new Thickness(12) };

            var label = new TextBlock
            {
                Text = L.T(L.App_CompressCombinedPromptLabel),
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(label);

            _textBox = new TextBox
            {
                Text = defaultName,
                Margin = new Thickness(0, 0, 0, 12)
            };
            _textBox.SelectAll();
            _textBox.Focus();
            stack.Children.Add(_textBox);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okBtn = new Button
            {
                Content = L.T(L.MsgBox_Ok),
                Width = 80, Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            okBtn.Click += (_, _) =>
            {
                ArchiveName = _textBox.Text.Trim();
                if (!string.IsNullOrEmpty(ArchiveName))
                    DialogResult = true;
            };
            btnPanel.Children.Add(okBtn);

            var cancelBtn = new Button
            {
                Content = L.T(L.MsgBox_Cancel),
                Width = 80, Height = 28,
                IsCancel = true
            };
            btnPanel.Children.Add(cancelBtn);

            stack.Children.Add(btnPanel);
            Content = stack;
        }
    }
}
