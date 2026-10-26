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
/// App 的 CLI 解压族部分（自 App.axaml.cs 拆分，纯移动）。
/// </summary>
public partial class App : Application
{
    // ════════════════════════════════════════════════════════════════
    //  Extract helpers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// 为 CLI 解压解析密码：从 PasswordManager 查找已保存密码并快速验证。
    /// </summary>
    private static string? ResolveCliPassword(string archivePath, IArchiveEngine engine)
    {
        try
        {
            var service = new PasswordService();

            // 方案 A：非加密包不做密码匹配。否则 QuickVerifyPassword 在包无加密条目时对任意密码
            // 恒返回 true → 误判「密码匹配成功」→ 进度窗口批处理行点亮只应在加密包出现的 🔑 徽标。
            if (!service.HasEncryptedEntries(archivePath, engine))
            {
                DebugLog($"CLI extract: '{archivePath}' has no encrypted entries, skipping password matching");
                return null;
            }

            var allMatches = PasswordManager.Instance.FindMatchingPasswords(archivePath);
            foreach (var entry in allMatches)
            {
                var pwd = entry.Password;
                if (string.IsNullOrEmpty(pwd)) continue;

                var verifyInfo = service.QuickVerifyPasswordEx(archivePath, pwd, engine);
                if (verifyInfo.Result == PasswordVerificationResult.Success)
                {
                    DebugLog($"CLI extract: saved password matched for '{archivePath}'");
                    return pwd;
                }
                if (verifyInfo.Result == PasswordVerificationResult.CorruptedOrInvalid)
                {
                    DebugLog($"CLI extract: archive corrupted, stopping password attempts for '{archivePath}'");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog($"CLI extract: password lookup failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// 内联密码库查找：在匹配 <paramref name="archivePath"/> 规则命中的条目中按明文密码反查，
    /// 取其匹配规则（Patterns 拼接）与描述，供进度窗口批处理行的密码徽标 Flyout 展示。
    /// 镜像 <c>MainWindowViewModel.FindSavedPasswordEntry</c>（private static，不可跨类调用）的容错；
    /// 查找失败返回 (null, null)，不阻断解压（Flyout 对应行自动隐藏）。
    /// </summary>
    private static (string? Rule, string? Description) LookupPasswordRuleAndDescription(
        string archivePath, string? password)
    {
        if (string.IsNullOrEmpty(password))
            return (null, null);
        try
        {
            var entry = PasswordManager.Instance.FindMatchingPasswords(archivePath)
                .FirstOrDefault(e => e.Password == password);
            if (entry == null)
                return (null, null);
            var rule = entry.Patterns is { Count: > 0 } ? string.Join("; ", entry.Patterns) : null;
            return (rule, entry.Description);
        }
        catch (Exception ex)
        {
            DebugLog($"FindMatchingPasswords failed: {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>
    /// 带提权支持的异步解压。先检查目标目录可写性，权限不足时弹出提权对话框。
    /// 自动尝试已保存密码。
    /// </summary>
    private static async Task<bool> TryExtractArchiveAsync(
        string archivePath,
        string targetDir,
        string[] originalArgs,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            // Pre-check target directory writability
            var unwritable = new List<string>();
            if (!IsDirectoryWritable(targetDir))
                unwritable.Add(targetDir);

            if (unwritable.Count > 0)
            {
                var restarted = await HandleElevationAsync(unwritable, originalArgs, desktop);
                // If user chose to restart as admin, we return without extracting.
                // If user declined or already elevated, we still return (can't proceed).
                return restarted;
            }

            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            if (engine == null)
            {
                Console.Error.WriteLine($"Unsupported archive format: {archivePath}");
                return false;
            }

            var password = ResolveCliPassword(archivePath, engine);
            return await RunCliExtractWithProgressAsync(
                archivePath, targetDir, password, engine, originalArgs, desktop);
        }
        catch (UnauthorizedAccessException)
        {
            return await HandleElevationAsync(new[] { targetDir }, originalArgs, desktop);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Extraction failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 带提权支持的异步智能解压。先分析目标目录可写性，权限不足时弹出提权对话框。
    /// 解压成功后根据 DeleteArchiveAfterExtract 设置尝试删除原包。
    /// 自动尝试已保存密码。
    /// </summary>
    private static async Task<bool> TryExtractSmartAsync(
        string archivePath,
        string[] originalArgs,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        string targetDir = string.Empty;
        try
        {
            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            if (engine == null)
            {
                Console.Error.WriteLine($"Unsupported archive format: {archivePath}");
                return false;
            }

            // 分析压缩包结构并确定目标目录（单根 → 压缩包所在目录；散列 → 命名子目录）
            targetDir = await ResolveSmartDestCliAsync(archivePath, engine);

            // Check writability before extraction
            var unwritable = new List<string>();
            if (!IsDirectoryWritable(targetDir))
                unwritable.Add(targetDir);

            if (unwritable.Count > 0)
                return await HandleElevationAsync(unwritable, originalArgs, desktop);

            var password = ResolveCliPassword(archivePath, engine);
            return await RunCliExtractWithProgressAsync(
                archivePath, targetDir, password, engine, originalArgs, desktop);
        }
        catch (UnauthorizedAccessException)
        {
            return !string.IsNullOrEmpty(targetDir)
                ? await HandleElevationAsync(new[] { targetDir }, originalArgs, desktop)
                : false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Smart extraction failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 智能解压目标目录计算（单文件与批处理共用）：
    /// 单根目录结构 → 压缩包所在目录；散列结构 → 压缩包名命名的子目录（自动创建）。
    /// 语义与 WPF ResolveSmartDestAsync 一致。
    /// </summary>
    private static async Task<string> ResolveSmartDestCliAsync(string archivePath, IArchiveEngine engine)
    {
        var items = await engine.ListEntriesAsync(archivePath);
        var hasSingleRoot = ArchiveStructureAnalyzer.HasSingleRootDirectory(items);

        if (hasSingleRoot)
        {
            // Single root folder: extract to the archive's directory (压缩包所在目录), not the process working directory
            Console.WriteLine("SmartExtract: single root detected, extracting to archive directory");
            return Path.GetDirectoryName(archivePath) ?? ".";
        }

        // Dispersed files: extract to named subfolder
        var targetDir = Path.Combine(
            Path.GetDirectoryName(archivePath) ?? ".",
            GetArchiveBaseName(archivePath));
        Directory.CreateDirectory(targetDir);
        Console.WriteLine($"SmartExtract: dispersed structure, extracting to {targetDir}");
        return targetDir;
    }

    /// <summary>
    /// 尝试在解压后删除原始压缩包（移动到回收站）。
    /// enabledOverride 非 null 时优先使用（解压窗口逐次勾选，不读全局设置、不写回）；
    /// null 时读 AppSettings.DeleteArchiveAfterExtract。
    /// 基于 DoubleClickOpenThreshold 的文件大小检查由 UI 侧的点击事件完成，
    /// CLI 模式不做额外大小检查。
    /// 重试 3 次（200ms 间隔），给 7z.dll 等外部组件释放文件句柄的时间。
    /// </summary>
    internal static void TryDeleteArchiveAfterExtract(string archivePath, bool? enabledOverride = null)
    {
        var settings = AppSettings.Load();
        var enabled = enabledOverride ?? settings.DeleteArchiveAfterExtract;
        if (!enabled) return;
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath)) return;

        for (int retry = 0; retry < 3; retry++)
        {
            try
            {
                FileSystem.DeleteFile(
                    archivePath,
                    UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin);
                DebugLog($"TryDeleteArchiveAfterExtract: moved '{archivePath}' to recycle bin");
                return;
            }
            catch (Exception ex) when (retry < 2)
            {
                DebugLog($"TryDeleteArchiveAfterExtract: attempt {retry + 1} failed for '{archivePath}': {ex.Message}");
                Thread.Sleep(200);
            }
            catch (Exception ex)
            {
                DebugLog($"TryDeleteArchiveAfterExtract: failed for '{archivePath}' after 3 attempts: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 启动时清理临时目录（对齐 WPF App.xaml.cs:141 CleanTempOnStartup）。
    /// 读取 AppSettings.CleanTempOnStartup，为 true 时递归删除临时目录。
    /// 失败静默（catch 后 DebugLog），绝不让启动崩溃；并发实例占用导致删除失败是正常情况。
    /// </summary>
    private static void CleanTempOnStartupCore()
    {
        try
        {
            var settings = AppSettings.Load();
            if (!settings.CleanTempOnStartup) return;

            var tempDir = AppSettings.GetTempDir();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
                DebugLog($"CleanTempOnStartupCore: deleted temp directory '{tempDir}'");
            }
        }
        catch (Exception ex)
        {
            DebugLog($"CleanTempOnStartupCore: failed to clean temp directory: {ex.Message}");
        }
    }

    /// <summary>
    /// CLI 解压入口包装：调用 TryExtractArchiveAsync，完成后 shutdown。
    /// </summary>
    private static async Task RunExtractCliAsync(
        string archivePath,
        string targetDir,
        string[] originalArgs,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var restarted = await TryExtractArchiveAsync(archivePath, targetDir, originalArgs, desktop);
        if (!restarted)
            AppShutdown.Request(desktop.Shutdown);
    }

    /// <summary>
    /// CLI 智能解压入口包装：调用 TryExtractSmartAsync，完成后 shutdown。
    /// </summary>
    private static async Task RunExtractSmartCliAsync(
        string archivePath,
        string[] originalArgs,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var restarted = await TryExtractSmartAsync(archivePath, originalArgs, desktop);
        if (!restarted)
            AppShutdown.Request(desktop.Shutdown);
    }

    /// <summary>
    /// CLI --extract 弹窗解压入口（对齐 WPF HandleExtractBatch mode=extract）：
    /// 弹出 ExtractSettingsWindow 让用户选择目标路径 / 冲突策略 / 过滤条件，
    /// 确认后批处理解压到所选目录，取消则直接退出。
    /// </summary>
    private static async Task RunExtractDialogCliAsync(
        List<string> paths,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (existing.Count == 0)
            {
                AppShutdown.Request(desktop.Shutdown);
                return;
            }

            // 立即显示设置窗口，消除弹窗前条目列表读取的空白期：
            // 窗口秒现后由窗口自身在 Loaded 逐包后台校验（ListEntriesAsync 只读头部索引），
            // 行内徽标实时回写 ✓/🔒/⚠️；预览树跟随选中行，损坏/需密码包显示对应占位。
            //
            // CLI 宿主差异：无属主窗口（Owner = null，冲突弹窗用 presenter 的活动进度窗）、
            // 进度走 CLI presenter、退出策略为 None —— ShutdownAfterRun 会在会话 finally 里先
            // 退出，NotStarted 原因来不及展示；进程退出一律由本方法在结局后显式请求。
            var session = new ExtractSession(postActions: new CliExtractPostActions());
            var context = new ExtractRunContext(
                Owner: null,
                Presenter: new Services.CliProgressPresenter(),
                ExitPolicy: ExitPolicy.None,
                DisplayPaths: existing);
            var dialog = new ExtractSettingsWindow(session, context, existing);

            // 命令行历史行为：逐包先撞密码库（桌面窗口只复用本会话已缓存的密码）。
            // 取密策略随载荷冻结进 ExtractSettings.PasswordPolicy，由执行器消费。
            dialog.SetPasswordPolicy(ExtractPasswordPolicy.FallbackToPasswordLibrary);

            Services.NativeSplash.Hook(dialog); // startup-native-splash：终端窗口出现即关 splash

            // CLI 模式没有主窗口，无法用 ShowDialog(owner)，改用非模态 Show + 窗口的完成信号等待结局。
            // 同时必须把 ShutdownMode 改为 OnExplicitShutdown：默认 OnLastWindowClose 会在弹窗
            // （唯一窗口）关闭瞬间同步触发应用退出，导致确认后的解压 continuation 来不及执行。
            // 退出时机改由我们显式控制：取消 → 立即退出；确认 → 会话语义执行完毕后退出。
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = dialog;
            dialog.Show();

            var confirmation = await dialog.Completion;
            if (confirmation is not Confirmed<ExtractSettings> confirmed)
            {
                // 取消 / 点 X：静默退出，不弹任何提示。
                AppShutdown.Request(desktop.Shutdown);
                return;
            }

            var outcome = await dialog.RunConfirmedAsync(confirmed.Value);
            if (outcome.IsNotStarted)
            {
                // 未开始没有进度窗承载原因：CLI 至少写到 stderr，不许静默消失。
                Console.Error.WriteLine(outcome.Reason);
            }

            AppShutdown.Request(desktop.Shutdown);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Extract dialog failed: {ex.Message}");
            AppShutdown.Request(desktop.Shutdown);
        }
    }

    /// <summary>
    /// CLI 宿主的「解压后动作」实现：命令行没有可回用的主窗口，
    /// 因此不提供「解压后打开目录」（也不显示该开关），只保留删包能力。
    /// </summary>
    private sealed class CliExtractPostActions : IExtractPostActions
    {
        /// <summary>CLI 不提供打开目录的能力位（开关不显示，完成侧动作跳过）。</summary>
        public bool SupportsOpenFolder => false;

        /// <summary>删包走统一入口（含回收站 / 永久删除的策略）。</summary>
        public void DeleteArchive(string archivePath, bool enabled)
            => App.TryDeleteArchiveAfterExtract(archivePath, enabled);

        /// <summary>CLI 无打开目录动作，空实现。</summary>
        public Task OpenFolderAsync(string destinationPath, string archivePath, string? password)
            => Task.CompletedTask;
    }

    /// <summary>
    /// 在进度窗口中执行 CLI 解压（对齐 WPF HandleExtractBatchCore）。
    /// 显示批处理文件列表（单文件也显示）、逐项状态、可暂停/取消；
    /// 成功时自动关闭（2.5s，尊重 📌 KeepOpenOnComplete），失败时等待用户手动关闭。
    /// 权限不足时关闭窗口后走提权流程。
    /// </summary>
    private static async Task<bool> RunCliExtractWithProgressAsync(
        string archivePath,
        string targetDir,
        string? password,
        IArchiveEngine engine,
        string[] originalArgs,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        // 显式管理退出：进度窗口是 CLI 模式下唯一窗口，默认 OnLastWindowClose 会在用户
        // 点击 X 时立即退出进程，后台解压被强杀中断（对齐 WPF 的 OnExplicitShutdown 用法）
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var progressWindow = new ProgressWindow(LocalizationManager.T("Progress_Title_Extract"));
        Services.NativeSplash.Hook(progressWindow); // startup-native-splash：进度窗即终端窗口
        progressWindow.InitCancellation();
        progressWindow.Show();
        desktop.MainWindow = progressWindow;

        // spec §11.2: CLI 解压 → 只显示「删包」开关（打开目录的 CLI 接线属挂起计划 cli-extract-open-folder.md，D18）
        progressWindow.SetExtractPostActions(openFolder: false, deleteArchive: true, openFolderSeed: null);

        // 始终显示文件列表（单文件也显示）：批处理列表项 = 压缩包路径
        progressWindow.InitBatchMode(new[] { archivePath });
        progressWindow.SetCurrentBatchItem(0);

        // 路径 A（决策 D1）单文件变体：命令行已给定密码 → 直接点亮行 0 的 🔑●●●● 徽标
        if (!string.IsNullOrEmpty(password))
        {
            var (rule, desc) = LookupPasswordRuleAndDescription(archivePath, password);
            progressWindow.SetBatchPasswordState(0, BatchPasswordState.Matched, password, rule, desc);
        }

        var ct = progressWindow.CancellationToken;
        var doneEvent = new ManualResetEventSlim(false);
        Exception? captureException = null;
        bool cancelled = false;
        bool elevationRequested = false;

        _ = Task.Run(async () =>
        {
            try
            {
                var rawProgress = progressWindow.CreatePauseAwareProgress(
                    ProgressWindow.CreateBackgroundProgress(progressWindow));

                // 冲突策略来自 AppSettings.FileConflictAction（默认 ask）；
                // Ask 弹 ConflictDialog（owner=进度窗口，与 --extract 弹窗批处理同逻辑）
                var settings = AppSettings.Load();
                var options = SelectedItemsExtractService.CreateExtractOptions(
                    settings.FileConflictAction,
                    info => ExtractFlow.ShowConflictDialogAsync(progressWindow, info));

                // 列表模式播种 + 并行度元数据：CLI 单文件叶子同样直连引擎、绕过 ExtractFlow.ExtractAsync，
                // 故在此补齐两处接线（与 RunCliDirectExtractBatchAsync 多文件叶子同源）。
                // InitBatchMode/SetCurrentBatchItem 已清空条目行并把并行度归零，此处只需重新播种/置位。
                ExtractFlow.TrySeedEntryItemsInBackground(
                    progressWindow, archivePath, password, filteredKeys: null, ct);
                progressWindow.SetParallelDegree(
                    ExtractFlow.ResolveDisplayParallelDegree(archivePath));

                // 叶子 5/5：单文件 CLI 解压入口（--extract-here / --extract-to-name / --extract 等）
                ExtractResult? extractResult = null;
                var (singleOutcome, _) = await PasswordRetryLoop.RunAsync(
                    archivePath, password, engine,
                    owner: progressWindow,
                    setStatus: msg => progressWindow.SetStatus(msg),
                    attempt: async (pwd, token) =>
                        extractResult = await engine.ExtractAsync(
                            archivePath, targetDir, pwd, rawProgress, token, options),
                    ct: ct);
                if (singleOutcome == PasswordRetryOutcome.Cancelled)
                    throw new PasswordRetryCancelledException(archivePath);
                // 损坏/无效：弹窗循环已判定继续重弹无意义 → 抛错走既有失败收尾，
                // 禁止落成功路径（否则会记录路径历史且 TryDeleteArchiveAfterExtract 会误删损坏源包）
                if (singleOutcome == PasswordRetryOutcome.CorruptedOrInvalid)
                    throw new InvalidDataException(LocalizationManager.T("Status_ArchiveCorrupted"));

                progressWindow.FinalizeBatch();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (extractResult is { HasFailures: true })
                    {
                        // 部分条目失败（如权限不足）：显示错误汇总（对齐 WPF ExtractResult.HasFailures 路径）
                        progressWindow.SetErrorSummary(
                            LocalizationManager.T("Progress_WriteFailed_Fmt", extractResult.FailedEntries, targetDir));
                    }
                    progressWindow.SetComplete(LocalizationManager.T("Cli_StatusDone"));
                });
                Console.WriteLine($"Extracted: {archivePath} -> {targetDir}");
                // 成功后把目标目录写入路径历史（--extract-here / --extract-to-name / --extract-smart 单文件直解）
                PathHistoryManager.Record(targetDir);
                // D19: 删包时序移至窗口关闭之后（倒计时内用户可切换 🗑 开关）
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (PasswordRetryCancelledException)
            {
                // 用户在密码弹窗点取消：行 0 标 ✗「已取消 - 需要密码」，走既有失败收尾（不吞、不重抛）
                captureException = new Exception(LocalizationManager.T("Status_PasswordCancelled"));
                await Dispatcher.UIThread.InvokeAsync(() =>
                    progressWindow.UpdateBatchItemStatus(0, BatchItemStatus.Failed,
                        LocalizationManager.T("Status_PasswordCancelled")));
            }
            catch (UnauthorizedAccessException)
            {
                // 目录创建级权限不足：关闭窗口后由外层走提权流程
                elevationRequested = true;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    progressWindow.Close();
                    desktop.MainWindow = null;
                });
            }
            catch (Exception ex)
            {
                captureException = ex;
                Console.Error.WriteLine($"Extraction failed: {ex.Message}");
            }
            finally
            {
                doneEvent.Set();
            }
        });

        await Task.Run(() => doneEvent.Wait());
        progressWindow.FinalizeBatch();

        if (elevationRequested)
        {
            return await HandleElevationAsync(new[] { targetDir }, originalArgs, desktop);
        }

        if (cancelled)
        {
            await Dispatcher.UIThread.InvokeAsync(() => progressWindow.Close());
            return false;
        }

        if (captureException != null)
        {
            // 失败：标记批次项为 Failed（FinalizeBatch 已把 InProgress 置为 Completed，这里覆盖为失败）
            progressWindow.UpdateBatchItemStatus(0, BatchItemStatus.Failed, captureException.Message);
            // 显示错误汇总并等待用户手动关闭窗口
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var (wholeErrorText, wholeErrorDetail) = WholeArchiveErrorLocalizer.ToUserText(captureException!);
                progressWindow.SetErrorSummary(wholeErrorText, wholeErrorDetail);
                progressWindow.CompleteWithErrors();
            });
            await progressWindow.AutoCloseOrWaitAsync(() => progressWindow.Close());
            return false;
        }

        // 成功：自动关闭（延迟来自设置 AutoCloseDelaySeconds）或等待用户手动关闭；关闭后按开关最终值删包（D19）
        await progressWindow.AutoCloseOrWaitAsync(() => progressWindow.Close());
        TryDeleteArchiveAfterExtract(archivePath, progressWindow.DeleteArchiveAfterExtractValue);
        return false;
    }

    private static string GetArchiveBaseName(string archivePath)
    {
        var name = Path.GetFileNameWithoutExtension(archivePath);
        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(archivePath));
        return name;
    }

    /// <summary>
    /// CLI 直接解压批处理（--extract-here / --extract-to-name / --extract-smart 多文件）：
    /// ShellExt 多选压缩包时一次传入全部路径，此前只取第一个解压，其余被忽略。
    /// 本方法用一个进度窗口逐文件解压，每文件按 mode 独立计算目标目录
    /// （here → 压缩包所在目录；toname → 命名子目录；smart → 结构分析）。
    /// 冲突策略来自 AppSettings.FileConflictAction（Ask 弹 ConflictDialog，owner=进度窗口）。
    /// </summary>
    private static async Task RunCliDirectExtractBatchAsync(
        List<string> archivePaths,
        string mode,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var progressWindow = new ProgressWindow(LocalizationManager.T("Progress_Title_Extract"));
        Services.NativeSplash.Hook(progressWindow); // startup-native-splash：进度窗即终端窗口
        progressWindow.InitCancellation();
        progressWindow.Show();
        desktop.MainWindow = progressWindow;
        progressWindow.InitBatchMode(archivePaths);

        // spec §11.2: CLI 解压 → 只显示「删包」开关（打开目录的 CLI 接线属挂起计划，D18）
        progressWindow.SetExtractPostActions(openFolder: false, deleteArchive: true, openFolderSeed: null);

        var ct = progressWindow.CancellationToken;
        var doneEvent = new ManualResetEventSlim(false);
        Exception? captureException = null;
        bool cancelled = false;
        var succeededArchives = new List<string>();   // D19: 关窗后按开关最终值统一删包

        var settings = AppSettings.Load();
        var conflictOptions = SelectedItemsExtractService.CreateExtractOptions(
            settings.FileConflictAction,
            info => ExtractFlow.ShowConflictDialogAsync(progressWindow, info));

        _ = Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < archivePaths.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var archivePath = archivePaths[i];
                    await Dispatcher.UIThread.InvokeAsync(() => progressWindow.SetCurrentBatchItem(i));

                    try
                    {
                        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
                        if (engine == null)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                                progressWindow.UpdateBatchItemStatus(i, BatchItemStatus.Failed,
                                    LocalizationManager.T("Error_UnsupportedArchiveFormat")));
                            continue;
                        }

                        // 每文件独立计算目标目录（与单文件 CLI 流程/WPF 语义一致）
                        string targetDir = mode switch
                        {
                            "toname" => Path.Combine(
                                Path.GetDirectoryName(archivePath) ?? ".",
                                GetArchiveBaseName(archivePath)),
                            "smart" => await ResolveSmartDestCliAsync(archivePath, engine),
                            _ => Path.GetDirectoryName(archivePath) ?? "."
                        };
                        if (mode == "toname")
                            Directory.CreateDirectory(targetDir);

                        var password = ResolveCliPassword(archivePath, engine);

                        // 路径 B（决策 D1）：解析前先亮 🔄 匹配中（密码库逐条尝试可能耗时）；
                        // 解析完成后再落终态——命中 → 🔑●●●●，无密码包 → 熄灭
                        progressWindow.SetBatchPasswordState(i, BatchPasswordState.Matching, null, null, null);
                        if (password != null)
                        {
                            var (pRule, pDesc) = LookupPasswordRuleAndDescription(archivePath, password);
                            progressWindow.SetBatchPasswordState(i, BatchPasswordState.Matched,
                                password, pRule, pDesc);
                        }
                        else
                        {
                            progressWindow.SetBatchPasswordState(i, BatchPasswordState.None, null, null, null);
                        }

                        // 列表模式播种 + 并行度元数据（本方法直连引擎、绕过 ExtractFlow，故在此补齐两处接线，
                        // 否则 ZIP 的列表模式无条目行且「并行」统计卡恒不出现）。
                        // ClearEntries 先清上一包残留行——InitBatchMode 每批只清一次，多包批处理必须逐包清理；
                        // 并行度逐包重算——非 zip/7z 返回 1 → HasParallelDegree=false 自动隐藏并行卡（Rule 6）。
                        // 播种为 fire-and-forget，且必须在解压启动前发起，否则首批条目行会缺失。
                        progressWindow.ClearEntries();
                        ExtractFlow.TrySeedEntryItemsInBackground(
                            progressWindow, archivePath, password, filteredKeys: null, ct);
                        progressWindow.SetParallelDegree(
                            ExtractFlow.ResolveDisplayParallelDegree(archivePath));

                        var progress = progressWindow.CreatePauseAwareProgress(
                            ProgressWindow.CreateBackgroundProgress(progressWindow));

                        // 叶子 4/5：直连引擎的批处理入口 → 密码弹窗兜底
                        var (extractOutcome, _) = await PasswordRetryLoop.RunAsync(
                            archivePath, password, engine,
                            owner: progressWindow,
                            setStatus: msg => progressWindow.SetStatus(msg),
                            attempt: async (pwd, token) =>
                                await engine.ExtractAsync(archivePath, targetDir, pwd, progress, token, conflictOptions),
                            ct: ct);
                        if (extractOutcome == PasswordRetryOutcome.Cancelled)
                            throw new PasswordRetryCancelledException(archivePath);
                        // 损坏/无效：弹窗循环已判定继续重弹无意义 → 抛错走既有失败收尾，
                        // 禁止落成功路径（否则会记录路径历史且 TryDeleteArchiveAfterExtract 会误删损坏源包）
                        if (extractOutcome == PasswordRetryOutcome.CorruptedOrInvalid)
                            throw new InvalidDataException(LocalizationManager.T("Status_ArchiveCorrupted"));

                        // 成功后把目标目录写入路径历史（多文件直解批处理逐项记录，重复路径由去重置顶）
                        PathHistoryManager.Record(targetDir);

                        // D19: 删包延后到窗口关闭后，此处仅登记成功项
                        succeededArchives.Add(archivePath);

                        await Dispatcher.UIThread.InvokeAsync(() =>
                            progressWindow.UpdateBatchItemStatus(i, BatchItemStatus.Completed));
                    }
                    catch (PasswordRetryCancelledException)
                    {
                        // 密码弹窗取消：该行标 ✗「已取消 - 需要密码」，批处理继续下一个包（决策 D7）
                        await Dispatcher.UIThread.InvokeAsync(() =>
                            progressWindow.UpdateBatchItemStatus(i, BatchItemStatus.Failed,
                                LocalizationManager.T("Status_PasswordCancelled")));
                        continue;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                            progressWindow.UpdateBatchItemStatus(i, BatchItemStatus.Failed, ex.Message));
                    }
                }

                progressWindow.FinalizeBatch();
                await Dispatcher.UIThread.InvokeAsync(() =>
                    progressWindow.SetComplete(LocalizationManager.T("Cli_StatusDone")));
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                captureException = ex;
                Console.Error.WriteLine($"Extraction failed: {ex.Message}");
            }
            finally
            {
                doneEvent.Set();
            }
        });

        await Task.Run(() => doneEvent.Wait());

        if (cancelled)
        {
            await Dispatcher.UIThread.InvokeAsync(() => progressWindow.Close());
            AppShutdown.Request(desktop.Shutdown);
            return;
        }

        if (captureException != null)
        {
            // 失败：标记首项为 Failed 并显示错误汇总（与 ExtractSession 的失败收口一致）
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                progressWindow.UpdateBatchItemStatus(0, BatchItemStatus.Failed, captureException!.Message);
                var (wholeErrorText, wholeErrorDetail) = WholeArchiveErrorLocalizer.ToUserText(captureException!);
                progressWindow.SetErrorSummary(wholeErrorText, wholeErrorDetail);
                progressWindow.CompleteWithErrors();
            });
            await progressWindow.AutoCloseOrWaitAsync(() => progressWindow.Close());
            AppShutdown.Request(desktop.Shutdown);
            return;
        }

        // 成功：自动关闭（延迟来自设置 AutoCloseDelaySeconds）或等待用户手动关闭；关闭时按开关最终值删包再退出（D19）
        await progressWindow.AutoCloseOrWaitAsync(() =>
        {
            foreach (var p in succeededArchives)
                TryDeleteArchiveAfterExtract(p, progressWindow.DeleteArchiveAfterExtractValue);
            AppShutdown.Request(desktop.Shutdown);
        });
    }

}
