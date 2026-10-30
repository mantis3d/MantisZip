using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;

// CompressConflictAction 在 Core.Abstractions（引擎侧，压缩真正用的那份）与 UI.Dialogs（冲突弹窗自己的枚举）
// 里各有一份，本文件同时引用两个命名空间，必须消歧义：会话交给引擎的始终是 Core 的枚举。
using CompressConflictAction = MantisZip.Core.Abstractions.CompressConflictAction;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// <see cref="ICompressSession"/> 的默认实现：把「用户确认过的不可变输入」跑成一次压缩，
/// 并只用 <see cref="CompressOutcome"/> 回报结局。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么存在：</b>压缩业务执行原本写在 <c>MainWindowViewModel.ExecuteCompressFromSettings</c>
/// （以及 CLI 的 <c>CompressWithProgress</c>）里，两条路径几乎逐行相同：构建
/// <c>CompressRequest</c>、开进度窗、跑引擎、成功后写路径历史与密码库。本类把这段「业务执行」
/// 吸收成唯一实现，宿主只保留「怎么拿到输入」和「怎么展示结局」。
/// </para>
/// <para>
/// <b>刻意不做的事：</b>
/// </para>
/// <list type="bullet">
/// <item><description>不回读任何 ViewModel：所有输入来自 <see cref="CompressSettings"/>，宿主环境来自 <see cref="CompressRunContext"/>。</description></item>
/// <item><description>不重复弹窗：异常文案已由展示器呈现，这里只把结论塞进 <see cref="CompressOutcome.Failed"/> 供日志/诊断。</description></item>
/// <item><description>不改压缩参数与输出命名：请求字段逐项对应原实现的赋值，不顺手调参。</description></item>
/// </list>
/// <para>
/// <b>关于构造函数里的委托端口：</b>密码库、路径历史、设置加载与「压缩引擎调用」都以委托形式注入，
/// 默认实现直接指向现有单例/服务。这样做不是为了抽象而抽象，而是为了让本类在没有数据库、
/// 没有真实引擎的测试里也能验证「结局映射」这一最容易被写错的部分（取消 → 静默、
/// 异常 → Failed、前置不成立 → NotStarted）。
/// </para>
/// </remarks>
public sealed class CompressSession : ICompressSession
{
    /// <summary>压缩引擎调用端口；默认走 <see cref="AvaloniaCompressService"/>。</summary>
    private readonly Func<
        CompressRequest,
        IProgress<ArchiveProgress>?,
        CancellationToken,
        CompressConflictResolver?,
        Action<int, BatchItemStatus>?,
        Action<int, string>?,
        Task<CompressResult>> _compressAsync;

    /// <summary>冲突弹窗端口；默认走 <see cref="CompressFlow.ShowConflictDialogAsync"/>。</summary>
    private readonly Func<Window, CompressConflictInfo, Task<(CompressConflictAction Action, string? CustomName, bool ApplyToAll)>> _showConflictDialog;

    /// <summary>成功后把输出目录写入路径历史的端口。</summary>
    private readonly Action<CompressRequest> _recordOutputHistory;

    /// <summary>密码库「新增条目」端口。</summary>
    private readonly Action<string, string, IReadOnlyList<string>> _addPassword;

    /// <summary>密码库「更新已有条目」端口。</summary>
    private readonly Action<string, string, string, IReadOnlyList<string>> _updatePassword;

    /// <summary>密码库「标记条目已使用」端口。</summary>
    private readonly Action<string> _markPasswordUsed;

    /// <summary>应用设置加载端口（只读：保留目录根、自定义格式、自适应覆盖规则）。</summary>
    private readonly Func<AppSettings> _loadSettings;

    /// <summary>
    /// 「压缩完成后打开输出目录」端口：<see langword="null"/> = 该宿主没有这个能力
    /// （CLI 弹窗流程、测试流程），进度窗口底部开关与收尾打开一并隐藏 / 跳过。
    /// 能力靠构造注入表达，不给上下文加「是不是 CLI」这类宿主身份布尔。
    /// </summary>
    private readonly Func<IReadOnlyList<string>, Task>? _openOutputFolder;

    /// <summary>
    /// 创建会话。
    /// </summary>
    /// <param name="compressAsync">压缩引擎调用端口；为 <see langword="null"/> 时走默认实现。</param>
    /// <param name="showConflictDialog">冲突弹窗端口；为 <see langword="null"/> 时走默认实现。</param>
    /// <param name="recordOutputHistory">路径历史记录端口；为 <see langword="null"/> 时写入真实历史。</param>
    /// <param name="addPassword">密码库新增端口；为 <see langword="null"/> 时写真实密码库。</param>
    /// <param name="updatePassword">密码库更新端口；为 <see langword="null"/> 时写真实密码库。</param>
    /// <param name="markPasswordUsed">密码库标记已使用端口；为 <see langword="null"/> 时写真实密码库。</param>
    /// <param name="loadSettings">应用设置加载端口；为 <see langword="null"/> 时读真实设置。</param>
    /// <param name="openOutputFolder">成功后打开输出目录的端口；为 <see langword="null"/> 表示该宿主没有这个能力（CLI / 测试流程）。</param>
    public CompressSession(
        Func<
            CompressRequest,
            IProgress<ArchiveProgress>?,
            CancellationToken,
            CompressConflictResolver?,
            Action<int, BatchItemStatus>?,
            Action<int, string>?,
            Task<CompressResult>>? compressAsync = null,
        Func<Window, CompressConflictInfo, Task<(CompressConflictAction Action, string? CustomName, bool ApplyToAll)>>? showConflictDialog = null,
        Action<CompressRequest>? recordOutputHistory = null,
        Action<string, string, IReadOnlyList<string>>? addPassword = null,
        Action<string, string, string, IReadOnlyList<string>>? updatePassword = null,
        Action<string>? markPasswordUsed = null,
        Func<AppSettings>? loadSettings = null,
        Func<IReadOnlyList<string>, Task>? openOutputFolder = null)
    {
        _compressAsync = compressAsync ?? ((request, progress, ct, conflictResolver, onItemStatus, onItemOutputPath) =>
            DefaultCompressAsync(request, progress, ct, conflictResolver, onItemStatus, onItemOutputPath));
        _showConflictDialog = showConflictDialog ?? ((owner, info) => CompressFlow.ShowConflictDialogAsync(owner, info));
        _recordOutputHistory = recordOutputHistory ?? (request => CompressFlow.RecordOutputHistory(request));
        _addPassword = addPassword ?? ((password, description, patterns) =>
            PasswordManager.Instance.AddPassword(password, description, new List<string>(patterns)));
        _updatePassword = updatePassword ?? ((id, password, description, patterns) =>
            PasswordManager.Instance.UpdatePassword(id, password, description, new List<string>(patterns)));
        _markPasswordUsed = markPasswordUsed ?? (id => PasswordManager.Instance.MarkUsed(id));
        _loadSettings = loadSettings ?? (() => AppSettings.Load());
        _openOutputFolder = openOutputFolder;
    }

    /// <inheritdoc />
    public async Task<CompressOutcome> RunAsync(
        CompressSettings settings,
        CompressRunContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        // ① 调用方在进入之前就已经取消：直接算取消，不进进度窗口、不弹任何提示。
        if (cancellationToken.IsCancellationRequested)
            return CompressOutcome.Cancelled;

        try
        {
            // ② 把冻结的意图翻译成引擎请求（含源校验与目标路径解析、过滤器白名单裁剪）。
            //    这一步等价于原 ExecuteCompressFromSettings 开头的 BuildRequest + null 判定。
            var request = BuildRequest(settings, _loadSettings());
            if (request == null)
            {
                // 前置不成立（典型：过滤器把条目全裁掉）——原实现弹一个警告框；
                // 这里只把原因交回宿主，由宿主决定怎么展示，避免执行器私自带 UI 依赖。
                return CompressOutcome.NotStarted(LocalizationManager.T("Compress_FilteredAllSkipped"));
            }

            // ③ 进度窗口要显示的行：宿主显式给了就用宿主的，否则按请求算出输出路径
            //    （等价于原实现的 AvaloniaCompressService.GetOutputPaths(request)）。
            var displayPaths = context.DisplayPaths is { Count: > 0 }
                ? context.DisplayPaths
                : AvaloniaCompressService.GetOutputPaths(request);

            bool completed;
            // 进度窗口底部开关的实时值（窗口关掉后回读；未显示开关时为 null → 回落到设置）。
            bool? finalOpenFolder = null;
            try
            {
                completed = await context.Presenter.RunAsync(new ProgressRunRequest
                {
                    Title = LocalizationManager.T("Status_Compressing"),
                    DisplayPaths = displayPaths,
                    // ④ 把流程标志映射到窗口：它们是「怎么呈现」的细节，不进 presenter 契约，
                    //    也不进设置载荷，只在会话这一层贴到窗口实例上。
                    ConfigureWindow = progressWindow =>
                    {
                        progressWindow.IsCompressFlow = context.IsCompressFlow;
                        progressWindow.IsTestFlow = context.IsTestFlow;

                        // 列表播种：原实现靠 VM 上的 PendingCompressRequest 让 RunWithProgress
                        // 枚举全量条目；现在由会话在窗口 Show 之前直接把请求递过去。
                        if (context.IsCompressFlow)
                            CompressFlow.TrySeedEntryItemsInBackground(progressWindow, request, progressWindow.CancellationToken);

                        // 「压缩完成后打开目录」开关：只有宿主注入了打开能力才显示（CLI / 测试流程没有）。
                        if (_openOutputFolder is not null)
                            progressWindow.SetCompressPostActions(openFolder: true, openFolderSeed: settings.OpenFolderAfterCompress);
                    },
                    Operation = async (progress, runToken) =>
                    {
                        // ⑤ 进度窗口自己的取消令牌与调用方令牌合并：任一触发都取消压缩。
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(runToken, cancellationToken);

                        await _compressAsync(
                            request,
                            progress,
                            linked.Token,
                            CompressFlow.CreateResolver(info => ResolveConflictAsync(context, info), request.ConflictAction),
                            // ⑥ 运行期逐条目状态写到当前进度窗口（原实现走 VM 上的 BatchStatusReporter）。
                            (index, status) =>
                            {
                                if (context.Presenter.ActiveWindow is { } window)
                                {
                                    window.SetCurrentBatchItem(index);
                                    window.UpdateBatchItemStatus(index, status);
                                }
                            },
                            (index, outputPath) => context.Presenter.ActiveWindow?.UpdateBatchItemName(index, outputPath));

                        // ⑥b 压缩结束后立刻回读窗口底部开关（presenter 随后会关窗，关掉就读不到了）。
                        finalOpenFolder = context.Presenter.ActiveWindow?.OpenFolderAfterCompressValue;
                    },
                });
            }
            catch (OperationCanceledException)
            {
                // 取消不是失败：presenter 正常会吞掉取消并返回 false，这里只是兜底。
                completed = false;
            }
            catch (Exception ex)
            {
                // ⑦ 执行异常：presenter 已把文案呈现给用户，这里只记录结论，绝不重复弹窗。
                var (errorText, _) = WholeArchiveErrorLocalizer.ToUserText(ex);
                return CompressOutcome.Failed(errorText);
            }

            // ⑧ presenter 返回 false = 用户在进度窗口点了取消 / 直接关掉了窗口：静默收场。
            if (!completed)
                return CompressOutcome.Cancelled;

            // ⑨ 成功侧收尾（顺序与原实现一致：先写路径历史，再写密码库）。
            _recordOutputHistory(request);
            WritePasswordToLibrary(settings);

            // ⑨b 打开输出所在目录并选中产物：宿主注入了能力才做，开关以进度窗口上的实时值为准。
            if (_openOutputFolder is not null && (finalOpenFolder ?? settings.OpenFolderAfterCompress))
                await _openOutputFolder(displayPaths);

            return CompressOutcome.Completed;
        }
        finally
        {
            // ⑩ 按宿主的退出策略收尾：成功、取消、失败、前置不成立四条路径都要执行，
            //    否则 CLI 隐藏主窗口后会挂在后台不退。
            ApplyExitPolicy(context);
        }
    }

    /// <summary>默认引擎调用：走 UI 层包装（它再转给 Core 的 <c>CompressService</c>）。</summary>
    private static Task<CompressResult> DefaultCompressAsync(
        CompressRequest request,
        IProgress<ArchiveProgress>? progress,
        CancellationToken ct,
        CompressConflictResolver? conflictResolver,
        Action<int, BatchItemStatus>? onItemStatus,
        Action<int, string>? onItemOutputPath)
        => new AvaloniaCompressService().CompressAsync(
            request, progress, ct, conflictResolver, onItemStatus, onItemOutputPath);

    /// <summary>
    /// 把冻结的意图翻译成引擎请求；返回 <see langword="null"/> 表示「没有可执行的条目」。
    /// </summary>
    /// <remarks>
    /// 与原 <c>CompressFlow.BuildRequest(CompressSettingsViewModel)</c> 逐字段等价——包括
    /// 过滤器白名单的两种语义（<c>null</c> = 未过滤、空集合 = 全被裁掉）以及 <c>Separate</c>
    /// 模式下不写 <c>OutputPath</c>（每个源自己生成目标包）。任何字段映射的改动都属于行为变更，
    /// 不要在这里顺手做。
    /// </remarks>
    /// <param name="settings">冻结的压缩设置。</param>
    /// <param name="appSettings">应用设置（提供保留目录根、自定义格式、自适应覆盖规则）。</param>
    /// <returns>可直接交给引擎的请求；<see langword="null"/> 表示没有可归档的源。</returns>
    private static CompressRequest? BuildRequest(CompressSettings settings, AppSettings appSettings)
    {
        var items = settings.Plan;

        // 只要有任意一条启用了过滤器，就按「过滤后白名单」整体裁一遍：全空即无可执行条目。
        var filterActive = items.Any(entry => entry.IncludedFiles != null);
        if (filterActive)
        {
            items = items.Where(entry => entry.IncludedFiles is { Count: > 0 }).ToList();
            if (items.Count == 0)
                return null;
        }

        var plan = new CompressPlan(
            settings.OutputMode,
            settings.OutputPath,
            items
                .Select(entry => new CompressPlanItem(entry.SourcePath, entry.TargetArchivePath, entry.IncludedFiles))
                .ToList());

        return new CompressRequest
        {
            SourcePaths = items.Select(entry => entry.SourcePath).ToList(),
            Mode = settings.OutputMode,
            Format = settings.Format,
            CompressionLevel = settings.CompressionLevel,
            Password = settings.Password,
            Encrypt = settings.Encrypt,
            Comment = settings.Comment,
            CommentDistribution = settings.CommentDistribution,
            OutputPath = settings.OutputMode switch
            {
                CompressOutputMode.Separate => null,
                _ => settings.OutputPath,
            },
            SplitSize = settings.SplitSizeBytes,
            PreserveDirectoryRoot = appSettings.PreserveDirectoryRoot,
            KeepOriginalExtension = settings.EngineOptions.KeepOriginalExtension,
            FileNameEncoding = settings.EngineOptions.FileNameEncoding,
            ZipCompressionMethod = settings.EngineOptions.ZipCompressionMethod,
            ZipEncryptionMethod = settings.EngineOptions.ZipEncryptionMethod,
            SevenZipCompressionMethod = settings.EngineOptions.SevenZipCompressionMethod,
            SevenZipSolid = settings.EngineOptions.SevenZipSolid,
            SevenZipSolidBlockSize = settings.EngineOptions.SevenZipSolidBlockSize,
            SevenZipDictionarySize = settings.EngineOptions.SevenZipDictionarySize,
            SevenZipNumFastBytes = settings.EngineOptions.SevenZipNumFastBytes,
            SevenZipMatchFinder = settings.EngineOptions.SevenZipMatchFinder,
            SevenZipMultithreaded = settings.EngineOptions.SevenZipMultithreaded,
            SevenZipEncryptHeaders = settings.EngineOptions.SevenZipEncryptHeaders,
            AdaptiveCompression = settings.EngineOptions.AdaptiveCompression,
            AdaptiveSmartDetect = settings.EngineOptions.AdaptiveSmartDetect,
            MultiThreadedCompression = settings.MultiThreaded,
            // 全局设置项（不属于「用户在对话框里的决定」）：与原 BuildRequest 一样在构建请求时读取，
            // 而不是冻进确认载荷，避免把全局配置混进「这次压缩的意图」。
            AdaptiveOverrides = new List<AdaptiveOverrideRule>(
                appSettings.AdaptiveOverrides ?? new List<AdaptiveOverrideRule>()),
            CustomFormats = new List<FormatDefinition>(
                appSettings.CustomFormats ?? new List<FormatDefinition>()),
            ConflictAction = settings.ConflictAction,
            ParallelCompressDegree = settings.ParallelDegree,
            Plan = plan,
            ErrorResolver = CompressFlow.CreateErrorResolver(),
        };
    }

    /// <summary>解析一次目标路径冲突；没有可用宿主窗口时沿用现状的「静默覆盖」。</summary>
    /// <remarks>
    /// 原实现的分支是「<c>ShowCompressConflictDialog == null</c> → 覆盖」：主窗口在没有注册
    /// 弹窗回调时走这条退路，CLI 则直接把自己的进度窗口当属主传给
    /// <c>CompressFlow.ShowConflictDialogAsync</c>。这里把两种情况统一成「先看宿主给的属主，
    /// 没有就退到当前进度窗口」——进度窗口在 CLI 与 GUI 下都存在，且它本身就是冲突弹窗的
    /// 合理属主（模态弹窗必须挂在可见窗口上，否则会掉到后面）。
    /// </remarks>
    /// <param name="context">宿主上下文（提供属主窗口与当前进度窗口）。</param>
    /// <param name="info">冲突信息（目标文件已存在、可追加性等）。</param>
    /// <returns>冲突动作、可选的重命名名、是否应用到全部。</returns>
    private async Task<(CompressConflictAction Action, string? CustomName, bool ApplyToAll)> ResolveConflictAsync(
        CompressRunContext context,
        CompressConflictInfo info)
    {
        var owner = context.Owner ?? context.Presenter.ActiveWindow;
        if (owner == null)
            return (CompressConflictAction.Overwrite, null, false);

        return await _showConflictDialog(owner, info);
    }

    /// <summary>
    /// 压缩成功后把密码写回密码库（相当于原实现里的 SavePasswordAfterCompress 段）。
    /// </summary>
    /// <remarks>
    /// 三处刻意的等价细节：
    /// ① 只在「用户勾了保存」且「本次确实加密」时才写；
    /// ② 密码库模式（从库里选已有条目）只做「匹配规则去重追加」，不新建条目、不改密码本身；
    /// ③ 规则为空时按格式补一条 <c>*.{ext}</c> 兜底（tar.gz 特殊处理成 <c>*.tar.gz</c>，
    ///   否则会被拆成 <c>*.tar</c> + <c>.gz</c> 两个后缀）。
    /// 写库失败不影响压缩结局——与现状一样只记诊断日志。
    /// </remarks>
    /// <param name="settings">冻结的压缩设置（含密码回写意图）。</param>
    private void WritePasswordToLibrary(CompressSettings settings)
    {
        var writeback = settings.PasswordWriteback;
        if (!writeback.SaveToLibrary || !settings.Encrypt)
            return;

        try
        {
            var rules = writeback.RulesText
                ?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(rule => rule.Trim())
                .Where(rule => !string.IsNullOrWhiteSpace(rule))
                .ToList() ?? new List<string>();

            if (rules.Count == 0)
            {
                var extension = settings.Format == "tar.gz" ? ".tar.gz" : "." + settings.Format;
                rules.Add($"*{extension}");
            }

            if (writeback.IsLibraryMode && writeback.LibraryEntryId is { Length: > 0 } entryId)
            {
                // 已有条目：把新规则去重后追加，然后回写整条记录并标记「已使用」。
                var patterns = new List<string>(writeback.LibraryEntryPatterns);
                var updated = false;
                foreach (var rule in rules)
                {
                    if (patterns.Contains(rule))
                        continue;

                    patterns.Add(rule);
                    updated = true;
                }

                if (updated)
                {
                    _updatePassword(
                        entryId,
                        settings.Password ?? string.Empty,
                        writeback.LibraryEntryDescription ?? string.Empty,
                        patterns);
                    _markPasswordUsed(entryId);
                    App.DebugLog($"Password rules updated for entry: {writeback.LibraryEntryDescription}");
                }
            }
            else if (!writeback.IsLibraryMode)
            {
                // 新条目：描述为空时补一个带时间戳的默认值。
                // 规则 13 豁免说明：这是写入密码库条目的内容数据（与界面语言无关），
                // 沿用既有实现的英文字面量，避免同一密码库在不同界面语言下出现两种描述。
                var description = writeback.Description?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(description))
                    description = $"Compressed on {DateTime.Now:yyyy-MM-dd HH:mm}";

                if (settings.Password != null)
                {
                    _addPassword(settings.Password, description, rules);
                    App.DebugLog($"Password saved to library: {description}");
                }
            }
        }
        catch (Exception ex)
        {
            // 密码库写失败不能把「压缩已成功」改判成失败——只记录诊断信息。
            App.DebugLog($"SavePasswordAfterCompress failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 按 <see cref="CompressRunContext.ExitPolicy"/> 收尾宿主的进程退出。
    /// </summary>
    /// <remarks>
    /// CLI 弹窗流程用 <c>ShutdownMode.OnExplicitShutdown</c>，隐藏主窗口后必须显式退出，
    /// 否则进程挂在后台；桌面主窗口流程（<see cref="ExitPolicy.None"/>）什么都不做。
    /// 四条结局路径都要调用——取消与失败时 CLI 同样要退。
    /// </remarks>
    /// <param name="context">宿主上下文（提供退出策略）。</param>
    private static void ApplyExitPolicy(CompressRunContext context)
    {
        if (context.ExitPolicy != ExitPolicy.ShutdownAfterRun)
            return;

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                AppShutdown.Request(desktop.Shutdown);
        }
        catch (Exception ex)
        {
            // 退出请求失败不应该把业务结局改成失败，只记录诊断信息。
            App.DebugLog($"CompressSession exit policy failed: {ex.Message}");
        }
    }
}
