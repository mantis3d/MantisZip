using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.Core.Services;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.Dialogs;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// 解压设置对话框的 ViewModel。
/// 用户选择目标路径、冲突策略和是否打开文件夹；点「开始解压」时由确认事务
/// （<see cref="TryConfirmAsync"/>）一次性产出 <see cref="ExtractSettings"/> 载荷，
/// 结局经 <see cref="ConfirmCompleted"/> 交给窗口——拒绝留窗并显示原因，确认/取消关窗。
/// </summary>
public partial class ExtractSettingsViewModel : ObservableObject
{
    /// <summary>初始传入的压缩包路径列表（只读）。</summary>
    public IReadOnlyList<string> ArchivePaths { get; }

    /// <summary>最终保留的文件路径列表（可修改后回读）。</summary>
    public List<string> SelectedPaths { get; }

    /// <summary>本地化字符串字典，供 XAML 绑定。</summary>
    public Dictionary<string, string> LocalizedStrings { get; }

    /// <summary>由 View 设置的文件夹选择回调。返回选择的路径，取消返回 null。</summary>
    public Func<Task<string?>>? BrowseFolder { get; set; }

    /// <summary>
    /// 由 View 设置的提示回调（拒绝原因必须让用户看见）：参数为已本地化的文案与对话框标题。
    /// </summary>
    public Func<string, string, Task>? ShowMessage { get; set; }

    /// <summary>
    /// 确认事务的结局出口（确认 / 拒绝 / 取消）：窗口订阅后据此关窗或留窗。
    /// 与压缩侧 <c>CompressSettingsViewModel.ConfirmCompleted</c> 同款式，替代旧的 <c>CloseAction(bool)</c> 双轨回传。
    /// </summary>
    public event Action<ConfirmOutcome<ExtractSettings>>? ConfirmCompleted;

    [ObservableProperty]
    private string _destinationPath = string.Empty;

    [ObservableProperty]
    private string _conflictAction = "ask";

    [ObservableProperty]
    private bool _openFolderAfterExtract;

    // ── 并行解压（本次解压生效；初值读取设置，不回写设置） ──
    /// <summary>全局并行开关（关闭则始终串行）。</summary>
    [ObservableProperty]
    private bool _enableParallelExtract = true;

    /// <summary>
    /// 解压后将原压缩包移入回收站。默认值读取 AppSettings.DeleteArchiveAfterExtract，
    /// 但勾选状态仅对本次解压生效，不写回全局设置。
    /// </summary>
    [ObservableProperty]
    private bool _deleteArchiveAfterExtract;

    /// <summary>并行解压线程数（1=串行，>1=并行）。默认 = CPU核心数。</summary>

    /// <summary>智能并行度开关（按磁盘类型/预测试选线程数）。</summary>
    [ObservableProperty]
    private bool _smartParallelDegree = true;

    /// <summary>智能模式 SSD 线程数（1-16）。</summary>
    [ObservableProperty]
    private int _smartSsdDegree = 12;

    /// <summary>智能模式 HDD 线程数（1-16）。</summary>
    [ObservableProperty]
    private int _smartHddDegree = 8;

    /// <summary>智能关闭时使用的固定线程数（1-16）。</summary>
    [ObservableProperty]
    private int _manualParallelDegree = 4;

    /// <summary>大包预测试开关（仅智能模式参与决策链第 4 步）。</summary>
    [ObservableProperty]
    private bool _largeArchivePreTest = true;

    partial void OnSmartParallelDegreeChanged(bool value)
    {
        OnPropertyChanged(nameof(SmartParallelDegreeText));
        NotifyParallelVisibility();
    }

    partial void OnEnableParallelExtractChanged(bool value) => NotifyParallelVisibility();

    /// <summary>集中通知并行解压相关派生属性（规则：集中通知模式）。</summary>
    private void NotifyParallelVisibility()
    {
        OnPropertyChanged(nameof(IsSmartDegreeVisible));
        OnPropertyChanged(nameof(IsManualDegreeVisible));
        OnPropertyChanged(nameof(IsPreTestVisible));
    }

    /// <summary>智能并行度复选框文案（开/关两种）。</summary>
    public string SmartParallelDegreeText => SmartParallelDegree
        ? LocalizationManager.T("Settings_SmartParallelDegree")
        : LocalizationManager.T("Settings_SmartParallelDegreeOff");

    /// <summary>SSD/HDD 线程数可见性：全局开关与智能并行度均开启时显示。</summary>
    public bool IsSmartDegreeVisible => EnableParallelExtract && SmartParallelDegree;

    /// <summary>手动线程数可见性：全局开关开启且智能关闭时显示。</summary>
    public bool IsManualDegreeVisible => EnableParallelExtract && !SmartParallelDegree;

    /// <summary>大包预测试开关可见性：全局开关与智能并行度均开启时显示。</summary>
    public bool IsPreTestVisible => EnableParallelExtract && SmartParallelDegree;

    /// <summary>构建本次解压的并行设置快照（初值读设置、本次解压生效、不回写设置）。</summary>
    public ParallelExtractSettings BuildParallelSettings() => new()
    {
        EnableParallel = EnableParallelExtract,
        SmartParallel = SmartParallelDegree,
        SmartSsdDegree = Math.Clamp(SmartSsdDegree, 1, 16),
        SmartHddDegree = Math.Clamp(SmartHddDegree, 1, 16),
        LargeArchivePreTest = LargeArchivePreTest,
        ManualDegree = Math.Clamp(ManualParallelDegree, 1, 16)
    };

    // ── Preview tree ──

    /// <summary>预览树的根节点。</summary>
    [ObservableProperty]
    private PreviewTreeNode? _previewRoot;

    /// <summary>预览面板是否启用精简模式。</summary>
    [ObservableProperty]
    private bool _previewCompactMode = true;

    /// <summary>是否显示过滤项。</summary>
    [ObservableProperty]
    private bool _showFilteredGhosts;

    /// <summary>预览树是否正在后台构建（构建超过阈值后置 true，驱动加载覆层显示）。</summary>
    [ObservableProperty]
    private bool _isPreviewBuilding;

    /// <summary>
    /// 预览树构建是否进行中（无论快慢，构建开始即置位，驱动"开始解压"按钮门禁）。
    /// 与 <see cref="IsPreviewBuilding"/>（仅慢构建 ≥250ms 置位，驱动加载覆层）不同：
    /// 快构建也会短暂置位，保证确认事务取到的过滤集合（条目预览可见部分）就绪前无法点击解压。
    /// </summary>
    [ObservableProperty]
    private bool _isBuildPending;

    partial void OnIsBuildPendingChanged(bool value) => ExtractCommand.NotifyCanExecuteChanged();

    /// <summary>预览树构建进度（0–100，-1 表示不确定进度/不定进度条）。</summary>
    [ObservableProperty]
    private double _previewBuildProgress = -1;


    // ── 逐包校验与预览跟随选中 ──

    /// <summary>源压缩包列表行模型（状态徽标 + 点击切换预览）。</summary>
    public ObservableCollection<SourceArchiveItem> SourceItems { get; } = new();

    /// <summary>当前选中（预览树正在展示）的压缩包行。</summary>
    [ObservableProperty]
    private SourceArchiveItem? _selectedSourceItem;

    /// <summary>条目缓存：校验成功的包把条目存这里，点击行切换预览零额外 IO。</summary>
    private readonly Dictionary<string, IReadOnlyList<ArchiveItem>> _entriesCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 已构建子树缓存（path→归档节点）。重建时只构建增量，
    /// 目标路径/过滤变化整体失效（InvalidatePreviewCache），解锁等单包变化定点失效。
    /// </summary>
    private readonly Dictionary<string, PreviewTreeNode> _subTreeCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>合并重建进行中标记（UI 线程串行；期间新请求合并为完成后补一轮）。</summary>
    private bool _isRebuilding;

    /// <summary>dirty 标记：重建期间收到新请求，当前轮结束后补一轮。</summary>
    private bool _rebuildPending;

    /// <summary>
    /// 第一个压缩包的条目。过滤统计与实际提取语义保持绑定首包
    /// （对齐 WPF HandleExtractBatchCore「过滤仅对 i==0 生效」的设计），与预览展示解耦。
    /// </summary>
    private IReadOnlyList<ArchiveItem>? _firstArchiveEntries;

    /// <summary>首包条目只读访问（窗口的过滤统计与预览灰显用；过滤集合只在确认事务里计算）。</summary>
    public IReadOnlyList<ArchiveItem>? FirstArchiveEntries => _firstArchiveEntries;

    /// <summary>是否存在多个源压缩包（驱动「过滤仅对首包生效」提示显隐）。</summary>
    public bool HasMultipleArchives => SourceItems.Count > 1;

    /// <summary>条目列表正在后台读取（驱动预览树 ⏳ 占位；区别于树构建的 IsPreviewBuilding）。</summary>
    [ObservableProperty]
    private bool _isListingPending;

    /// <summary>当前选中包加载失败时的标题（空串 = 无错误态；损坏 / 需要密码）。</summary>
    [ObservableProperty]
    private string _previewErrorTitle = "";

    /// <summary>加载失败详情（异常原因原文）。</summary>
    [ObservableProperty]
    private string _previewErrorDetail = "";

    /// <summary>失败是否因需要密码（预览树 🔒 / ⚠️ 图标切换）。</summary>
    [ObservableProperty]
    private bool _previewNeedsPassword;

    /// <summary>由窗口注入当前过滤条件读取器（FileFilterControl.GetFilter 封装）。</summary>
    public Func<FileFilterCriteria?>? FilterProvider { get; set; }

    /// <summary>
    /// 由 View 注入：弹出密码输入对话框（复用主流程 PasswordDialog），取消返回 null。
    /// 用于自动匹配失败后的手动解锁。
    /// </summary>
    public Func<string, Task<PasswordDialogResponse?>>? ShowUnlockDialog { get; set; }

    private readonly PasswordService _passwordService = new();

    /// <summary>已确定的密码字典（只由确认事务整表冻结进载荷，不再对外暴露回读通道）。</summary>
    private readonly Dictionary<string, string> _matchedPasswords = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>单包/逐包冲突扫描的取消令牌。</summary>
    private CancellationTokenSource? _conflictCts;

    /// <summary>
    /// 取消正在进行的冲突扫描（窗口关闭时调用，避免无谓 I/O）。
    /// </summary>
    public void CancelConflictScan()
    {
        _conflictCts?.Cancel();
        _conflictCts?.Dispose();
        _conflictCts = null;
    }

    /// <summary>
    /// 冲突标记已完成一轮扫描（含两阶段），View 收到后调用 RefreshDisplay 更新计数。
    /// 仅当该轮扫描未被取消时触发。
    /// </summary>
    public event EventHandler? PreviewTreeInvalidated;

    /// <summary>当前选中行是否可手动输密码（🔒 无法列出 / 🔑 加密未匹配）。</summary>
    [ObservableProperty]
    private bool _canUnlockSelected;

    partial void OnCanUnlockSelectedChanged(bool value) => UnlockSelectedCommand.NotifyCanExecuteChanged();

    private void UpdateCanUnlockSelected()
        => CanUnlockSelected = IsUnlockable(SelectedSourceItem);

    /// <summary>该行是否处于可手动输密码的状态。</summary>
    public static bool IsUnlockable(SourceArchiveItem? item)
        => item is { Status: SourceArchiveStatus.NeedsPassword }
        || item is { Status: SourceArchiveStatus.Ok, IsEncrypted: true, MatchedPassword: null };

    partial void OnSelectedSourceItemChanged(SourceArchiveItem? value) => UpdateCanUnlockSelected();

    public ExtractSettingsViewModel(IReadOnlyList<string> archivePaths)
    {
        ArchivePaths = archivePaths;
        SelectedPaths = archivePaths.ToList();

        // 默认目标路径：第一个压缩包所在目录/压缩包名
        if (archivePaths.Count > 0)
        {
            var dir = Path.GetDirectoryName(archivePaths[0]) ?? "";
            var name = Path.GetFileNameWithoutExtension(archivePaths[0]);
            DestinationPath = Path.Combine(dir, name);
        }

        // 预选当前设置中的冲突策略（对齐 WPF ExtractSettingsWindow 从 FileConflictAction 预选）
        var settings = AppSettings.Load();
        if (!string.IsNullOrEmpty(settings.FileConflictAction))
            ConflictAction = settings.FileConflictAction;
        // 并行解压（本次解压生效；初值读取设置，不回写设置——对话框镜像设置但不改变设置）
        _enableParallelExtract = settings.EnableParallelExtract;
        _smartParallelDegree = settings.SmartParallelDegree;
        _smartSsdDegree = Math.Clamp(settings.SmartSsdDegree, 1, 16);
        _smartHddDegree = Math.Clamp(settings.SmartHddDegree, 1, 16);
        _manualParallelDegree = Math.Clamp(settings.ParallelExtractDegree, 1, 16);
        _largeArchivePreTest = settings.LargeArchivePreTest;
        // 解压后打开文件夹（对齐 WPF ExtractSettingsWindow.LoadDefaultsFromSettings 读取 OpenFolderAfterExtract）
        OpenFolderAfterExtract = settings.OpenFolderAfterExtract;
        // 解压后将原压缩包移入回收站：读取全局设置作为默认，勾选仅本次解压生效（不写回 AppSettings）
        DeleteArchiveAfterExtract = settings.DeleteArchiveAfterExtract;

        // 冲突策略选项（ComboBox 用对象绑定——Avalonia 无 WPF 的 SelectedValuePath）
        ConflictActionOptions.Add(new Option(LocalizationManager.T("Extract_Conflict_Ask"), "ask"));
        ConflictActionOptions.Add(new Option(LocalizationManager.T("Extract_Conflict_Overwrite"), "overwrite"));
        ConflictActionOptions.Add(new Option(LocalizationManager.T("Settings_Extract_Conflict_OverwriteOlder"), "overwrite-if-older"));
        ConflictActionOptions.Add(new Option(LocalizationManager.T("Settings_Extract_Conflict_OverwriteSmaller"), "overwrite-if-smaller"));
        ConflictActionOptions.Add(new Option(LocalizationManager.T("Extract_Conflict_Rename"), "rename"));
        ConflictActionOptions.Add(new Option(LocalizationManager.T("Extract_Conflict_Skip"), "skip"));
        SelectedConflictActionOption =
            ConflictActionOptions.FirstOrDefault(o => o.Value == ConflictAction) ?? ConflictActionOptions[0];

        // 初始化本地化字符串
        var keys = new[]
        {
            "Extract_Title",
            "Extract_SourceArchives",
            "Extract_Destination",
            "Extract_DestinationPlaceholder",
            "Extract_Browse",
            "Extract_WhenFileExists",
            "Extract_ConflictAction",
            "Extract_Conflict_Ask",
            "Extract_Conflict_Overwrite",
            "Extract_Conflict_Rename",
            "Extract_Conflict_Skip",
            "Extract_OpenFolder",
            "Settings_Extract_DeleteArchiveAfterExtract",
            "Extract_Start",
            "Extract_Cancel",
            "Extract_TabFilter",
            "Extract_Source_MultiFilterHint",
            "Extract_UnlockButton",
            "Settings_ExtractTab_Parallel",
            "Settings_EnableParallelExtract",
            "Settings_SmartSsdLabel",
            "Settings_SmartHddLabel",
            "Settings_ManualDegreeLabel",
            "Settings_ManualDegreeHint",
            "Settings_LargeArchivePreTest"
        };
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            dict[key] = LocalizationManager.T(key);
        }
        LocalizedStrings = dict;

        // 源压缩包行模型（默认全部 Pending；Loaded 后 ValidateAllAsync 逐包校验）
        foreach (var path in archivePaths)
            SourceItems.Add(new SourceArchiveItem(path));

        // 默认选中首包（列表高亮用，不再驱动预览）；初始 ⏳ 占位由首次合并重建接管
        SelectedSourceItem = SourceItems.FirstOrDefault();
        IsListingPending = SourceItems.Count > 0;
    }

    /// <summary>冲突策略下拉选项（显示文本 + 存储值）。</summary>
    public ObservableCollection<Option> ConflictActionOptions { get; } = new();

    /// <summary>当前选中的冲突策略选项（同步写回 <see cref="ConflictAction"/> 字符串值）。</summary>
    [ObservableProperty]
    private Option? _selectedConflictActionOption;

    partial void OnSelectedConflictActionOptionChanged(Option? value)
    {
        if (value != null)
            ConflictAction = value.Value;
    }

    // ── 逐包校验与合并预览树 ──

    /// <summary>
    /// 记录外部已加载的首包条目（MainWindow 单包路径：打开压缩包时已成功列出）。
    /// 直接标记 Ok 并刷新首行预览，跳过对首包的重复读取。
    /// </summary>
    public void SetFirstArchiveEntries(IReadOnlyList<ArchiveItem> entries)
    {
        if (SourceItems.Count == 0) return;

        var first = SourceItems[0];
        _entriesCache[first.Path] = entries;
        _firstArchiveEntries = entries;
        first.ErrorMessage = null;
        first.Status = SourceArchiveStatus.Ok;

        RebuildMergedPreview();
    }

    /// <summary>
    /// 逐包并发校验（限流 3），状态实时回写行模型供列表徽标显示。
    /// 成功条目入缓存供点击切换预览；首包条目兼作过滤/提取数据源（既有语义）。
    /// </summary>
    public async Task ValidateAllAsync(CancellationToken ct = default)
    {
        var pending = SourceItems.Where(i => i.Status == SourceArchiveStatus.Pending).ToList();
        using var gate = new SemaphoreSlim(3);
        var tasks = pending.Select(item => Task.Run(() => ValidateOneAsync(item, gate, ct), ct));
        await Task.WhenAll(tasks);
    }

    private async Task ValidateOneAsync(SourceArchiveItem item, SemaphoreSlim gate, CancellationToken ct)
    {
        item.Status = SourceArchiveStatus.Validating;
        try
        {
            await gate.WaitAsync(ct);
            try
            {
                var engine = ArchiveEngineFactory.GetEngineByExtension(item.Path);
                if (engine == null)
                {
                    item.ErrorMessage = LocalizationManager.T("Status_UnsupportedFormat",
                        System.IO.Path.GetExtension(item.Path));
                    item.Status = SourceArchiveStatus.Failed;
                    return;
                }

                IReadOnlyList<ArchiveItem>? entries;
                bool lockedType = false;   // A 类：加密文件名等，无密码列不出条目

                try
                {
                    entries = await engine.ListEntriesAsync(item.Path, null, ct);
                }
                catch (Exception ex) when (ArchiveService.IsPasswordRelatedError(ex, item.Path))
                {
                    // ── A 类：自动尝试密码库，命中后用密码重新列出条目 ──
                    entries = await TryAutoUnlockAsync(item, engine, relist: true, ct);
                    if (entries == null)
                    {
                        int candidates = 0;
                        try
                        {
                            candidates = PasswordManager.Instance.FindMatchingPasswords(item.Path).Count;
                        }
                        catch (Exception cntEx)
                        {
                            App.DebugLog($"count candidates failed: {cntEx.Message}");
                        }
                        item.ErrorMessage = LocalizationManager.T("Extract_Preview_NoPasswordMatch", candidates);
                        item.Status = SourceArchiveStatus.NeedsPassword;
                        return;
                    }
                    lockedType = true;
                }

                _entriesCache[item.Path] = entries.ToList();
                item.IsEncrypted = lockedType || entries.Any(e => e.IsEncrypted);

                // ── B 类：能列出但内容加密 → 自动尝试密码库；未命中保持可浏览（🔑）──
                if (item.IsEncrypted && !lockedType && item.MatchedPassword == null)
                {
                    await TryAutoUnlockAsync(item, engine, relist: false, ct);
                }

                item.ErrorMessage = null;
                item.Status = SourceArchiveStatus.Ok;

                // 首包条目兼作过滤统计与提取的数据源（对齐 WPF「过滤仅对 i==0 生效」）
                if (_firstArchiveEntries == null &&
                    string.Equals(item.Path, SourceItems[0].Path, StringComparison.OrdinalIgnoreCase))
                {
                    _firstArchiveEntries = _entriesCache[item.Path];
                }
            }
            finally
            {
                gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // 窗口已关闭：回退初始态即可（无 UI 可刷新）
            if (item.Status == SourceArchiveStatus.Validating)
                item.Status = SourceArchiveStatus.Pending;
            return;
        }
        catch (Exception ex)
        {
            item.ErrorMessage = ex.Message;
            item.Status = SourceArchiveStatus.Failed;
        }

        // 任一包校验完成即增量刷新合并树 + 刷新解锁按钮可用性
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UpdateCanUnlockSelected();
            RebuildMergedPreview();
        });
    }

    /// <summary>
    /// 自动尝试密码库。relist=true（A 类）用候选密码重新列条目验证；
    /// relist=false（B 类）依赖 TryMatchPassword 内部的 QuickVerify。
    /// 命中返回条目并记录密码/描述；未命中返回 null（不改 Status，由调用方决定终态）。
    /// </summary>
    private async Task<IReadOnlyList<ArchiveItem>?> TryAutoUnlockAsync(
        SourceArchiveItem item, IArchiveEngine engine, bool relist, CancellationToken ct)
    {
        var match = _passwordService.TryMatchPasswordEx(item.Path, engine);
        if (match == null) return null;

        // 文件损坏：直接报错，不再尝试其他密码
        if (match.Value.VerifyInfo.Result == PasswordVerificationResult.CorruptedOrInvalid)
        {
            item.ErrorMessage = LocalizationManager.T("Status_ArchiveCorrupted");
            item.Status = SourceArchiveStatus.Failed;
            UpdateCanUnlockSelected();
            return null;
        }

        IReadOnlyList<ArchiveItem> entries;
        if (relist)
        {
            entries = (await engine.ListEntriesAsync(item.Path, match.Value.Password, ct)).ToList();
        }
        else
        {
            entries = _entriesCache.TryGetValue(item.Path, out var cached)
                ? cached
                : (await engine.ListEntriesAsync(item.Path, match.Value.Password, ct)).ToList();
        }

        item.SetMatched(match.Value.Password, match.Value.Description);
        item.ErrorMessage = null;
        item.IsEncrypted = true;
        item.Status = SourceArchiveStatus.Ok;
        _matchedPasswords[item.Path] = match.Value.Password;
        return entries;
    }

    /// <summary>
    /// 手动输入密码解锁（自动匹配失败后的补救）：验证循环直到正确或取消。
    /// 成功后缓存条目、记录密码、按用户选择入库，并刷新合并树。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUnlockSelected))]
    private async Task UnlockSelected()
    {
        if (SelectedSourceItem != null)
            await UnlockManuallyAsync(SelectedSourceItem);
    }

    public async Task UnlockManuallyAsync(SourceArchiveItem item)
    {
        if (ShowUnlockDialog == null) return;

        var engine = ArchiveEngineFactory.GetEngineByExtension(item.Path);
        if (engine == null) return;

        while (true)
        {
            var resp = await ShowUnlockDialog(item.Path);
            if (resp?.Password == null) return;   // 用户取消：维持现状，解压阶段走现有弹窗兜底

            bool ok;
            if (item.Status == SourceArchiveStatus.NeedsPassword)
            {
                // A 类：重列条目即验证
                try
                {
                    var entries = (await engine.ListEntriesAsync(item.Path, resp.Password)).ToList();
                    _entriesCache[item.Path] = entries;
                    item.IsEncrypted = true;
                    if (_firstArchiveEntries == null &&
                        string.Equals(item.Path, SourceItems[0].Path, StringComparison.OrdinalIgnoreCase))
                    {
                        _firstArchiveEntries = entries;
                    }
                    ok = true;
                }
                catch (Exception ex)
                {
                    App.DebugLog($"manual unlock relist failed: {ex.Message}");
                    ok = false;
                }
            }
            else
            {
                // B 类：快速验证首个加密条目
                var verifyInfo = _passwordService.QuickVerifyPasswordEx(item.Path, resp.Password, engine);
                if (verifyInfo.Result == PasswordVerificationResult.CorruptedOrInvalid)
                {
                    await AppMessageBox.Show(
                        LocalizationManager.T("Status_ArchiveCorrupted"),
                        LocalizationManager.T("App_ErrorTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    item.ErrorMessage = LocalizationManager.T("Status_ArchiveCorrupted");
                    item.Status = SourceArchiveStatus.Failed;
                    UpdateCanUnlockSelected();
                    return;
                }
                ok = verifyInfo.Result == PasswordVerificationResult.Success;
            }

            if (!ok)
            {
                await AppMessageBox.Show(
                    LocalizationManager.T("Extract_Unlock_WrongPassword"),
                    LocalizationManager.T("App_ErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }

            item.SetMatched(resp.Password, resp.Description);
            _subTreeCache.Remove(item.Path);   // 手动解锁后该包子树定点重建
            item.ErrorMessage = null;
            item.Status = SourceArchiveStatus.Ok;
            _matchedPasswords[item.Path] = resp.Password;

            if (resp.SavePermanently)
                _passwordService.TrySavePassword(resp.Password, item.Path, resp.Patterns, resp.Description);

            UpdateCanUnlockSelected();
            RebuildMergedPreview();
            return;
        }
    }

    /// <summary>
    /// 重建合并预览树（增量骨架式）：
    /// 骨架立即上屏——所有压缩包从一开始就以占位节点（未开始/读取中）全部可见，
    /// 随校验与子树构建逐个「原位转正」，已显示部分零闪烁零重排。
    /// 已建子树按包缓存复用，重建只处理增量；目标路径/过滤变化时整体失效。
    /// 构建期间的新请求不丢弃当前工作，仅标记 dirty，完成后补一轮（请求合并）。
    /// 单包场景退化为原有单树（目标目录根 + 条目结构，无多余层级）。
    /// </summary>
    public void RebuildMergedPreview()
    {
        if (_isRebuilding)
        {
            _rebuildPending = true;
            return;
        }
        _ = RunRebuildLoopAsync();
    }

    private async Task RunRebuildLoopAsync()
    {
        _isRebuilding = true;
        try
        {
            do
            {
                _rebuildPending = false;
                await RebuildOnceAsync();
            } while (_rebuildPending);   // 补建期间又有新结论 → 再来一轮（每轮只处理增量）
        }
        finally
        {
            _isRebuilding = false;
        }
    }

    private async Task RebuildOnceAsync()
    {
        var sources = SourceItems.ToList();

        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            PreviewRoot = null;
            IsPreviewBuilding = false;
            IsBuildPending = false;
            IsListingPending = false;
            return;
        }

        var destDir = DestinationPath;
        var filter = FilterProvider?.Invoke();

        // ── 单包退化：保持原语义（目标目录根 + 条目结构，无中间归档层、无骨架阶段）──
        if (sources.Count == 1)
        {
            if (!_entriesCache.TryGetValue(sources[0].Path, out var onlyEntries))
            {
                // MainWindow 路径注入前的短暂瞬间 / 校验中
                IsListingPending = true;
                PreviewRoot = null;
                IsPreviewBuilding = false;
                IsBuildPending = false;
                return;
            }
            await BuildAndAssignSingleAsync(onlyEntries, destDir, filter);
            return;
        }

        // ── 多包：Phase 1 骨架立即上屏（缓存子树挂载，其余占位），用户一眼看到全量与差距 ──
        var root = AssembleSkeleton(sources, destDir);
        PreviewRoot = root;
        IsListingPending = false;

        var toBuild = sources
            .Where(i => !_subTreeCache.ContainsKey(i.Path) && _entriesCache.TryGetValue(i.Path, out _))
            .Select(i => (Item: i, Entries: _entriesCache[i.Path]))
            .ToList();

        if (toBuild.Count > 0)
        {
            // ── Phase 2 后台构建缺失子树 ──
            // 不显示整树加载覆层：骨架上的占位节点本身就是进度表达
            // （读取中 → 原位转正），整树覆盖层会遮蔽这一过程。
            // IsBuildPending 仅保留门禁作用：「开始解压」在过滤结果就绪前禁用。
            IsBuildPending = true;

            try
            {
                var builtNodes = await Task.Run(() =>
                {
                    var built = new List<PreviewTreeNode>();
                    for (int i = 0; i < toBuild.Count; i++)
                    {
                        var sub = ResultPreviewService.BuildExtractPreview(
                            toBuild[i].Entries, destDir,
                            checkExists: true, filter: filter);
                        DecorateSubTree(sub, toBuild[i].Item);
                        built.Add(sub);
                    }
                    return built;
                });

                foreach (var node in builtNodes)
                    _subTreeCache[node.FullPath] = node;
            }
            catch (Exception ex)
            {
                App.DebugLog($"RebuildMergedPreview build failed: {ex.Message}");
            }
            finally
            {
                IsBuildPending = false;
            }

            // ── Phase 3 最终装配（新建根对象触发视图刷新；占位原位转正为子树）──
            var final = AssembleSkeleton(sources, destDir);
            ResultPreviewService.RecalculateDescendantStats(final);
            PreviewRoot = final;
        }
    }

    /// <summary>
    /// 组装合并骨架：目标目录为根；已缓存子树的包直接挂载（装饰同步到最新状态），
    /// 其余包为状态占位节点（未开始/读取中/需密码/损坏）。纯同步、低成本。
    /// </summary>
    private PreviewTreeNode AssembleSkeleton(List<SourceArchiveItem> sources, string destDir)
    {
        var root = new PreviewTreeNode
        {
            Name = Path.GetFileName(destDir.TrimEnd(Path.DirectorySeparatorChar)),
            FullPath = destDir,
            DisplayLabel = destDir,
            IsExpanded = true
        };

        foreach (var item in sources)
        {
            if (_subTreeCache.TryGetValue(item.Path, out var cached))
            {
                DecorateSubTree(cached, item);   // 状态可能因解锁等变化，装饰每次装配时刷新
                root.Children.Add(cached);
            }
            else
            {
                root.Children.Add(CreatePlaceholderNode(item));
            }
        }
        return root;
    }

    /// <summary>把行模型当前状态（图标键/颜色键/加密副文本）应用到压缩包子树节点上。
    /// 注意 FullPath 必须写回压缩包自身路径——它同时是 <see cref="_subTreeCache"/> 的键，
    /// 构建服务默认填的是共享目标目录，不覆盖会导致所有包互相同名覆盖、永远无法命中缓存。</summary>
    private static void DecorateSubTree(PreviewTreeNode sub, SourceArchiveItem item)
    {
        var displayName = Path.GetFileName(item.Path);
        sub.Name = displayName;
        sub.DisplayLabel = displayName;
        sub.FullPath = item.Path;
        sub.IsArchiveNode = true;
        sub.IconKeyOverride = item.StatusIconKey;
        sub.StatusForegroundKey = item.StatusForegroundKey;

        if (item.IsEncrypted)
        {
            sub.SizeDisplay = item.MatchedPassword == null
                ? LocalizationManager.T("Extract_Preview_EncryptedNoMatch")
                : string.IsNullOrEmpty(item.MatchedDescription)
                    ? LocalizationManager.T("Extract_Preview_Unlocked")
                    : LocalizationManager.T("Extract_Preview_UnlockedDesc", item.MatchedDescription);
        }
    }

    /// <summary>
    /// 创建状态占位节点：图标/颜色与列表徽标一致，
    /// 副文本为 未开始 / 读取中 / 需密码原因 / 损坏原因。
    /// </summary>
    private PreviewTreeNode CreatePlaceholderNode(SourceArchiveItem item)
    {
        var statusText = item.Status switch
        {
            SourceArchiveStatus.Pending => LocalizationManager.T("Extract_Preview_NotStarted"),
            SourceArchiveStatus.Validating => LocalizationManager.T("Preview_Result_Reading"),
            SourceArchiveStatus.NeedsPassword =>
                string.IsNullOrEmpty(item.ErrorMessage)
                    ? LocalizationManager.T("Extract_Preview_NeedsPassword")
                    : item.ErrorMessage,
            SourceArchiveStatus.Failed =>
                LocalizationManager.T("Extract_Preview_LoadFailed"),
            _ => LocalizationManager.T("Preview_Result_Reading"),
        };

        return new PreviewTreeNode
        {
            Name = Path.GetFileName(item.Path),
            DisplayLabel = Path.GetFileName(item.Path),
            SizeDisplay = statusText,
            FullPath = item.Path,
            IsArchiveNode = true,
            IconKeyOverride = item.StatusIconKey,
            StatusForegroundKey = item.StatusForegroundKey,
        };
    }

    /// <summary>单包路径：直接构建条目结构为根（无归档层），沿用 250ms 防闪烁加载态。
    /// 冲突检测分两阶段：depth 2 快速上屏 → 全量后台补全。</summary>
    private async Task BuildAndAssignSingleAsync(
        IReadOnlyList<ArchiveItem> entries, string destDir, FileFilterCriteria? filter)
    {
        // 取消前一轮冲突扫描（目标路径变化时快速终止无用 I/O）
        _conflictCts?.Cancel();
        _conflictCts?.Dispose();
        _conflictCts = new CancellationTokenSource();
        var ct = _conflictCts.Token;

        IsListingPending = false;
        IsBuildPending = true;
        PreviewBuildProgress = -1;
        var progress = new Progress<double>(v => PreviewBuildProgress = v);

        try
        {
            // Phase 1：构建树（不含冲突检测）+ 250ms 防闪烁
            var buildTask = Task.Run(() => ResultPreviewService.BuildExtractPreview(
                entries, destDir, checkExists: false, filter: filter, progress: progress));

            var delayTask = Task.Delay(250);
            if (await Task.WhenAny(buildTask, delayTask) == delayTask)
                IsPreviewBuilding = true;

            var root = await buildTask;

            // Phase 2a：深度 2 快速冲突标记（直接子项 + 孙辈，即时上屏）
            ct.ThrowIfCancellationRequested();
            ResultPreviewService.ApplyConflictMarkers(root, destDir, maxDepth: 2, ct);
            PreviewRoot = root;

            // Phase 2b：全量冲突标记（后台线程）
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                ResultPreviewService.ApplyConflictMarkers(root, destDir, maxDepth: int.MaxValue, ct);
            }, ct);

            // 全量完成：再次上屏 + 触发 View 刷新冲突计数
            ct.ThrowIfCancellationRequested();
            PreviewRoot = root;
            PreviewTreeInvalidated?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // 被新请求取消：静默丢弃，由新一轮接管
        }
        catch (Exception ex)
        {
            App.DebugLog($"single preview build failed: {ex.Message}");
        }
        finally
        {
            IsPreviewBuilding = false;
            IsBuildPending = false;
        }
    }

    /// <summary>清空全部子树缓存（目标路径 / 过滤条件变化时调用，冲突高亮与灰显标记需全量重算）。</summary>
    public void InvalidatePreviewCache() => _subTreeCache.Clear();

    /// <summary>单个包的子树失效（如手动解锁后条目集/注解变化，仅重刷该包）。</summary>
    public void InvalidateArchivePreview(string archivePath) => _subTreeCache.Remove(archivePath);


    /// <summary>
    /// 基于首包条目计算过滤后的提取 key 列表（提取语义始终绑定首包，与预览展示解耦）。
    /// <para>
    /// 只服务确认事务（<see cref="TryConfirmAsync"/>）内部的过滤快照，故为 private：
    /// 旧的「窗口 GetFilteredEntryKeys 回读」通道已退休，过滤集合只能随载荷交付。
    /// </para>
    /// </summary>
    private List<string>? ComputeFilteredEntryKeys(FileFilterCriteria? filter)
    {
        var entries = _firstArchiveEntries;
        if (filter == null || entries == null || !filter.IsActive) return null;
        return entries
            .Where(e => FileFilterMatcher.IsMatch(filter, e))
            .Select(e => e.FullPath)
            .ToList();
    }

    partial void OnDestinationPathChanged(string value)
    {
        // 目标路径变化 → 刷新"开始解压"按钮状态（CanExecuteExtract 依赖非空目标）
        ExtractCommand.NotifyCanExecuteChanged();
        // The caller should call BuildExtractPreview again
    }

    [RelayCommand]
    private async Task BrowseDestination()
    {
        if (BrowseFolder == null) return;
        var path = await BrowseFolder();
        if (!string.IsNullOrEmpty(path))
        {
            DestinationPath = path;
        }
    }

    private bool CanExecuteExtract()
    {
        // 预览树构建期间禁用"开始解压"：确认事务要用的过滤集合（预览结果）未就绪时不允许执行
        if (IsBuildPending) return false;
        return !string.IsNullOrWhiteSpace(DestinationPath);
    }

    /// <summary>
    /// 「开始解压」：只把确认事务的结局翻译成窗口动作——校验、等就绪、过滤快照都在
    /// <see cref="TryConfirmAsync"/> 里一次做完，真正的解压由宿主按载荷执行。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteExtract))]
    private async Task Extract()
    {
        var outcome = await TryConfirmAsync();

        switch (outcome)
        {
            case Rejected<ExtractSettings> rejected:
                // 拒绝原因必须让用户看见（目标目录无效 / 预览未就绪）：窗口保持打开。
                if (ShowMessage != null)
                    await ShowMessage(rejected.Reason, LocalizationManager.T("Progress_Title_Extract"));
                return;

            case Cancelled<ExtractSettings>:
                // 等待期间被取消：窗口已由 Cancel 关闭，这里不再重复关窗、不弹任何提示。
                return;

            default:
                // Confirmed：把载荷交给窗口，由窗口关闭自己并把结局回传给宿主。
                ConfirmCompleted?.Invoke(outcome);
                return;
        }
    }

    [RelayCommand]
    private Task Cancel()
    {
        // 确认事务可能在等预览就绪：先标记取消，避免恢复执行后把载荷当成确认交出去。
        _extractCancelled = true;

        // 取消是静默动作：关窗由窗口订阅方处理，不弹任何提示。
        ConfirmCompleted?.Invoke(new Cancelled<ExtractSettings>());
        return Task.CompletedTask;
    }

    /// <summary>用户是否在确认事务等待期间点了「取消」。</summary>
    private bool _extractCancelled;

    // ════════════════════════════════════════════
    //  确认事务（T8：意图-执行接缝的「结果即载荷」法则）
    // ════════════════════════════════════════════

    /// <summary>等预览/条目就绪的最长时间（毫秒）；超时即视为「过滤结果未就绪」，绝不带着未算完的结果开跑。</summary>
    private const int PreviewReadyTimeoutMs = 10_000;

    /// <summary>等就绪期间的轮询间隔（毫秒）。</summary>
    private const int PreviewReadyPollMs = 50;

    /// <summary>调用方预置的「已知密码」（会话缓存 / 命令行）；随确认快照进入载荷，不做窗口回读。</summary>
    private string? _knownPassword;

    /// <summary>
    /// 预置「已知密码」：由入口在打开对话框前设置（会话内记住的密码），随确认快照进入
    /// <see cref="ExtractSettings.Password"/>。
    /// <para>
    /// 为什么用方法而不是公开属性：密码属于「入口交给对话框的输入」，不是对话框对外暴露的可变状态；
    /// 写成方法既避免 <c>ExtractSettingsDialogCopyTests</c> 把它当成需要归类的外部回读通道，
    /// 也让「谁在什么时候写入密码」在调用点上可搜。
    /// </para>
    /// </summary>
    /// <param name="password">入口已确认的密码；无则传 <see langword="null"/>。</param>
    public void SetKnownPassword(string? password) => _knownPassword = password;

    /// <summary>
    /// 本次运行的密码策略（载荷字段 <see cref="ExtractSettings.PasswordPolicy"/> 的入口侧输入）。
    /// 默认「只使用提供的密码」（主窗口路径）；CLI 入口在打开对话框前改成「回退密码库」，
    /// 因为命令行历史行为就是逐包打密码库，而窗口本身无权决定宿主的取密策略。
    /// </summary>
    private ExtractPasswordPolicy _passwordPolicy = ExtractPasswordPolicy.UseProvidedOnly;

    /// <summary>
    /// 设置本次运行的密码策略：由入口在打开对话框前注入，随确认快照进入载荷。
    /// </summary>
    /// <param name="policy">入口选定的取密策略。</param>
    public void SetPasswordPolicy(ExtractPasswordPolicy policy) => _passwordPolicy = policy;

    /// <summary>
    /// 确认事务：把「用户点下开始解压」这一刻的决定一次性冻结成 <see cref="ExtractSettings"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 顺序固定为「校验 → 等预览/过滤结果就绪 → 快照」。以前确认流程是「关窗 + 调用方补读」，
    /// 漏读一项就静默失效（历史事故：CLI 多包的过滤集合恒 null，过滤条件静默不生效）；
    /// 现在载荷在事务内一次产出，调用方拿到的就是全部，缺项在产出点就炸，而不是在执行段变成
    /// 「这次解压好像没听我的话」。
    /// </para>
    /// <para>
    /// 校验判定条件与现状完全一致（目标目录非空、压缩包集合非空、预览构建期间不得确认），
    /// 唯一变化是把原本靠按钮 <c>CanExecute</c> 挡住的「构建中」显式化成带文案的拒绝，
    /// 避免重复实现同一谓词（计划 §4.1 法则四）。
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">取消等待预览就绪的令牌；取消时返回 <see cref="Cancelled{T}"/>。</param>
    /// <returns>
    /// <see cref="Confirmed{T}"/>（载荷已完整）/ <see cref="Rejected{T}"/>（带本地化原因，调用方必须留窗展示）/
    /// <see cref="Cancelled{T}"/>（等待期间被取消）。
    /// </returns>
    public async Task<ConfirmOutcome<ExtractSettings>> TryConfirmAsync(CancellationToken cancellationToken = default)
    {
        // ⓪ 每次事务独立：上一次的取消标记不得污染本次确认
        _extractCancelled = false;

        // ① 目标目录：与 CanExecuteExtract 同一谓词，空/空白一律不许继续
        if (string.IsNullOrWhiteSpace(DestinationPath))
            return new Rejected<ExtractSettings>(LocalizationManager.T("Error_Extract_DestinationInvalid"));

        // ② 压缩包集合：载荷要求非空（对话框入口不会走到这里，守住直接构造 VM 的调用方）
        if (ArchivePaths.Count == 0 || SelectedPaths.Count == 0)
            return new Rejected<ExtractSettings>(LocalizationManager.T("Error_Extract_NoEntries"));

        // ③ 等预览树就绪（原实现由按钮 CanExecute 拦住构建期，判定条件不变，只是把结论显式化）
        if (IsBuildPending || IsListingPending)
        {
            var ready = await WaitForPreviewReadyAsync(cancellationToken).ConfigureAwait(true);
            if (!ready)
            {
                if (cancellationToken.IsCancellationRequested)
                    return new Cancelled<ExtractSettings>();

                // 只有启用过滤时才真正依赖预览结果；未启用过滤时构建中不影响执行，按原语义继续。
                if (IsFilterActive())
                    return new Rejected<ExtractSettings>(LocalizationManager.T("Error_Extract_FilterNotReady"));
            }
        }

        // ④ 过滤集合：启用过滤则必须在事务内算出来（不再由调用方补读）
        var filter = FilterProvider?.Invoke();
        List<string>? filteredEntryKeys = null;
        if (filter is { IsActive: true })
        {
            if (_firstArchiveEntries is null)
                return new Rejected<ExtractSettings>(LocalizationManager.T("Error_Extract_FilterNotReady"));

            filteredEntryKeys = ComputeFilteredEntryKeys(filter);
        }

        // ⑤ 一次快照：此后窗口销毁、VM 通道复位都不影响载荷
        var appSettings = AppSettings.Load();
        var snapshot = new ExtractSettings
        {
            ArchivePaths = SelectedPaths.ToList(),
            DestinationPath = DestinationPath,
            ConflictAction = ConflictAction,
            FilteredEntryKeys = filteredEntryKeys,
            // 设置对话框只解整包或过滤集合；勾选条目由预览树入口另行构造载荷（T11）。
            SelectedEntries = null,
            CurrentFolder = null,
            Password = _knownPassword,
            MatchedPasswords = _matchedPasswords.Count > 0
                ? new Dictionary<string, string>(_matchedPasswords, StringComparer.OrdinalIgnoreCase)
                : null,
            // 取密策略由入口注入：主窗口只复用对话框内已校验的密码，CLI 入口才回退密码库。
            PasswordPolicy = _passwordPolicy,
            OpenFolderAfterExtract = OpenFolderAfterExtract,
            DeleteArchiveAfterExtract = DeleteArchiveAfterExtract,
            // 路径契约的执行输入：路径归属由 ExtractPathResolver 决定，这里只记录当前设置值。
            PreserveFullPath = appSettings.ExtractPreserveFullPath,
            // 本次解压的并行度快照（对话框内改动本次生效、不回写全局设置）。
            ParallelOverride = BuildParallelSettings(),
        };

        // ⑥ 等待期间用户点了「取消」：不得把载荷当成确认交出去（取消是静默结局，不弹任何提示）。
        if (_extractCancelled)
            return new Cancelled<ExtractSettings>();

        return new Confirmed<ExtractSettings>(snapshot);
    }

    /// <summary>当前是否真的启用了过滤（过滤编辑器关闭或条件为空都算未启用）。</summary>
    private bool IsFilterActive() => FilterProvider?.Invoke() is { IsActive: true };

    /// <summary>轮询等待预览树 / 条目列表跑完；返回 <see langword="false"/> 表示超时或被取消。</summary>
    private async Task<bool> WaitForPreviewReadyAsync(CancellationToken cancellationToken)
    {
        if (!IsBuildPending && !IsListingPending)
            return true;

        var deadline = Environment.TickCount64 + PreviewReadyTimeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            try
            {
                await Task.Delay(PreviewReadyPollMs, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            if (!IsBuildPending && !IsListingPending)
                return true;
        }

        return false;
    }
}
