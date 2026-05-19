using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
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
        L.T(L.App_MantisZipTitle), "debug.log");
    // StartupLog 已合并到 LogFile（同一个文件）
    private static string StartupLog => LogFile;
    private static Mutex? _instanceMutex;

    private static string CompressMutexName = "MantisZip-CompressMutex";
    private static string CompressPipeName = "MantisZip-Compress";

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
            LogDebug("InitializeApp: failed to load AppSettings: {0}", initEx.Message);
            // 使用默认 7z 路径继续运行
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局初始化（所有入口最先运行）
        InitializeApp();

        // 初始化语言管理器（需在 InitializeApp 之后，因为初始化加载 AppSettings）
        LanguageManager.Instance.Initialize();

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

        Log("启动参数: {0}", string.Join(" ", e.Args));

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

                    case "--compress-quick":
                        HandleCompressQuick(e.Args.Skip(1).ToArray());
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
    /// 处理 --extract 模式：根据设置或用户选择解压，不经过主窗口。
    /// </summary>
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
                        progressWindow.SetComplete(L.T(L.App_ExtractComplete));
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
        catch (Exception explorerEx) { LogDebug("OpenInExplorerStatic: failed for '{0}': {1}", path, explorerEx.Message); }
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
            File.AppendAllText(LogFile, $"[TRACE] [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {redacted}\n");
        }
        catch { }
    }

    public static void TraceLog(string fmt, params object[] args)
    {
        TraceLog(string.Format(fmt, args));
    }

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
}
