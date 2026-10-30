using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// <see cref="IExtractSession"/> 的默认实现：把「用户确认过的不可变输入」跑成一次解压，
/// 并且只用 <see cref="ExtractOutcome"/> 回报结局。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么存在：</b>解压执行段原本散在 <c>MainWindowViewModel.Extract.cs</c>（单包 + 多包 +
/// 选中条目三条分支）与 <c>App.CliExtract.cs</c>（批处理 + 密码库匹配 + 行状态徽标）里，两条链
/// 几乎逐行相同却各写一遍：解析条目范围、取密码、开进度窗、跑引擎、写历史、收尾删包/打开目录。
/// 本类把这段吸收成唯一实现，也是「唯一落地者」法则的落点 —— 密码库匹配、历史记录、冲突决策、
/// 进度呈现都只能在这里发生，入口再也不能各写一份。
/// </para>
/// <para>
/// <b>刻意不做的事：</b>
/// </para>
/// <list type="bullet">
/// <item><description>不回读任何 ViewModel：输入全部来自 <see cref="ExtractSettings"/>，宿主差异来自 <see cref="ExtractRunContext"/>。</description></item>
/// <item><description>不改路径契约：路径归属仍只由 <c>ExtractPathResolver</c>（经 <c>ExtractFlow</c> / <c>SelectedItemsExtractService</c>）决定。</description></item>
/// <item><description>不重复弹窗：异常文案由 presenter 写进进度窗口后原样抛回，这里只把它转成 <see cref="ExtractOutcome.Failed"/>。</description></item>
/// <item><description>不碰窗口生命周期：进度窗口的创建、取消、自动关闭都由 <see cref="IProgressPresenter"/> 负责；这里只在 <c>ConfigureWindow</c> 钩子里贴一次性配置。</description></item>
/// </list>
/// <para>
/// <b>关于构造函数里的委托端口：</b>引擎解析、密码服务、冲突弹窗、完成动作与设置加载都以委托/端口
/// 注入，默认实现直接指向现有静态服务。理由与 <c>CompressSession</c> 一致：让本类在没有真实引擎、
/// 没有数据库、没有 Shell 的测试里也能验证最容易被写错的「结局映射」（取消 → Cancelled、
/// 异常 → Failed、前置不成立 → NotStarted），否则入口矩阵测试只能靠跑真解压。
/// </para>
/// </remarks>
public sealed class ExtractSession : IExtractSession
{
    /// <summary>压缩包路径 → 引擎（默认 <see cref="ArchiveEngineFactory.GetEngineByExtension"/>）。</summary>
    private readonly Func<string, IArchiveEngine?> _getEngine;

    /// <summary>密码服务工厂（默认每次新建 <see cref="PasswordService"/>，与现有调用点一致）。</summary>
    private readonly Func<PasswordService> _passwordServiceFactory;

    /// <summary>冲突弹窗端口（默认 <see cref="ExtractFlow.ShowConflictDialogAsync"/>）。</summary>
    private readonly Func<Window, FileConflictInfo, Task<(FileConflictAction Action, bool ApplyToAll)>> _showConflictDialog;

    /// <summary>完成后动作端口（默认桌面宿主的 <see cref="DefaultExtractPostActions"/>）。</summary>
    private readonly IExtractPostActions _postActions;

    /// <summary>应用设置加载端口（只读：保留完整路径、并行解压默认值）。</summary>
    private readonly Func<AppSettings?> _loadSettings;

    /// <summary>
    /// 创建会话。
    /// </summary>
    /// <param name="getEngine">引擎解析端口；为 <see langword="null"/> 时走 <see cref="ArchiveEngineFactory"/>。</param>
    /// <param name="passwordServiceFactory">密码服务端口；为 <see langword="null"/> 时每次新建 <see cref="PasswordService"/>。</param>
    /// <param name="showConflictDialog">冲突弹窗端口；为 <see langword="null"/> 时走 <see cref="ExtractFlow.ShowConflictDialogAsync"/>。</param>
    /// <param name="postActions">完成后动作端口；为 <see langword="null"/> 时用桌面默认实现。</param>
    /// <param name="loadSettings">应用设置端口；为 <see langword="null"/> 时读真实设置。</param>
    public ExtractSession(
        Func<string, IArchiveEngine?>? getEngine = null,
        Func<PasswordService>? passwordServiceFactory = null,
        Func<Window, FileConflictInfo, Task<(FileConflictAction Action, bool ApplyToAll)>>? showConflictDialog = null,
        IExtractPostActions? postActions = null,
        Func<AppSettings?>? loadSettings = null)
    {
        _getEngine = getEngine ?? ArchiveEngineFactory.GetEngineByExtension;
        _passwordServiceFactory = passwordServiceFactory ?? (() => new PasswordService());
        _showConflictDialog = showConflictDialog
            ?? ((owner, info) => ExtractFlow.ShowConflictDialogAsync(owner, info));
        _postActions = postActions ?? new DefaultExtractPostActions();
        _loadSettings = loadSettings ?? AppSettings.Load;
    }

    /// <inheritdoc />
    public async Task<ExtractOutcome> RunAsync(
        ExtractSettings settings,
        ExtractRunContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            // ① 调用方进入前已取消：算取消，不进进度窗口、不弹提示。
            if (cancellationToken.IsCancellationRequested)
                return ExtractOutcome.Cancelled;

            // ② 前置条件（NotStarted）。对应原实现的「dest 为空直接 Shutdown」「引擎为 null 抛
            //    NotSupportedException」两类判定，只是把结论显式化，不再静默。
            if (settings.ArchivePaths.Count == 0)
                return ExtractOutcome.NotStarted(LocalizationManager.T("Error_Extract_NoEntries"));

            var firstArchive = settings.ArchivePaths[0];
            if (!File.Exists(firstArchive))
                return ExtractOutcome.NotStarted(LocalizationManager.T("Error_Extract_ArchiveNotFound", firstArchive));

            if (string.IsNullOrWhiteSpace(settings.DestinationPath))
                return ExtractOutcome.NotStarted(LocalizationManager.T("Error_Extract_DestinationInvalid"));

            if (!TryEnsureDestination(settings.DestinationPath, out var destinationFailure))
                return ExtractOutcome.NotStarted(destinationFailure);

            // 批处理只跑仍然存在的包（原 CLI 逐项判存在性；这里先裁一遍，避免整批因一个包消失而失败）。
            var archivePaths = settings.ArchivePaths.Where(File.Exists).ToList();
            if (archivePaths.Count == 0)
                return ExtractOutcome.NotStarted(LocalizationManager.T("Error_Extract_ArchiveNotFound", firstArchive));

            // ③ 条目范围（对应原 VM 的 FilteredEntryKeys 与 SelectedEntries 两条分支）。
            //    注意：FilteredEntryKeys 非 null 但为空 = 有意零匹配，绝不回退整包（「预览 = 实际」边界）。
            List<string>? filteredKeys = settings.FilteredEntryKeys is { } keys ? new List<string>(keys) : null;
            var selectedEntries = settings.SelectedEntries;
            var useSelectedEntries = selectedEntries is { Count: > 0 };

            // ④ 密码：首包一次解析（原 ExtractArchive 取会话缓存、CLI 逐包先打密码库）。
            var passwordService = _passwordServiceFactory();
            var firstPassword = ResolvePassword(settings, archivePaths[0], settings.PasswordPolicy, passwordService);

            // ⑤ 并行度快照：对话框给了就用对话框的（本次生效、不回写），否则 ExtractFlow 读全局设置。
            var parallelOverride = settings.ParallelOverride;

            // ⑥ 运行期要回读的窗口开关最终值（原实现从 LastProgressWindow 回读；这里在同一作用域内捕获）。
            bool? finalOpenFolder = null;
            bool? finalDeleteArchive = null;
            var succeededArchives = new List<string>(archivePaths.Count);

            var displayPaths = context.DisplayPaths is { Count: > 0 }
                ? context.DisplayPaths
                : archivePaths.Count > 1 ? archivePaths : null;

            bool completed;
            try
            {
                completed = await context.Presenter.RunAsync(new ProgressRunRequest
                {
                    Title = LocalizationManager.T("Progress_Title_Extract"),
                    DisplayPaths = displayPaths,
                    ConfigureWindow = window =>
                    {
                        // 解压后置开关：能力位 + 初值（必须在 Show 前，窗口据此决定两个复选框是否可见）。
                        window.SetExtractPostActions(
                            openFolder: _postActions.SupportsOpenFolder,
                            deleteArchive: true,
                            openFolderSeed: settings.OpenFolderAfterExtract,
                            deleteArchiveSeed: settings.DeleteArchiveAfterExtract);
                    },
                    Operation = async (progress, runToken) =>
                    {
                        // ⑦ 进度窗口的取消令牌与调用方令牌合并：任一触发都停止解压。
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(runToken, cancellationToken);
                        var ct = linked.Token;

                        Func<FileConflictInfo, Task<(FileConflictAction Action, bool ApplyToAll)>> conflictDialog =
                            info =>
                            {
                                // 冲突弹窗必须有宿主窗（原实现隐式依赖 ProgressWindow.CurrentVisible / 主窗口）。
                                // 两者都缺说明宿主没按契约给窗口：炸在产出点，而不是在弹窗里空引用。
                                var owner = context.Presenter.ActiveWindow ?? context.Owner
                                    ?? throw new InvalidOperationException(
                                        "ExtractSession: 冲突弹窗需要宿主窗口，但 context 里既无 Owner 也无活动进度窗。");
                                return _showConflictDialog(owner, info);
                            };

                        if (useSelectedEntries)
                        {
                            // ⑧-a 选中条目：走 SelectedItemsExtractService（与预览树同一路径契约），
                            //      窗口由 presenter 提供，故不调用 ExtractFlow 里那个自建窗口的封装。
                            var appSettings = _loadSettings();
                            await new SelectedItemsExtractService().ExtractEntriesAsync(
                                firstArchive,
                                firstPassword,
                                selectedEntries!,
                                settings.DestinationPath,
                                settings.ConflictAction,
                                settings.CurrentFolder ?? string.Empty,
                                settings.PreserveFullPath ?? appSettings?.ExtractPreserveFullPath ?? false,
                                conflictDialog,
                                progress,
                                ct);

                            // 选中条目路径的历史记录在服务内不发生（原实现由 ExtractFlow 记），这里补上。
                            PathHistoryManager.Record(settings.DestinationPath);
                            succeededArchives.Add(firstArchive);
                        }
                        else if (archivePaths.Count > 1)
                        {
                            // ⑧-b 多包批处理：一包一行，逐行状态与密码徽标写入 presenter 暴露的窗口；
                            //      过滤集合只作用于首包（与 WPF「过滤仅对 i==0 生效」语义对齐）。
                            var window = context.Presenter.ActiveWindow;
                            for (var i = 0; i < archivePaths.Count; i++)
                            {
                                ct.ThrowIfCancellationRequested();
                                var archivePath = archivePaths[i];
                                window?.SetCurrentBatchItem(i);

                                if (_getEngine(archivePath) is null)
                                {
                                    window?.UpdateBatchItemStatus(i, BatchItemStatus.Failed, LocalizationManager.T("Error_UnsupportedArchiveFormat"));
                                    continue;
                                }

                                var password = ResolvePassword(settings, archivePath, settings.PasswordPolicy, passwordService);
                                ApplyPasswordBadge(window, i, archivePath, password);

                                try
                                {
                                    await ExtractFlow.ExtractAsync(
                                        archivePath,
                                        settings.DestinationPath,
                                        settings.ConflictAction,
                                        i == 0 ? filteredKeys : null,
                                        password,
                                        conflictDialog,
                                        progress,
                                        ct,
                                        parallelOverride);

                                    window?.UpdateBatchItemStatus(i, BatchItemStatus.Completed);
                                    succeededArchives.Add(archivePath);
                                }
                                catch (PasswordRetryCancelledException)
                                {
                                    // 与 CLI 现状一致：密码重试被用户取消 → 该行标记「已取消 - 需要密码」，整批继续。
                                    window?.UpdateBatchItemStatus(i, BatchItemStatus.Failed, LocalizationManager.T("Status_PasswordCancelled"));
                                }
                                catch (OperationCanceledException)
                                {
                                    throw;
                                }
                                catch (Exception ex)
                                {
                                    // 多包批处理：单包失败只标记该行并继续（与 CLI 现状一致，不让一个坏包拖死整批）。
                                    // 旧写法是 `catch (Exception ex) when (archivePaths.Count > 1)` —— 本分支恒为多包，
                                    // 该 filter 恒真，导致紧随其后的 rethrow 分支永远不可达（既有缺陷）。
                                    // 现在改成无条件 catch（= 该处本意），语义显式化；对外行为不变。
                                    window?.UpdateBatchItemStatus(i, BatchItemStatus.Failed, ex.Message);
                                    App.DebugLog($"[ExtractSession] batch item failed: {archivePath}: {ex}");
                                }
                            }

                            window?.FinalizeBatch();
                        }
                        else
                        {
                            // ⑧-c 单包：与主窗口现状逐字段等价（目标目录、冲突策略、过滤集合、密码、并行快照）。
                            await ExtractFlow.ExtractAsync(
                                firstArchive,
                                settings.DestinationPath,
                                settings.ConflictAction,
                                filteredKeys,
                                firstPassword,
                                conflictDialog,
                                progress,
                                ct,
                                parallelOverride);

                            succeededArchives.Add(firstArchive);
                        }

                        // ⑨ 捕获窗口开关的最终值（用户在运行期间改过复选框时以窗口为准，与现状回读一致）。
                        if (context.Presenter.ActiveWindow is { } active)
                        {
                            finalOpenFolder = active.OpenFolderAfterExtractValue;
                            finalDeleteArchive = active.DeleteArchiveAfterExtractValue;
                        }
                    },
                });
            }
            catch (OperationCanceledException)
            {
                // presenter 正常会吞掉取消并返回 false，这里只是兜底。
                completed = false;
            }
            catch (Exception ex)
            {
                // ⑩ 执行异常：错误摘要已由 presenter 呈现，这里只记录结论，绝不重复弹窗。
                var (errorText, errorDetail) = WholeArchiveErrorLocalizer.ToUserText(ex);
                App.DebugLog($"[ExtractSession] extraction failed: {errorText} {errorDetail}");
                return ExtractOutcome.Failed(errorText);
            }

            // ⑪ presenter 返回 false = 用户取消 / 窗口被关：静默收场，不删包、不开目录、不弹提示。
            if (!completed)
                return ExtractOutcome.Cancelled;

            // ⑫ 完成侧动作（顺序与现状一致：先删包，再打开目录）。
            await RunCompletionActionsAsync(
                settings,
                succeededArchives,
                finalDeleteArchive ?? settings.DeleteArchiveAfterExtract,
                finalOpenFolder ?? settings.OpenFolderAfterExtract,
                firstArchive,
                firstPassword);

            return ExtractOutcome.Completed;
        }
        finally
        {
            // ⑬ 按宿主退出策略收尾：完成、取消、失败、未开始四条路径都要走，
            //    否则 CLI 隐藏主窗口后会挂在后台不退。
            ApplyExitPolicy(context);
        }
    }

    /// <summary>
    /// 确保目标目录存在且可写；失败时给出已本地化的原因（对应原实现「dest 不可用」的静默 Shutdown）。
    /// </summary>
    private static bool TryEnsureDestination(string destinationPath, out string failureReason)
    {
        try
        {
            Directory.CreateDirectory(destinationPath);
            failureReason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            App.DebugLog($"[ExtractSession] destination unavailable: {destinationPath}: {ex.Message}");
            failureReason = LocalizationManager.T("Error_Extract_DestinationNotWritable", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 解析某个压缩包的密码：载荷里已有的（对话框内校验 / CLI 预点亮）优先，
    /// 其次才是「仅首包生效」的调用方预置密码，最后按策略决定是否打密码库。
    /// </summary>
    private string? ResolvePassword(
        ExtractSettings settings,
        string archivePath,
        ExtractPasswordPolicy policy,
        PasswordService passwordService)
    {
        if (settings.MatchedPasswords is { } matched
            && matched.TryGetValue(archivePath, out var known)
            && !string.IsNullOrEmpty(known))
        {
            return known;
        }

        if (settings.Password is { Length: > 0 } provided
            && string.Equals(archivePath, settings.ArchivePaths[0], StringComparison.OrdinalIgnoreCase))
        {
            return provided;
        }

        if (policy != ExtractPasswordPolicy.FallbackToPasswordLibrary)
            return null;

        var engine = _getEngine(archivePath);
        if (engine is null)
            return null;

        try
        {
            if (!passwordService.HasEncryptedEntries(archivePath, engine))
                return null;

            foreach (var entry in PasswordManager.Instance.FindMatchingPasswords(archivePath))
            {
                if (string.IsNullOrEmpty(entry.Password))
                    continue;

                var verify = passwordService.QuickVerifyPasswordEx(archivePath, entry.Password, engine);
                if (verify.Result == PasswordVerificationResult.Success)
                    return entry.Password;
                if (verify.Result == PasswordVerificationResult.CorruptedOrInvalid)
                    break;
            }
        }
        catch (Exception ex)
        {
            App.DebugLog($"[ExtractSession] password library lookup failed for '{archivePath}': {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 把「这一行用了哪个密码、命中哪条规则」写进批处理行徽标（与 CLI 现状的密码徽标一致）。
    /// </summary>
    private void ApplyPasswordBadge(ProgressWindow? window, int index, string archivePath, string? password)
    {
        if (window is null)
            return;

        if (string.IsNullOrEmpty(password))
        {
            window.SetBatchPasswordState(index, BatchPasswordState.None, null, null, null);
            return;
        }

        var match = PasswordManager.Instance.FindMatchingPasswords(archivePath)
            .FirstOrDefault(entry => string.Equals(entry.Password, password, StringComparison.Ordinal));
        var rule = match?.Patterns is { Count: > 0 } patterns ? string.Join("; ", patterns) : null;
        var description = string.IsNullOrEmpty(match?.Description) ? null : match!.Description;
        window.SetBatchPasswordState(index, BatchPasswordState.Matched, password, rule, description);
    }

    /// <summary>
    /// 完成侧动作：删包 / 打开目录。顺序与原实现一致（CLI 先逐个删成功包再退出；
    /// 主窗口先删包再打开目录）。
    /// </summary>
    private async Task RunCompletionActionsAsync(
        ExtractSettings settings,
        IReadOnlyList<string> succeededArchives,
        bool deleteArchive,
        bool openFolder,
        string firstArchive,
        string? firstPassword)
    {
        // 打开目录的密码：首包载荷密码优先，其次匹配表（原实现直接用会话密码）。
        var openPassword = firstPassword
            ?? (settings.MatchedPasswords is { } m && m.TryGetValue(firstArchive, out var p) ? p : null);

        if (deleteArchive)
        {
            foreach (var archivePath in succeededArchives)
            {
                try
                {
                    _postActions.DeleteArchive(archivePath, enabled: true);
                }
                catch (Exception ex)
                {
                    App.DebugLog($"[ExtractSession] delete archive failed: {archivePath}: {ex.Message}");
                }
            }
        }

        if (openFolder && _postActions.SupportsOpenFolder)
        {
            await _postActions.OpenFolderAsync(settings.DestinationPath, firstArchive, openPassword);
        }
    }

    /// <summary>
    /// 按 <see cref="ExitPolicy"/> 请求进程退出；桌面流程为 <see cref="ExitPolicy.None"/>，此处是空操作。
    /// </summary>
    private static void ApplyExitPolicy(ExtractRunContext context)
    {
        if (context.ExitPolicy != ExitPolicy.ShutdownAfterRun)
            return;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            AppShutdown.Request(desktop.Shutdown);
        else
            AppShutdown.Request(_ => { });
    }
}
