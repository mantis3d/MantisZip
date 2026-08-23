using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// 解压设置对话框的 ViewModel。
/// 用户选择目标路径、冲突策略和是否打开文件夹，通过 CloseAction 回调返回结果。
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

    /// <summary>由 View 设置的关闭回调。参数 true=确认解压，false=取消。</summary>
    public Func<bool, Task>? CloseAction { get; set; }

    [ObservableProperty]
    private string _destinationPath = string.Empty;

    [ObservableProperty]
    private string _conflictAction = "ask";

    [ObservableProperty]
    private bool _openFolderAfterExtract;

    /// <summary>过滤后需实际解压的条目 key 列表（由 View 从对话框回传；null = 未启用过滤，全量解压）。</summary>
    public List<string>? FilteredEntryKeys { get; set; }

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
    /// 快构建也会短暂置位，保证过滤结果（FilteredEntryKeys 读取的预览）就绪前无法点击解压。
    /// </summary>
    [ObservableProperty]
    private bool _isBuildPending;

    partial void OnIsBuildPendingChanged(bool value) => ExtractCommand.NotifyCanExecuteChanged();

    /// <summary>预览树构建进度（0–100，-1 表示不确定进度/不定进度条）。</summary>
    [ObservableProperty]
    private double _previewBuildProgress = -1;

    /// <summary>预览树构建版本号，用于丢弃过期异步结果。</summary>
    private int _previewBuildVersion;

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
    /// 第一个压缩包的条目。过滤统计与实际提取语义保持绑定首包
    /// （对齐 WPF HandleExtractBatchCore「过滤仅对 i==0 生效」的设计），与预览展示解耦。
    /// </summary>
    private IReadOnlyList<ArchiveItem>? _firstArchiveEntries;

    /// <summary>首包条目只读访问（窗口的过滤统计 / FilteredEntryKeys 计算用）。</summary>
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
            "Extract_Start",
            "Extract_Cancel",
            "Extract_TabFilter",
            "Extract_Source_MultiFilterHint"
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

    /// <summary>
    /// 构建解压预览树。由窗口在加载完成后调用。
    /// 原始树构建在后台线程执行，快速操作时通过版本号丢弃过期结果。
    /// </summary>
    /// <param name="entries">压缩包内的条目列表。</param>
    /// <param name="filter">文件过滤条件，传递到服务层标记 IsFilteredOut。</param>
    /// <param name="checkExists">是否逐文件检查目标位置是否存在。</param>
    public void BuildExtractPreview(IEnumerable<ArchiveItem> entries, FileFilterCriteria? filter = null, bool checkExists = false)
        => _ = BuildExtractPreviewCoreAsync(entries, filter, checkExists);

    private async Task BuildExtractPreviewCoreAsync(IEnumerable<ArchiveItem> entries, FileFilterCriteria? filter, bool checkExists)
    {
        var version = ++_previewBuildVersion;
        IsBuildPending = true; // 快慢构建都置位，过滤结果就绪前禁用"开始解压"
        PreviewBuildProgress = -1;

        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            PreviewRoot = null;
            IsPreviewBuilding = false;
            IsBuildPending = false;
            return;
        }

        // 快照输入（后台构建期间 DestinationPath 等可能被用户修改）
        var snapshot = entries.ToList();
        var destDir = DestinationPath;

        // Progress<T> 捕获构造时的 SynchronizationContext（UI 线程），自动封送回 UI；
        // 版本号守卫丢弃过期构建的进度回调
        var progress = new Progress<double>(v =>
        {
            if (version == _previewBuildVersion)
                PreviewBuildProgress = v;
        });

        try
        {
            var rootName = Path.GetFileName(destDir);

            var buildTask = Task.Run(() => ResultPreviewService.BuildExtractPreview(
                snapshot,
                destDir,
                rootName: rootName,
                checkExists: checkExists,
                filter: filter,
                progress: progress));

            // 快速构建（<250ms）不显示加载态，避免切换目标路径时预览树闪烁；
            // 慢构建显示确定性进度条（服务按条目数上报 0–100）
            var delayTask = Task.Delay(250);
            if (await Task.WhenAny(buildTask, delayTask) == delayTask)
            {
                if (version != _previewBuildVersion) return; // 已有更新的构建
                IsPreviewBuilding = true;
            }

            var root = await buildTask;
            if (version != _previewBuildVersion) return; // 过期结果丢弃

            PreviewRoot = root;
        }
        catch (Exception ex)
        {
            App.DebugLog($"BuildExtractPreview failed: {ex.Message}");
        }
        finally
        {
            if (version == _previewBuildVersion)
            {
                IsPreviewBuilding = false;
                IsBuildPending = false;
            }
        }
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

                var entries = await engine.ListEntriesAsync(item.Path, null, ct);
                _entriesCache[item.Path] = entries.ToList();
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
            // 密码类异常 ≠ 损坏：加密文件名的 7z 无密码时无法列出条目
            item.Status = ArchiveService.IsPasswordRelatedError(ex)
                ? SourceArchiveStatus.NeedsPassword
                : SourceArchiveStatus.Failed;
        }

        // 任一包校验完成即增量刷新合并树（健康包子树就位 / 占位节点转正）
        global::Avalonia.Threading.Dispatcher.UIThread.Post(RebuildMergedPreview);
    }

    /// <summary>
    /// 重建合并预览树：所有压缩包并列展示——健康包为归档节点（下挂条目子树），
    /// 损坏/需密码/未就绪的包为占位节点。校验逐个完成时增量刷新。
    /// 单包场景退化为原有单树（目标目录根 + 条目结构，无多余层级）。
    /// </summary>
    public void RebuildMergedPreview() => _ = RebuildMergedPreviewCoreAsync();

    private async Task RebuildMergedPreviewCoreAsync()
    {
        var version = ++_previewBuildVersion;
        var sources = SourceItems.ToList();

        // 全部尚未得出结论（仍在排队/校验中）→ ⏳ 读取中占位。
        // 注意不能用「缓存为空」判断：损坏/需密码的包永远不会入缓存，
        // 否则单加密包场景会永远卡在读取中。
        if (sources.All(i => i.Status is SourceArchiveStatus.Pending or SourceArchiveStatus.Validating))
        {
            IsListingPending = true;
            PreviewErrorTitle = "";
            PreviewRoot = null;
            IsPreviewBuilding = false;
            IsBuildPending = false;
            return;
        }
        IsListingPending = false;
        PreviewErrorTitle = "";

        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            PreviewRoot = null;
            IsPreviewBuilding = false;
            IsBuildPending = false;
            return;
        }

        IsBuildPending = true;
        PreviewBuildProgress = -1;

        var destDir = DestinationPath;
        var filter = FilterProvider?.Invoke();
        // 缓存快照：后台构建期间新完成的校验由下一次增量刷新呈现
        var cache = new Dictionary<string, IReadOnlyList<ArchiveItem>>(
            _entriesCache, StringComparer.OrdinalIgnoreCase);

        var progress = new Progress<double>(v =>
        {
            if (version == _previewBuildVersion)
                PreviewBuildProgress = v;
        });

        try
        {
            // 单包：保持原语义（目标目录根 + 条目结构，无中间归档层）
            bool single = sources.Count == 1 && cache.ContainsKey(sources[0].Path);

            var buildTask = Task.Run(() =>
            {
                if (single)
                {
                    return ResultPreviewService.BuildExtractPreview(
                        cache[sources[0].Path], destDir,
                        checkExists: true, filter: filter, progress: progress);
                }

                // 合并根 = 目标目录本身；每个压缩包为其直接子节点
                var root = new PreviewTreeNode
                {
                    Name = Path.GetFileName(destDir.TrimEnd(Path.DirectorySeparatorChar)),
                    FullPath = destDir,
                    DisplayLabel = destDir,
                    IsExpanded = true
                };

                foreach (var item in sources)
                {
                    var displayName = Path.GetFileName(item.Path);

                    if (cache.TryGetValue(item.Path, out var entries))
                    {
                        // 健康包：独立子树（冲突高亮/过滤灰显基于同一共享目标目录），
                        // 组装后改挂到合并根下并标记为归档节点（归档图标 + 粗体标签）
                        var sub = ResultPreviewService.BuildExtractPreview(
                            entries, destDir,
                            checkExists: true, filter: filter, progress: progress);
                        sub.Name = displayName;
                        sub.DisplayLabel = displayName;
                        sub.IsArchiveNode = true;
                        root.Children.Add(sub);
                    }
                    else
                    {
                        // 占位节点：与正常压缩包相同的归档图标 + 状态色文字（损坏红/加密蓝），副文本为原因文案
                        var statusText = item.Status switch
                        {
                            SourceArchiveStatus.NeedsPassword =>
                                LocalizationManager.T("Extract_Preview_NeedsPassword"),
                            SourceArchiveStatus.Failed =>
                                LocalizationManager.T("Extract_Preview_LoadFailed"),
                            _ => LocalizationManager.T("Preview_Result_Reading"),
                        };
                        var statusKey = item.Status switch
                        {
                            SourceArchiveStatus.NeedsPassword => "Blue",
                            SourceArchiveStatus.Failed => "ConflictRed",
                            _ => (string?)null,
                        };
                        root.Children.Add(new PreviewTreeNode
                        {
                            Name = displayName,
                            DisplayLabel = displayName,
                            SizeDisplay = statusText,
                            FullPath = item.Path,
                            IsArchiveNode = true,
                            StatusForegroundKey = statusKey,
                        });
                    }
                }

                // 重算合并根统计供摘要栏显示全树文件数 / 总大小
                ResultPreviewService.RecalculateDescendantStats(root);
                ((IProgress<double>)progress).Report(100);
                return root;
            });

            // 快速构建（<250ms）不显示加载态；慢构建显示进度条（沿用既有防闪烁策略）
            var delayTask = Task.Delay(250);
            if (await Task.WhenAny(buildTask, delayTask) == delayTask)
            {
                if (version != _previewBuildVersion) return;
                IsPreviewBuilding = true;
            }

            var built = await buildTask;
            if (version != _previewBuildVersion) return;

            PreviewRoot = built;
        }
        catch (Exception ex)
        {
            App.DebugLog($"RebuildMergedPreview failed: {ex.Message}");
        }
        finally
        {
            if (version == _previewBuildVersion)
            {
                IsPreviewBuilding = false;
                IsBuildPending = false;
            }
        }
    }

    /// <summary>
    /// 基于首包条目计算过滤后的提取 key 列表（提取语义始终绑定首包，与预览展示解耦）。
    /// </summary>
    public List<string>? ComputeFilteredEntryKeys(FileFilterCriteria? filter)
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
        // 预览树构建期间禁用"开始解压"：过滤结果（FilteredEntryKeys 读取的预览树）未就绪时不允许执行
        if (IsBuildPending) return false;
        return !string.IsNullOrWhiteSpace(DestinationPath);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteExtract))]
    private async Task Extract()
    {
        if (string.IsNullOrWhiteSpace(DestinationPath)) return;

        // 修复潜伏 bug：CLI 多包路径（App.axaml.cs）读取的 FilteredEntryKeys 此前从未被
        // 赋值（恒 null），批量解压时过滤条件静默失效。提取语义始终基于首包条目。
        FilteredEntryKeys = ComputeFilteredEntryKeys(FilterProvider?.Invoke());

        if (CloseAction != null)
            await CloseAction(true);
    }

    [RelayCommand]
    private async Task Cancel()
    {
        if (CloseAction != null)
            await CloseAction(false);
    }
}
