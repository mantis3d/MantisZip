using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;

namespace MantisZip.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
    private static readonly string StartupLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip", "startup.log");
    private static Mutex? _instanceMutex;

    private const string CompressMutexName = "MantisZip-CompressMutex";
    private const string CompressPipeName = "MantisZip-Compress";

    /// <summary>
    /// 全局初始化：所有入口（主窗口、--compress、--extract 等）都先经过这里。
    /// 用于设置编码、全局静态变量等。
    /// </summary>
    private static void InitializeApp()
    {
        // ZIP 中文文件名编码支持（进程级）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#pragma warning disable CS0618 // StringCodec public API not available in SharpZipLib 1.4.0
        ZipStrings.CodePage = 936; // GBK
#pragma warning restore CS0618

        // 后续若有其他全局初始化（如 7z 路径检测、临时目录清理等）加在这里
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局初始化（所有入口最先运行）
        InitializeApp();

        // ===== 持久化启动日志（独立于 debug.log，不被删除）=====
        LogStartup($"START BaseDir={AppDomain.CurrentDomain.BaseDirectory} Args=[{string.Join(" ", e.Args)}]");

        // 日志（WPF 级别，每次启动清空）
        var listener = new TextWriterTraceListener(LogFile);
        listener.Name = "FileLogger";
        Trace.Listeners.Add(listener);
        Trace.AutoFlush = true;

        // DEBUG 模式下不清除日志文件，保留调试信息
        // RELEASE 模式下每次启动清空
#if !DEBUG
        try { File.Delete(LogFile); } catch { }
#endif
        LogDebug("LogDebug: debug.log will be appended in DEBUG mode");

        Log("启动参数: {0}", string.Join(" ", e.Args));

        try
        {
            if (e.Args.Length > 0)
            {
                switch (e.Args[0])
                {
                    case "--install-shell":
                        ShellIntegration.Install();
                        MessageBox.Show(
                            "Shell 右键菜单已安装。\n\n" +
                            "• 右键压缩包 → 打开压缩包 / 解压到此处 / 解压到压缩包名 / 解压到……\n" +
                            "• 右键任意文件/文件夹 → 压缩为（文件名）.zip / 压缩",
                            "MantisZip", MessageBoxButton.OK, MessageBoxImage.Information);
                        Shutdown();
                        return;

                    case "--uninstall-shell":
                        ShellIntegration.Uninstall();
                        MessageBox.Show("Shell 右键菜单已卸载", "MantisZip",
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

                    case "--compress-quick":
                        HandleCompressQuick(e.Args.Skip(1).ToArray());
                        return;

                    case "--test":
                        LogStartup("--test 模式：应用启动成功");
                        MessageBox.Show(
                            $"应用启动成功\n\n当前目录: {AppDomain.CurrentDomain.BaseDirectory}\n启动日志: {StartupLog}",
                            "MantisZip 测试", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show($"启动失败: {ex.Message}", "MantisZip 错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // 正常启动：手动创建主窗口（已移除 StartupUri）
        var mainWin = new MainWindow();
        mainWin.Show();
        base.OnStartup(e);
        Log("程序启动");
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
            StartCompressPipeServer(allPaths, cts.Token);

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
                    MessageBox.Show($"启动压缩窗口失败: {ex.Message}", "MantisZip 错误",
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
        ThreadPool.QueueUserWorkItem(async _ =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        CompressPipeName, PipeDirection.In, -1,
                        PipeTransmissionMode.Message, PipeOptions.Asynchronous);

                    await pipe.WaitForConnectionAsync(ct);
                    using var reader = new StreamReader(pipe);
                    var line = await reader.ReadLineAsync();
                    while (line != null)
                    {
                        lock (allPaths)
                        {
                            if (!allPaths.Contains(line) && (File.Exists(line) || Directory.Exists(line)))
                                allPaths.Add(line);
                        }
                        line = await reader.ReadLineAsync();
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception pipeEx) { LogDebug("StartCompressPipeServer: connection error: {0}", pipeEx.Message); }
            }
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

    /// <summary>
    /// 处理 --extract-here 模式：解压到压缩包所在目录。
    /// </summary>
    private static void HandleExtractHere(string? archivePath)
    {
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
        {
            MessageBox.Show("要解压的文件不存在", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
            return;
        }

        // 解压到压缩包所在目录
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
            MessageBox.Show("要解压的文件不存在", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
            return;
        }

        // 解压到 压缩包所在目录\压缩包名称\ 下
        var parentDir = Path.GetDirectoryName(archivePath);
        var archiveName = Path.GetFileNameWithoutExtension(archivePath);
        if (string.IsNullOrEmpty(parentDir))
            parentDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var dest = Path.Combine(parentDir, archiveName);

        RunExtractStatic(archivePath, dest);
    }

    /// <summary>
    /// 处理 --extract 模式：根据设置或用户选择解压，不经过主窗口。
    /// </summary>
    private static void HandleExtract(string? archivePath)
    {
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
        {
            MessageBox.Show("要解压的文件不存在", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
            return;
        }

        var settings = AppSettings.Instance;

        // 确定解压目标目录
        var dest = ResolveExtractDestinationStatic(archivePath, settings);
        if (dest == null)
        {
            Current.Shutdown();
            return;
        }

        RunExtractStatic(archivePath, dest);
    }

    /// <summary>
    /// 公共解压逻辑：获取引擎、显示进度窗口、执行解压，完成后退出。
    /// </summary>
    private static void RunExtractStatic(string archivePath, string dest)
    {
        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        if (engine == null)
        {
            MessageBox.Show("不支持的压缩格式", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
            return;
        }

        var settings = AppSettings.Instance;

        // 显示进度窗口
        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();
        progressWindow.SetProgress(0, "正在解压...");

        var progress = new Progress<ArchiveProgress>(p =>
        {
            progressWindow.Dispatcher.BeginInvoke(() =>
                progressWindow.SetProgress(p));
        });

        Log("--extract: {0} → {1}", archivePath, dest);

        // 后台解压，完成后自动退出
        Task.Run(async () =>
        {
            try
            {
                await engine.ExtractAsync(archivePath, dest, null, progress, progressWindow.CancellationToken);
                await progressWindow.Dispatcher.InvokeAsync(() => progressWindow.SetComplete("解压完成"));

                if (settings.OpenFolderAfterExtract)
                    OpenInExplorerStatic(dest);

                await Task.Delay(800);
                await progressWindow.Dispatcher.InvokeAsync(() => Current.Shutdown());
            }
            catch (OperationCanceledException)
            {
                await progressWindow.Dispatcher.InvokeAsync(() => Current.Shutdown());
            }
            catch (Exception ex)
            {
                Log("--extract 失败: {0}", ex.Message);
                await progressWindow.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"解压失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    Current.Shutdown();
                });
            }
        });
    }

    /// <summary>
    /// 处理 --open 模式：启动主窗口并加载压缩包供浏览。
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
    private static string? ResolveExtractDestinationStatic(string archivePath, AppSettings settings)
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
            Description = "选择解压目录"
        };
        return dialog.ShowDialog() == true ? dialog.SelectedPath : null;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void OpenInExplorerStatic(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start("explorer.exe", path);
        }
        catch (Exception explorerEx) { LogDebug("OpenInExplorerStatic: failed for '{0}': {1}", path, explorerEx.Message); }
    }

    /// <summary>
    /// 处理 --compress-quick 模式：不显示设置窗口，使用 AppSettings 默认值直接压缩。
    /// </summary>
    private static void HandleCompressQuick(string[] paths)
    {
        LogStartup($"HandleCompressQuick: paths=[{string.Join(";", paths)}]");
        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (myPaths.Count == 0)
        {
            Current.Shutdown();
            return;
        }

        var settings = AppSettings.Instance;

        // 自动确定输出路径：与第一个源文件/目录同目录
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

        // 直接显示进度窗口并压缩
        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();
        progressWindow.SetProgress(0, "准备压缩...");

        var options = new ArchiveOptions
        {
            CompressionLevel = settings.DefaultLevel,
            Encrypt = false
        };

        var progress = new Progress<ArchiveProgress>(p =>
        {
            progressWindow.Dispatcher.BeginInvoke(() =>
                progressWindow.SetProgress(p));
        });

        var engine = ArchiveEngineFactory.GetEngineByExtension(outputPath) ?? new ZipEngine();
        Log("--compress-quick: {0} → {1}", string.Join(", ", myPaths), outputPath);

        // 异步压缩，完成后自动退出
        Task.Run(async () =>
        {
            try
            {
                await engine.CompressAsync(myPaths.ToArray(), outputPath, options, progress, progressWindow.CancellationToken);
                await progressWindow.Dispatcher.InvokeAsync(() => progressWindow.SetComplete("压缩完成"));
                await Task.Delay(800);
                await progressWindow.Dispatcher.InvokeAsync(() => Current.Shutdown());
            }
            catch (OperationCanceledException)
            {
                await progressWindow.Dispatcher.InvokeAsync(() => Current.Shutdown());
            }
            catch (Exception ex)
            {
                Log("--compress-quick 失败: {0}", ex.Message);
                await progressWindow.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"压缩失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    Current.Shutdown();
                });
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 清理预览临时文件
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "MantisZip");
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
                Log("已清理预览临时目录");
            }
        }
        catch (Exception exitEx) { LogDebug("OnExit: temp cleanup failed: {0}", exitEx.Message); }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// 持久化启动日志（写入 %LOCALAPPDATA%\MantisZip\startup.log，不被删除）。
    /// </summary>
    private static void LogStartup(string msg)
    {
        try
        {
            var dir = Path.GetDirectoryName(StartupLog);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(StartupLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    public static void Log(string msg)
    {
        try
        {
            File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            Debug.WriteLine(msg);
        }
        catch { }
    }

    public static void Log(string fmt, params object[] args)
    {
        Log(string.Format(fmt, args));
    }

    /// <summary>
    /// DEBUG-only 日志：编译到 RELEASE 中会消失（如同 CoreLog）。
    /// 用于调试期间捕获细粒度信息而不影响 RELEASE 性能。
    /// </summary>
    [Conditional("DEBUG")]
    public static void LogDebug(string msg)
    {
        try
        {
            File.AppendAllText(LogFile, $"[DBG] [{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    [Conditional("DEBUG")]
    public static void LogDebug(string fmt, params object[] args)
    {
        LogDebug(string.Format(fmt, args));
    }
}
