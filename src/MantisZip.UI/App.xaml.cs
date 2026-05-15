using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core;
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
            LogDebug("InitializeApp: failed to load AppSettings: {0}", initEx.Message);
            // 使用默认 7z 路径继续运行
        }
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

                    case "--install-assoc":
                        ShellIntegration.InstallAssociations();
                        MessageBox.Show("文件关联已安装。\n\n现在双击 .zip/.7z/.rar 等文件将默认用 MantisZip 打开。",
                            "MantisZip", MessageBoxButton.OK, MessageBoxImage.Information);
                        Shutdown();
                        return;

                    case "--uninstall-assoc":
                        ShellIntegration.UninstallAssociations();
                        MessageBox.Show("文件关联已卸载", "MantisZip",
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

    #region 共享解压逻辑

    /// <summary>
    /// 从已保存密码中匹配并快速验证。返回 (密码, 描述) 或 null。
    /// </summary>
    internal static (string Password, string Description)? TryMatchPassword(
        string archivePath, IArchiveEngine engine, ProgressWindow? progressWindow,
        bool showPwdSection)
    {
        var savedPasswords = PasswordManager.Instance.FindMatchingPasswords(archivePath);
        var tried = new HashSet<string>();

        foreach (var entry in savedPasswords)
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
            PasswordManager.Instance.AddPassword(password, saveDesc, savePatterns);
        }
        return true;
    }

    #endregion

    /// <summary>
    /// 公共解压逻辑：获取引擎、显示进度窗口、执行解压，完成后退出。
    /// 支持从 PasswordManager 加载已保存密码，以及在加密时弹出密码输入框。
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

        var baseProgress = new Progress<ArchiveProgress>(p =>
        {
            progressWindow.Dispatcher.BeginInvoke(() =>
                progressWindow.SetProgress(p));
        });
        var progress = progressWindow.CreatePauseAwareProgress(baseProgress);

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
                var match = TryMatchPassword(archivePath, engine, progressWindow, showPwd);
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
                        progressWindow.SetComplete("解压完成");
                    });
                    if (settings.OpenFolderAfterExtract) OpenInExplorerStatic(dest);
                    await Task.Delay(2500);
                    await progressWindow.Dispatcher.InvokeAsync(() => appRef.Shutdown());
                    return;
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
                        progressWindow.SetComplete("解压完成");
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

                // QuickVerify + 解压（带保存密码）
                bool showPwdManual = AppSettings.Instance.ShowPasswordMatchNotification;
                if (!await ExtractWithPasswordAsync(archivePath, dest, engine, userPwd, "手动输入",
                        progressWindow, progress, progressWindow.CancellationToken, showPwdManual, remember,
                        pwdDesc, pwdPatterns))
                {
                    await progressWindow.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("密码错误", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        appRef.Shutdown();
                    });
                    return;
                }

                await progressWindow.Dispatcher.InvokeAsync(() =>
                {
                    appRef.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    progressWindow.SetComplete("解压完成");
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
                    MessageBox.Show($"解压失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    appRef.Shutdown();
                });
            }
        });
    }

    /// <summary>
    /// 从 AppSettings 读取解压冲突策略。
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
                // 已勾选"应用到全部" → 直接返回记忆的选择
                if (applyToAll && chosenAction.HasValue)
                    return chosenAction.Value;

                // 调度到 UI 线程显示模态对话框
                var dispatcher = Current?.Dispatcher;
                if (dispatcher == null) return FileConflictAction.Overwrite;

                var result = dispatcher.Invoke(() =>
                {
                    var dialog = new ConflictDialog(info);
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
    /// </summary>
    private static string GetUniquePath(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
        return path;
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
    /// 快速验证密码是否正确——读第一个加密条目 1 字节，
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
                using var zipFile = new ZipFile(archivePath);
                return zipFile.Cast<ZipEntry>().Any(e => e.IsCrypted || e.AESKeySize > 0);
            }
            if (engine is SevenZipEngine)
            {
                using var archiveFile = new global::SevenZipExtractor.ArchiveFile(archivePath);
                return archiveFile.Entries.Any(e => !e.IsFolder && e.IsEncrypted);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    internal static bool QuickVerifyPassword(string archivePath, string password, IArchiveEngine engine)
    {
        try
        {
            if (engine is ZipEngine)
            {
                using var zipFile = new ZipFile(archivePath);
                zipFile.Password = password;
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
        return msg.Contains("password") || msg.Contains("密码") ||
               msg.Contains("encrypted") || msg.Contains("decrypt") ||
               msg.Contains("encryption") || ex is InvalidOperationException ||
               (ex is ZipException && (msg.Contains("password") || msg.Contains("decrypt")));
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
            Description = "选择解压目录"
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
        catch (Exception explorerEx) { LogDebug("OpenInExplorerStatic: failed for '{0}': {1}", path, explorerEx.Message); }
    }

    /// <summary>
    /// 处理 --compress-quick 模式：不显示设置窗口，使用 AppSettings 默认值直接压缩。
    /// </summary>
    private static void HandleCompressQuick(string[] paths)
    {
        LogStartup($"HandleCompressQuick: paths=[{string.Join(";", paths)}]");
        var app = Current; // Application.Current, never null after startup
        if (app == null) return;

        var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (myPaths.Count == 0)
        {
            app.Shutdown();
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

        // 目标文件已存在 → 弹冲突对话框
        string finalPath = outputPath;
        bool addMode = false;
        if (File.Exists(outputPath))
        {
            var engine = ArchiveEngineFactory.GetEngineByExtension(outputPath);
            bool canAdd = engine is not null and not TarGzEngine;
            var dispatcher = app.Dispatcher;
            if (dispatcher == null) { app.Shutdown(); return; }

            var conflictResult = dispatcher.Invoke(() =>
            {
                var dlg = new CompressConflictDialog(outputPath, canAdd);
                return dlg.ShowDialog() == true ? dlg.ResultAction : CompressConflictAction.Cancel;
            });

            switch (conflictResult)
            {
                case CompressConflictAction.Cancel:
                    app.Shutdown();
                    return;
                case CompressConflictAction.Rename:
                    finalPath = GetUniquePath(outputPath);
                    break;
                case CompressConflictAction.Add:
                    addMode = true;
                    break;
                case CompressConflictAction.Overwrite:
                default:
                    break;
            }
        }

        // 选择添加到已存在的压缩包
        if (addMode)
        {
            var addEngine = ArchiveEngineFactory.GetEngineByExtension(finalPath);
            if (addEngine == null) { app.Shutdown(); return; }

            var pw = new ProgressWindow();
            pw.InitCancellation();
            pw.Show();
            pw.SetProgress(0, "正在添加到压缩包...");

            var addProgress = new Progress<ArchiveProgress>(p =>
            {
                pw.Dispatcher.BeginInvoke(() => pw.SetProgress(p));
            });

            Task.Run(async () =>
            {
                try
                {
                    await addEngine.AddToArchiveAsync(finalPath, myPaths.ToArray(),
                        new ArchiveOptions { CompressionLevel = settings.DefaultLevel },
                        addProgress, pw.CancellationToken);
                    await pw.Dispatcher.InvokeAsync(() => pw.SetComplete("添加完成"));
                    await Task.Delay(800);
                    await pw.Dispatcher.InvokeAsync(() => app.Shutdown());
                }
                catch (OperationCanceledException)
                {
                    await pw.Dispatcher.InvokeAsync(() => app.Shutdown());
                }
                catch (Exception ex)
                {
                    Log("--compress-quick add failed: {0}", ex.Message);
                    await pw.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"添加到压缩包失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        app.Shutdown();
                    });
                }
            });
            return;
        }

        // 直接压缩
        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();
        progressWindow.SetProgress(0, "准备压缩...");

        var options = App.CreateCompressOptions();
        options.CompressionLevel = settings.DefaultLevel;

        var progress = new Progress<ArchiveProgress>(p =>
        {
            progressWindow.Dispatcher.BeginInvoke(() =>
                progressWindow.SetProgress(p));
        });

        var compressEngine = ArchiveEngineFactory.GetEngineByExtension(finalPath) ?? new ZipEngine();
        Log("--compress-quick: {0} → {1}", string.Join(", ", myPaths), finalPath);

        // 异步压缩，完成后自动退出
        Task.Run(async () =>
        {
            try
            {
                await compressEngine.CompressAsync(myPaths.ToArray(), finalPath, options, progress, progressWindow.CancellationToken);
                await progressWindow.Dispatcher.InvokeAsync(() => progressWindow.SetComplete("压缩完成"));
                await Task.Delay(800);
                await progressWindow.Dispatcher.InvokeAsync(() => app.Shutdown());
            }
            catch (OperationCanceledException)
            {
                await progressWindow.Dispatcher.InvokeAsync(() => app.Shutdown());
            }
            catch (Exception ex)
            {
                Log("--compress-quick 失败: {0}", ex.Message);
                await progressWindow.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"压缩失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    app.Shutdown();
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
            if (!AppSettings.Instance.EnableDebugLogging) return;
            var dir = Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
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
    /// 调试日志：仅当 AppSettings.EnableDebugLogging 开启时写入。
    /// 用于用户开启后帮助排查问题。
    /// </summary>
    public static void LogDebug(string msg)
    {
        try
        {
            if (!AppSettings.Instance.EnableDebugLogging) return;
            var dir = Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(LogFile, $"[DBG] [{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            Debug.WriteLine(msg);
        }
        catch { }
    }

    public static void LogDebug(string fmt, params object[] args)
    {
        LogDebug(string.Format(fmt, args));
    }
}
