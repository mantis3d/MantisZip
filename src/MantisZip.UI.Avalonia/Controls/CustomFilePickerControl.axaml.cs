using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Controls;

/// <summary>
/// 可内嵌的文件/目录选择器控件：地址栏 + <see cref="QuickPathControl"/> 速选 + 文件浏览 +
/// 右栏面板（PickItems 累积选择 / ExtractFolder 解压预览）+ 文件名与类型行 + 通用参数区。
/// </summary>
/// <remarks>
/// <para>
/// 本控件只负责「选择器」本身，是 <see cref="CustomFilePickerDialog"/> 的内嵌主体（模态窗口
/// 只剩标题栏、底部「确定 / 取消」与规则 15 唯一豁免的「系统浏览」原生逃生通道）。
/// </para>
/// <para>
/// 使用顺序：先设 <see cref="Mode"/> / <see cref="IsSimpleMode"/>，再调 <see cref="Initialize"/>；
/// 之后由宿主决定何时「确定」（<see cref="TryConfirm"/> 或响应 <see cref="ConfirmRequested"/>）。
/// </para>
/// </remarks>
public partial class CustomFilePickerControl : UserControl
{
    // ── 宿主配置（必须在 Initialize 之前赋值） ────────────────────────────

    /// <summary>选择器模式。必须在 <see cref="Initialize"/> 之前赋值。</summary>
    public PickerMode Mode { get; set; } = PickerMode.PickFolder;

    /// <summary>
    /// 简单模式（默认 <c>true</c>）：不显示复选框，当前目录里选中的项就是结果，
    /// 切换目录即清空已选（覆盖语义）。<c>false</c> = 高级模式：复选框 + 跨目录累积。
    /// </summary>
    /// <remarks>
    /// 设置窗口内嵌使用时默认简单模式；高级模式给「一次挑多个目录里的文件」用。
    /// 切换时会就地刷新已加载列表的复选框列，并（切回简单时）把已选同步到列表选中态。
    /// </remarks>
    public bool IsSimpleMode
    {
        get => _isSimpleMode;
        set
        {
            if (_isSimpleMode == value) return;
            _isSimpleMode = value;
            ApplySimpleModeToItems();
        }
    }

    private bool _isSimpleMode = true;

    // ── Initialize 注入的调用参数（宿主每个实例写一次） ──────────────────

    private IReadOnlyList<ArchiveItem>? _entries;
    private string? _defaultExtension;
    private string? _suggestedFileName;
    private IReadOnlyList<FileTypeOption> _fileTypeOptions = Array.Empty<FileTypeOption>();
    private string _extractCurrentFolder = string.Empty;
    // 去 readonly：参数区 CheckBox 勾选变化时需回写本字段（决策 A：仅本次生效，不回写设置）
    private bool _extractPreserveFullPath;
    // 左侧面板标签是否记住上次（设置项 PickerRememberTab）：false = 固定标签，关闭时不回写
    private bool _rememberPickerTab;

    // ── Navigation state ──

    private readonly List<string> _backStack = new();
    private readonly List<string> _forwardStack = new();
    private string _currentDir = string.Empty;

    // ── Debounce for extract preview ──

    private CancellationTokenSource? _previewDebounceCts;

    // ── 对外只读状态 ─────────────────────────────────────────────────────

    /// <summary>当前所在目录（未初始化时为空串转 null）。</summary>
    public string? CurrentPath => string.IsNullOrEmpty(_currentDir) ? null : _currentDir;

    /// <summary>PickItems 模式累积选中的路径列表（按路径排序，FullPath 去重）。</summary>
    public IReadOnlyList<string> SelectedPaths => _accumulatedPaths
        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>最近一次 <see cref="TryConfirm"/> 解析出的路径（取消/未确认时为 null）。</summary>
    public string? ConfirmedPath { get; private set; }

    /// <summary>最近一次 <see cref="TryConfirm"/> 解析出的文件名（保存/打开模式有意义）。</summary>
    public string ConfirmedFileName { get; private set; } = string.Empty;

    /// <summary>参数项列表（供 XAML <c>{Binding Options}</c> 使用）。</summary>
    public IReadOnlyList<PickerOptionItem> Options => _options;

    /// <summary>参数区容器（供宿主做可见性与结构断言；空注册表时 <c>IsVisible=false</c>）。</summary>
    public Grid OptionsArea => OptionsRow;

    /// <summary>本次解压是否保留完整路径。仅本次生效，不回写 AppSettings（决策 A）。</summary>
    public bool SelectedPreserveFullPath => _extractPreserveFullPath;

    /// <summary>根目录时无前缀可裁，两模式结果相同 → 禁用（决策 a）。</summary>
    public bool IsPreserveFullPathToggleAvailable => !string.IsNullOrEmpty(_extractCurrentFolder);

    // ── 事件 ─────────────────────────────────────────────────────────────

    /// <summary>PickItems 累积选中集合发生变化（勾选 / 批量增删 / 清空 / 回填）。</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// 单文件模式下用户已确认某个文件（双击 / 回车 / 地址栏输入文件路径）。
    /// 参数为新确认的文件路径；壳层据此走「确定」流程（写回结果并关窗）。
    /// </summary>
    public event EventHandler<string>? ConfirmRequested;

    /// <summary>
    /// 用户点击了地址行「系统浏览」按钮。原生 <c>StorageProvider</c> 调用属规则 15 唯一豁免，
    /// 只能留在壳层 <see cref="CustomFilePickerDialog"/> 内，故此处仅上抛意图。
    /// </summary>
    public event EventHandler? SystemBrowseRequested;

    /// <summary>
    /// 当前浏览目录变化（参数为新的绝对路径）。内嵌到设置窗时，
    /// 「输出目录 / 解压目标目录」就靠它跟随选择器的导航实时回填。
    /// </summary>
    public event EventHandler<string>? PathChanged;

    // ── 界面文案（规则 13：全部走 LocalizationManager.T） ─────────────────

    public string BackText => LocalizationManager.T("Picker_Back");
    public string ForwardText => LocalizationManager.T("Picker_Forward");
    public string UpText => LocalizationManager.T("Picker_Up");
    public string BrowseSystemTooltip => LocalizationManager.T("Picker_BrowseSystemTooltip");
    public string FileNameLabel => LocalizationManager.T("Picker_FileName");
    public string FileTypeLabel => LocalizationManager.T("Picker_FileType");
    public string ExtractPreviewTitle => LocalizationManager.T("Picker_ExtractPreviewTitle");

    // ── 通用参数区（宿主，不认识任何具体参数） ────────────────────────────

    /// <summary>
    /// 「本次调用需要显示哪些参数」的注册表。空 → 参数区（OptionsRow）整区隐藏（规则 6）。
    /// </summary>
    /// <remarks>
    /// 渲染层只遍历本集合生成「标签 + 控件」，不认识任何具体 key —— 新增参数只需
    /// 调用 <see cref="AddOption"/> 注册一项，无需改动渲染层与布局（决策 3）。
    /// XAML 的 <c>{Binding Options}</c> 是 ItemsSource 的唯一数据源，代码后台不重复赋值。
    /// </remarks>
    private readonly ObservableCollection<PickerOptionItem> _options = new();

    /// <summary>参数区标题文案（供 XAML 绑定；刻意无同名 x:Name，避免 CS0102）。</summary>
    public string OptionsCaptionText => LocalizationManager.T("Picker_OptionsCaption");

    /// <summary>
    /// 注册一个参数项。新增参数只需调用此方法，不改渲染层与布局（决策 3）。
    /// </summary>
    /// <param name="key">参数键，同时作为返回通道取值键。</param>
    /// <param name="labelKey">标签 i18n key（规则 13）。</param>
    /// <param name="initial">初值（调用方传入的设置值），在构造 <see cref="PickerOptionItem"/> 时一次性写入。</param>
    /// <param name="onChanged">值变化回调（参数自身消费）。</param>
    /// <param name="isEnabled">可用性判定；null = 始终可用。<b>生命周期内只求值一次</b>
    /// （可用性依赖的 <c>_extractCurrentFolder</c> 是 Initialize 期固定字段，生命周期内不会变化）。</param>
    /// <param name="disabledHintKey">禁用提示 i18n key；null = 不提示。</param>
    private PickerOptionItem AddOption(
        string key, string labelKey, bool initial,
        Action<bool> onChanged, Func<bool>? isEnabled = null,
        string? disabledHintKey = null)
    {
        var item = new PickerOptionItem
        {
            Key = key,
            Label = LocalizationManager.T(labelKey),
            IsChecked = initial,                                   // 初值在此一次性写入（D1 修法）
            IsEnabled = isEnabled?.Invoke() ?? true,               // 可用性静态，只求值一次
            DisabledHint = disabledHintKey == null ? null : LocalizationManager.T(disabledHintKey),
        };
        item.PropertyChanged += (_, e) =>
        {
            // 只有 IsChecked 会变化（CheckBox 双向绑定回推）；无派生属性，无需手动通知
            if (e.PropertyName == nameof(PickerOptionItem.IsChecked))
                onChanged(item.IsChecked);
        };
        _options.Add(item);
        OptionsRow.IsVisible = true;                               // 空态：任一参数注册即显示
        return item;
    }

    // ── 构造 ──────────────────────────────────────────────────────────────

    /// <summary>设计时/XAML 无参构造函数。</summary>
    public CustomFilePickerControl()
    {
        InitializeComponent();
        DataContext = this;
    }

    // ── 公开初始化入口（等价于原对话框「构造 + 初始化」的全部副作用） ──────

    /// <summary>
    /// 初始化选择器：注册参数项、切换右栏面板、解析初始目录、绑定地址栏补全。
    /// 与本控件构造后再调用等价于原 <c>CustomFilePickerDialog</c> 构造函数的全部初始化逻辑。
    /// </summary>
    /// <param name="initialPath">上下文目录（按 <see cref="AppSettings.DefaultPathOrder"/> 优先级链解析）。</param>
    /// <param name="preselected">预选路径（仅 PickItems 模式累积；null / 空 = 保持既有行为，不预选）。</param>
    /// <param name="entries">解压条目（仅 ExtractFolder 模式用于解压预览）。</param>
    /// <param name="defaultExtension">保存模式默认扩展名。</param>
    /// <param name="fileExtensions">打开模式的扩展名筛选器。</param>
    /// <param name="fileTypes">自定义文件类型下拉项（null = 按模式生成默认值）。</param>
    /// <param name="currentFolder">解压场景的压缩包内当前目录（决定「保留完整路径」是否可用）。</param>
    /// <param name="preserveFullPath">解压场景「保留完整路径」的初始勾选值。</param>
    /// <param name="suggestedFileName">保存模式下文件名框的建议文件名。</param>
    public void Initialize(
        string? initialPath = null,
        IReadOnlyList<string>? preselected = null,
        IReadOnlyList<ArchiveItem>? entries = null,
        string? defaultExtension = null,
        string[]? fileExtensions = null,
        IReadOnlyList<FileTypeOption>? fileTypes = null,
        string currentFolder = "",
        bool preserveFullPath = true,
        string? suggestedFileName = null)
    {
        _entries = entries;
        _defaultExtension = defaultExtension;
        _suggestedFileName = suggestedFileName;
        _extractCurrentFolder = currentFolder;
        _extractPreserveFullPath = preserveFullPath;
        _fileTypeOptions = BuildFileTypeOptions(Mode, fileExtensions, fileTypes);

        // ── Extract 保留完整路径开关（参数区的一个参数项，仅 ExtractFolder 模式注册）──
        // 注册即触发 ItemsControl 渲染（ObservableCollection 自带 INotifyCollectionChanged），
        // XAML {Binding Options} 指向本控件（DataContext = this）。
        if (Mode == PickerMode.ExtractFolder)
        {
            AddOption(
                key: "preserveFullPath",
                labelKey: "Picker_PreserveFullPath",
                initial: _extractPreserveFullPath,                 // 初值即字段值（调用方传入的设置值）
                onChanged: v =>
                {
                    _extractPreserveFullPath = v;
                    // 预览树 300ms 防抖重建，实时反映勾选变化（_currentDir 由稍后的
                    // NavigateTo(startDir) 赋值；回调仅在用户交互时触发，初始化期不会执行）
                    SchedulePreviewRebuild(_currentDir);
                },
                isEnabled: () => IsPreserveFullPathToggleAvailable,
                disabledHintKey: "Picker_PreserveFullPathDisabledHint");
        }
        // ★ 不在此处赋 ItemsControl.ItemsSource —— XAML 已绑定 {Binding Options}（单一数据源）

        // 右侧面板切换：PickItems 显示累积面板，ExtractFolder 显示解压预览面板
        PickItemsPanel.IsVisible = Mode == PickerMode.PickItems;
        ExtractFolderPanel.IsVisible = Mode == PickerMode.ExtractFolder;
        // 右栏分隔条：仅当右栏面板可见时显示（否则拖动无意义）
        RightSplitter.IsVisible = Mode is PickerMode.PickItems or PickerMode.ExtractFolder;

        // 右栏列宽：显示面板的模式用持久化像素宽度（拖拽范围受 ColumnDefinition Min/Max 约束），
        // 其余模式保持 Auto（面板隐藏时列塌缩为 0，文件列表占满整行）
        if (Mode is PickerMode.PickItems or PickerMode.ExtractFolder)
        {
            var settings = AppSettings.Load();
            var saved = Mode == PickerMode.PickItems ? settings.PickItemsPanelWidth : settings.ExtractFolderPanelWidth;
            var rightCol = BrowserGrid.ColumnDefinitions[4];
            rightCol.Width = new GridLength(saved is >= 200 and <= 800 ? saved : 260);
            rightCol.MinWidth = 200;
            rightCol.MaxWidth = 800;
        }
        else
        {
            var rightCol = BrowserGrid.ColumnDefinitions[4];
            rightCol.Width = GridLength.Auto;
            rightCol.MinWidth = 0;
            rightCol.MaxWidth = double.PositiveInfinity;
        }
        AccumulatedItemsControl.ItemsSource = _accumulatedItems;
        ClearAccumulatedButtonText.Text = LocalizationManager.T("Picker_ClearSelection");

        // PickItems 模式：批量按钮文案固定（计数显示在下方 PickActionCountText，避免按钮宽度跳变）
        if (Mode == PickerMode.PickItems)
        {
            AddSelectedButtonText.Text = LocalizationManager.T("Picker_AddSelected");
            RemoveSelectedButtonText.Text = LocalizationManager.T("Picker_RemoveSelected");
            // 初始计数文本：未选中任何项时的灰字占位
            PickActionCountText.Text = LocalizationManager.T("Picker_AddRemoveEmpty");
            PickActionCountText.Foreground = GetThemeBrush("ThemeTextSecondaryBrush");
        }

        // 系统浏览按钮：PickItems / ExtractFolder 模式隐藏
        if (Mode is PickerMode.PickItems or PickerMode.ExtractFolder)
        {
            SystemBrowseButton.IsVisible = false;
        }

        // 文件名/文件类型行：仅 SaveFile / OpenFile 模式显示
        if (Mode is PickerMode.SaveFile or PickerMode.OpenFile)
        {
            InitFileNameArea(Mode);
        }
        else
        {
            FileNameArea.IsVisible = false;
        }

        // 预选路径回填（仅 PickItems 累积；必须在 NavigateTo 之前，列表项才能带上勾选态）
        if (preselected is { Count: > 0 })
        {
            foreach (var path in preselected)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (!_accumulatedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    _accumulatedPaths.Add(path);
            }
            UpdatePickPanel();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        // 左侧「路径切换面板」标签：记住模式 = 上次关闭时的标签，固定模式 = 设置里选定的标签。
        // 两者对控件是同一件事（给一个标签），差别只在关闭时是否回写（见 PersistUiState）。
        // 与下方优先级链无关：优先级链只决定 NavigateTo 的落点目录，不决定面板标签。
        var pickerSettings = AppSettings.Load();
        _rememberPickerTab = pickerSettings.PickerRememberTab;
        QuickPath.SelectTab(QuickPathControl.ParseTabOrDefault(pickerSettings.PickerSourceTab));

        // 初始目录
        var startDir = ResolveInitialPath(initialPath);
        NavigateTo(startDir);

        // 地址栏补全/历史
        InitBreadcrumb();
    }

    // ── 宿主可调用的方法 ─────────────────────────────────────────────────

    /// <summary>由宿主尝试导航到目录。<paramref name="path"/> 不是已存在目录时返回 false。</summary>
    public bool TryNavigate(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var dir = path.Trim();
        if (!Directory.Exists(dir)) return false;
        NavigateTo(dir);
        return true;
    }

    /// <summary>强制控制参数区整区显隐（规则 6：隐藏而非禁用）。</summary>
    public void SetOptionsVisible(bool visible) => OptionsRow.IsVisible = visible;

    /// <summary>
    /// 同步左侧面板标签到设置（仅「记住标签」模式）与右栏面板宽度，
    /// 供宿主在窗口关闭时调用。仅在值确有变化时落盘。
    /// </summary>
    public void PersistUiState()
    {
        var settings = AppSettings.Load();
        var dirty = false;

        // 左侧面板标签：固定模式下切换只是临时状态，不回写（否则「固定」会退化成「记住」）
        if (_rememberPickerTab)
        {
            var tabName = QuickPath.CurrentTab.ToString();
            if (!string.Equals(settings.PickerSourceTab, tabName, StringComparison.Ordinal))
            {
                settings.PickerSourceTab = tabName;
                dirty = true;
            }
        }

        // 右栏宽度：仅 PickItems / ExtractFolder 模式右栏可见；其余模式右栏 Auto 塌缩，无需保存
        if (Mode is PickerMode.PickItems or PickerMode.ExtractFolder)
        {
            // 取列的实际宽度（拖拽后 rightCol.Width 为像素值，此时等于实际宽度）
            var rightCol = BrowserGrid.ColumnDefinitions[4];
            double width = rightCol.Width.IsAbsolute ? rightCol.Width.Value : rightCol.ActualWidth;
            // 超出面板 Min/Max 约束的宽度不保存（防御）
            if (width is >= 200 and <= 800)
            {
                if (Mode == PickerMode.PickItems && settings.PickItemsPanelWidth != width)
                {
                    settings.PickItemsPanelWidth = width;
                    dirty = true;
                }
                else if (Mode == PickerMode.ExtractFolder && settings.ExtractFolderPanelWidth != width)
                {
                    settings.ExtractFolderPanelWidth = width;
                    dirty = true;
                }
            }
        }

        if (dirty) settings.Save();
    }

    /// <summary>
    /// 执行「确定」：解析并写入 <see cref="ConfirmedPath"/> / <see cref="ConfirmedFileName"/>
    /// （PickItems 模式结果见 <see cref="SelectedPaths"/>）。
    /// 返回 <c>false</c> 表示不应关闭（已弹出校验提示，窗口保持打开）。
    /// </summary>
    public bool TryConfirm()
    {
        var selected = FileList.SelectedItem as FileBrowserItem;

        switch (Mode)
        {
            case PickerMode.PickFolder:
            case PickerMode.ExtractFolder:
                // 优先使用浏览区选中项，否则当前目录
                if (selected is { IsDirectory: true })
                {
                    ConfirmedPath = selected.FullPath;
                }
                else
                {
                    ConfirmedPath = _currentDir;
                }
                return true;

            case PickerMode.PickItems:
                // 结果在 SelectedPaths（累积集合），无需解析单一路径
                ConfirmedPath = null;
                return true;

            case PickerMode.OpenFile:
                // 优先文件名框输入：完整路径或相对当前目录的文件名
                var typedPath = ResolveTypedFilePath(FileNameBox.Text);
                if (typedPath != null)
                {
                    ConfirmedPath = typedPath;
                    ConfirmedFileName = Path.GetFileName(typedPath);
                    return true;
                }

                // 回退：浏览区选中文件
                if (selected is { IsDirectory: false })
                {
                    ConfirmedPath = selected.FullPath;
                    ConfirmedFileName = selected.DisplayName;
                    return true;
                }

                _ = AppMessageBox.Show(
                    LocalizationManager.T("Picker_WarningSelectFile"),
                    LocalizationManager.T("Picker_PickFolderTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning, OwnerWindow);
                return false;

            case PickerMode.SaveFile:
                // 文件名来自底部文件名文本框
                var name = FileNameBox.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    _ = AppMessageBox.Show(
                        LocalizationManager.T("Picker_WarningEnterName"),
                        LocalizationManager.T("Picker_SaveFileTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning, OwnerWindow);
                    return false;
                }
                // 格式联动：应用当前文件类型扩展名
                if (!Path.HasExtension(name))
                {
                    name = name + GetSelectedSaveExtension();
                }
                ConfirmedPath = Path.Combine(_currentDir, name);
                ConfirmedFileName = name;
                return true;

            default:
                return false;
        }
    }

    /// <summary>「系统浏览」保存模式下的建议文件名（优先浏览区选中文件，其次地址栏输入的最后一段）。</summary>
    public string GetSaveFileName()
    {
        // 优先浏览区选中的文件，其次地址栏输入的最后一段
        if (FileList.SelectedItem is FileBrowserItem { IsDirectory: false } item)
            return item.DisplayName;
        var text = PathCrumbs.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var name = Path.GetFileName(text.TrimEnd('\\', '/'));
        // 如果输入的是已有目录路径，则不是文件名
        if (Directory.Exists(text)) return string.Empty;
        // 如果 name 是当前目录名（即输入了目录本身），返回空
        if (string.Equals(name, Path.GetFileName(_currentDir.TrimEnd('\\', '/')), StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return name;
    }

    /// <summary>宿主窗口（用于弹 <see cref="AppMessageBox"/> / <see cref="AddFavoriteDialog"/>）。</summary>
    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

    // ── File type options ──────────────────────────────────────────────────

    /// <summary>构建文件类型下拉项：优先用调用方传入的 <paramref name="fileTypes"/>，
    /// 否则按模式生成默认值（保持各调用方既有行为不变）。</summary>
    private static IReadOnlyList<FileTypeOption> BuildFileTypeOptions(
        PickerMode mode, string[]? fileExtensions, IReadOnlyList<FileTypeOption>? fileTypes)
    {
        if (fileTypes is { Count: > 0 })
            return fileTypes;

        switch (mode)
        {
            case PickerMode.SaveFile:
                // 默认保存目标为压缩包：下拉项即压缩格式（选中时联动文件名扩展名）
                return
                [
                    new FileTypeOption("*.zip", [".zip"]),
                    new FileTypeOption("*.7z", [".7z"]),
                    new FileTypeOption("*.tar.gz", [".tar.gz"])
                ];

            case PickerMode.OpenFile:
                // 默认两项：按筛选器过滤 / 不过滤。fileExtensions 为空时第一项也不过滤
                // （保持「无筛选器 = 显示所有文件」的既有语义）。
                return
                [
                    new FileTypeOption(LocalizationManager.T("Picker_FileTypeArchive"),
                        fileExtensions is { Length: > 0 } ? fileExtensions : []),
                    new FileTypeOption(LocalizationManager.T("Picker_FileTypeAll"), [])
                ];

            default:
                return Array.Empty<FileTypeOption>();
        }
    }

    /// <summary>把扩展名模式（"*.json" / ".json" / "json"）归一化为扩展名（".json"）。
    /// "*" / "*.*" / 空 → 返回 null（表示无扩展名约束）。</summary>
    private static string? NormalizeExtension(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return null;

        var p = pattern.Trim();
        if (p is "*" or "*.*")
            return null;
        if (p.StartsWith("*."))
            p = p[1..];              // "*.json" → ".json"
        if (!p.StartsWith('.'))
            p = "." + p;             // "json"  → ".json"
        return p.Length > 1 ? p : null;
    }

    /// <summary>取当前选中的文件类型下拉项（ItemsSource 与 <see cref="_fileTypeOptions"/> 同序，索引一一对应）。</summary>
    private FileTypeOption? GetSelectedFileTypeOption()
    {
        if (_fileTypeOptions.Count == 0)
            return null;
        var index = FileTypeSelector.SelectedIndex;
        return index >= 0 && index < _fileTypeOptions.Count ? _fileTypeOptions[index] : null;
    }

    // ── File name + File type row ──────────────────────────────────────────

    private bool _isSyncingFileType;

    /// <summary>
    /// 初始化底部文件名 + 文件类型行。下拉项来自 <see cref="_fileTypeOptions"/>
    /// （调用方自定义优先，否则按模式生成默认值，见 <see cref="BuildFileTypeOptions"/>）。
    /// - SaveFile：选中与 defaultExtension 匹配的类型，文件名框预填 suggestedFileName 或 untitled{扩展名}；切换类型时更新扩展名
    /// - OpenFile：选中第一项；切换时按该项的 Patterns 重新过滤列表
    /// </summary>
    private void InitFileNameArea(PickerMode mode)
    {
        FileTypeSelector.ItemsSource = _fileTypeOptions.Select(o => o.Label).ToList();

        if (mode == PickerMode.SaveFile)
        {
            _isSyncingFileType = true;
            try
            {
                var index = _defaultExtension is null ? -1 : FindSaveFileTypeIndex(_defaultExtension);
                FileTypeSelector.SelectedIndex = index >= 0 ? index : 0;
            }
            finally
            {
                _isSyncingFileType = false;
            }

            // 预填文件名：调用方给了建议文件名就用它，否则沿用 untitled + 默认扩展名
            FileNameBox.Text = !string.IsNullOrEmpty(_suggestedFileName)
                ? _suggestedFileName
                : _defaultExtension == null
                    ? string.Empty
                    : "untitled" + _defaultExtension;
        }
        else // OpenFile
        {
            _isSyncingFileType = true;
            try
            {
                FileTypeSelector.SelectedIndex = 0;
            }
            finally
            {
                _isSyncingFileType = false;
            }
        }
    }

    /// <summary>找出 Patterns 覆盖指定扩展名的下拉项索引；无匹配返回 -1。</summary>
    private int FindSaveFileTypeIndex(string extension)
    {
        for (var i = 0; i < _fileTypeOptions.Count; i++)
        {
            if (_fileTypeOptions[i].Patterns.Any(
                    p => string.Equals(NormalizeExtension(p), extension, StringComparison.OrdinalIgnoreCase)))
                return i;
        }
        return -1;
    }

    private bool _isSyncingFileName;

    private void FileNameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        // 程序内同步文件名框（选中文件）时不触发清除选中
        if (_isSyncingFileName) return;

        // 用户在文件名框输入时清除选中（避免选中文件覆盖输入）
        if (FileList.SelectedItem is FileBrowserItem { IsDirectory: false })
        {
            FileList.SelectedItem = null;
        }
    }

    /// <summary>同步文件名框内容（不触发清除选中逻辑）。</summary>
    private void SyncFileNameBox(string fileName)
    {
        _isSyncingFileName = true;
        try
        {
            FileNameBox.Text = fileName;
        }
        finally
        {
            _isSyncingFileName = false;
        }
    }

    private void FileTypeSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingFileType) return;
        if (FileTypeSelector.SelectedIndex < 0) return;

        if (Mode == PickerMode.OpenFile)
        {
            // 重新加载当前目录应用新筛选
            LoadDirectory(_currentDir);
        }
        else if (Mode == PickerMode.SaveFile)
        {
            UpdateSaveFileExtension();
        }
    }

    /// <summary>SaveFile 模式：文件类型切换时更新文件名扩展名（格式联动）。</summary>
    private void UpdateSaveFileExtension()
    {
        var extension = GetSelectedSaveExtension();
        if (string.IsNullOrEmpty(extension)) return;

        var name = FileNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name)) return;

        // 去掉旧扩展名，换上当前类型扩展名
        var baseName = Path.GetFileNameWithoutExtension(name);
        FileNameBox.Text = baseName + extension;
    }

    private string GetSelectedSaveExtension()
    {
        var selected = GetSelectedFileTypeOption();
        if (selected != null)
        {
            foreach (var pattern in selected.Patterns)
            {
                var ext = NormalizeExtension(pattern);
                if (!string.IsNullOrEmpty(ext))
                    return ext;
            }
        }
        return _defaultExtension ?? ".zip";
    }

    // ── Path resolution ────────────────────────────────────────────────────

    /// <summary>
    /// 按用户设置的优先级链解析初始路径。
    /// 链顺序来自 <see cref="AppSettings.DefaultPathOrder"/>（值域 context/explorer/recent/custom），
    /// 依次探测第一个存在的路径；桌面始终作为最终兜底。
    /// </summary>
    private static string ResolveInitialPath(string? initialPath)
    {
        var settings = AppSettings.Load();
        var order = settings.DefaultPathOrder ?? new List<string>();

        // 依次尝试链上的每一项；未知值跳过
        foreach (var kind in order)
        {
            var candidate = kind switch
            {
                "context" => ResolveContextPath(initialPath),
                "explorer" => ExplorerWindowTracker.GetActiveExplorerPath(),
                "recent" => PathHistoryManager.GetRecent(1).FirstOrDefault()?.Path,
                "custom" => ResolveCustomPath(settings.CustomDefaultPath),
                _ => null
            };
            if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        // 兜底：桌面
        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    /// <summary>场景路径：调用方传入的上下文路径（存在则用，否则 null 跳过）。</summary>
    private static string? ResolveContextPath(string? initialPath)
    {
        if (string.IsNullOrWhiteSpace(initialPath)) return null;
        var p = Environment.ExpandEnvironmentVariables(initialPath.Trim());
        if (Directory.Exists(p)) return p;
        var dir = Path.GetDirectoryName(p);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
        if (File.Exists(p)) return Path.GetDirectoryName(p);
        return null;
    }

    /// <summary>手动路径：用户填写的固定路径（留空或不存在则 null 跳过）。</summary>
    private static string? ResolveCustomPath(string? customPath)
    {
        if (string.IsNullOrWhiteSpace(customPath)) return null;
        var p = Environment.ExpandEnvironmentVariables(customPath.Trim());
        return Directory.Exists(p) ? p : null;
    }

    // ── Address bar ────────────────────────────────────────────────────────

    private void InitBreadcrumb()
    {
        // 编辑态补全：历史匹配 + 父目录下以输入为前缀的子目录（与原 AutoCompleteBox 行为一致）
        PathCrumbs.EditCompletionProvider = text =>
        {
            var t = (text ?? string.Empty).Trim();
            var suggestions = new List<string>();

            if (t.Length >= 2)
            {
                suggestions.AddRange(PathHistoryManager.GetRecent(50)
                    .Select(h => h.Path)
                    .Where(p => p.Contains(t, StringComparison.OrdinalIgnoreCase))
                    .Take(10));

                try
                {
                    var parent = Path.GetDirectoryName(t.TrimEnd('\\', '/'));
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    {
                        var prefix = Path.GetFileName(t.TrimEnd('\\', '/'));
                        suggestions.AddRange(Directory.EnumerateDirectories(parent, prefix + "*", SearchOption.TopDirectoryOnly)
                            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                            .Take(20));
                    }
                }
                catch
                {
                    // 非法路径等，忽略
                }
            }
            else
            {
                suggestions.AddRange(PathHistoryManager.GetRecent(50).Select(h => h.Path).Take(50));
            }

            return suggestions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        };

        // 段点击 / 编辑态 Enter：严格照抄原 PathAutoComplete_KeyDown 分支（含 Trim，且**不加模式门**）
        PathCrumbs.NavigateRequested += (_, raw) =>
        {
            var text = (raw ?? string.Empty).Trim();
            if (Directory.Exists(text))
            {
                NavigateTo(text);
            }
            else if (File.Exists(text))
            {
                TryConfirmFile(text);
            }
        };
    }

    // ── Navigation ─────────────────────────────────────────────────────────

    private void NavigateTo(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        if (!string.IsNullOrEmpty(_currentDir) &&
            !string.Equals(_currentDir, dir, StringComparison.OrdinalIgnoreCase))
        {
            _backStack.Add(_currentDir);
            _forwardStack.Clear();
        }
        _currentDir = dir;

        // 简单模式：切换目录即清空已选（「当前目录选中项 = 结果」的覆盖语义）
        if (_isSimpleMode && Mode == PickerMode.PickItems)
        {
            ClearAccumulatedSelection();
        }

        CurrentPathText.Text = dir;
        PathCrumbs.Path = dir;
        PathHistoryManager.Record(dir);
        PathChanged?.Invoke(this, dir);

        LoadDirectory(dir);
        QuickPath.SetCurrentPath(dir);
        UpdateFavoriteButtonState();
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
    {
        if (_backStack.Count == 0) return;
        var dir = _backStack[^1];
        _backStack.RemoveAt(_backStack.Count - 1);
        if (!string.IsNullOrEmpty(_currentDir))
            _forwardStack.Add(_currentDir);
        _currentDir = dir;
        CurrentPathText.Text = dir;
        PathCrumbs.Path = dir;
        LoadDirectory(dir);
        QuickPath.SetCurrentPath(dir);
        UpdateFavoriteButtonState();
    }

    private void Forward_Click(object? sender, RoutedEventArgs e)
    {
        if (_forwardStack.Count == 0) return;
        var dir = _forwardStack[^1];
        _forwardStack.RemoveAt(_forwardStack.Count - 1);
        if (!string.IsNullOrEmpty(_currentDir))
            _backStack.Add(_currentDir);
        _currentDir = dir;
        CurrentPathText.Text = dir;
        PathCrumbs.Path = dir;
        LoadDirectory(dir);
        QuickPath.SetCurrentPath(dir);
        UpdateFavoriteButtonState();
    }

    private void Up_Click(object? sender, RoutedEventArgs e)
    {
        var parent = Path.GetDirectoryName(_currentDir.TrimEnd('\\', '/'));
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            NavigateTo(parent);
    }

    // ── Directory listing ──────────────────────────────────────────────────

    private readonly ObservableCollection<FileBrowserItem> _fileItems = new();

    /// <summary>右侧累积面板显示项（PickItems 模式）。</summary>
    private sealed record AccumulatedPathItem(string Path);

    private readonly ObservableCollection<AccumulatedPathItem> _accumulatedItems = new();

    private readonly List<string> _accumulatedPaths = new();

    /// <summary>
    /// 同步保护：宿主回填 / 简单模式列表选中态同步时置 true，避免
    /// <see cref="FileList_SelectionChanged"/> 与 <c>IsSelected</c> 订阅互相回推造成抖动。
    /// </summary>
    private bool _syncingSelection;

    private void LoadDirectory(string dir)
    {
        _fileItems.Clear();
        FileList.ItemsSource = _fileItems;
        FileList.SelectedItem = null;

        try
        {
            // 目录列表
            var showFiles = Mode is PickerMode.SaveFile or PickerMode.OpenFile or PickerMode.PickItems;
            var dirs = Directory.EnumerateDirectories(dir)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var files = showFiles
                ? Directory.EnumerateFiles(dir)
                    .Where(MatchesFileFilter)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

            foreach (var d in dirs)
            {
                _fileItems.Add(CreateDirItem(d));
            }
            foreach (var f in files)
            {
                _fileItems.Add(CreateFileItem(f));
            }

            // 解压模式下路径变化 → 防抖重建预览
            if (Mode == PickerMode.ExtractFolder && _entries != null)
            {
                SchedulePreviewRebuild(dir);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomFilePickerDialog] LoadDirectory failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 文件筛选器匹配（OpenFile 模式）。按当前选中的文件类型下拉项的 Patterns 过滤，
    /// 支持 "*.zip" / ".zip" / "zip" 格式（统一经 <see cref="NormalizeExtension"/> 归一化）。
    /// Patterns 为空（"所有文件" 项）或无下拉项时显示所有文件。
    /// </summary>
    private bool MatchesFileFilter(string filePath)
    {
        if (Mode != PickerMode.OpenFile)
            return true;

        var patterns = GetSelectedFileTypeOption()?.Patterns;
        if (patterns is null || patterns.Length == 0)
            return true;

        var ext = Path.GetExtension(filePath);
        foreach (var pattern in patterns)
        {
            var normalized = NormalizeExtension(pattern);
            if (normalized != null && string.Equals(ext, normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private FileBrowserItem CreateDirItem(string path)
    {
        var info = new DirectoryInfo(path);
        var item = new FileBrowserItem
        {
            FullPath = path,
            DisplayName = info.Name,
            IsDirectory = true,
            Icon = IconService.GetFolderIcon(),
            SizeText = string.Empty,
            ModifiedText = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
            ShowSubText = false,
            CanCheck = Mode == PickerMode.PickItems && !_isSimpleMode
        };
        SubscribeIsSelected(item);
        item.IsSelected = _accumulatedPaths.Contains(path, StringComparer.OrdinalIgnoreCase);
        return item;
    }

    private FileBrowserItem CreateFileItem(string path)
    {
        var info = new FileInfo(path);
        var ext = info.Extension;
        var item = new FileBrowserItem
        {
            FullPath = path,
            DisplayName = info.Name,
            IsDirectory = false,
            Icon = IconService.GetFileIcon(string.IsNullOrEmpty(ext) ? ".file" : ext),
            SizeText = FormatUtil.FormatSize(info.Length),
            ModifiedText = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
            ShowSubText = false,
            CanCheck = Mode == PickerMode.PickItems && !_isSimpleMode
        };
        SubscribeIsSelected(item);
        item.IsSelected = _accumulatedPaths.Contains(path, StringComparer.OrdinalIgnoreCase);
        return item;
    }

    /// <summary>
    /// 订阅勾选状态变更：勾选框点击等通过 TwoWay 绑定修改 IsSelected 时，
    /// 统一走 <see cref="ToggleAccumulated"/> 累积/移除。
    /// </summary>
    private void SubscribeIsSelected(FileBrowserItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FileBrowserItem.IsSelected))
            {
                // 宿主回填 / 简单模式同步期间不回推，否则会与列表选中态互相触发
                if (_syncingSelection) return;
                ToggleAccumulated(item.FullPath, item.IsSelected);
            }
        };
    }

    // ── Extract preview (debounced) ────────────────────────────────────────

    private void SchedulePreviewRebuild(string destDir)
    {
        _previewDebounceCts?.Cancel();
        _previewDebounceCts = new CancellationTokenSource();
        var token = _previewDebounceCts.Token;

        // 防抖 ~300ms
        Task.Delay(300, token).ContinueWith(t =>
        {
            if (t.IsCanceled || token.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested) return;
                try
                {
                    var root = ResultPreviewService.BuildExtractPreview(
                        _entries ?? Array.Empty<ArchiveItem>(),
                        destDir,
                        checkExists: true,
                        preserveFullPath: _extractPreserveFullPath,
                        currentFolder: _extractCurrentFolder);
                    PreviewTree.Root = root;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CustomFilePickerDialog] Preview rebuild failed: {ex.Message}");
                }
            });
        }, token);
    }

    // ── List interaction ───────────────────────────────────────────────────

    private void FileList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // PickItems：仅刷新批量按钮计数（Extended 多选）
        if (Mode == PickerMode.PickItems)
        {
            // 简单模式没有复选框：列表里选中的文件就是结果（Shift/Ctrl 由 ListBox 自身处理）
            if (_isSimpleMode) SyncAccumulatedFromListSelection();
            UpdatePickPanel();
            return;
        }

        if (FileList.SelectedItem is not FileBrowserItem item) return;
        if (item.IsDirectory)
        {
            // 选择目录：预览/高亮，但不自动进入（等待双击或 Enter）
            QuickPath.SetCurrentPath(item.FullPath);
        }
        else if (Mode is PickerMode.SaveFile or PickerMode.OpenFile)
        {
            // 选中文件 → 文件名框自动同步
            SyncFileNameBox(item.DisplayName);
        }
    }

    private void FileList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FileList.SelectedItem is not FileBrowserItem item) return;
        if (item.IsDirectory)
        {
            NavigateTo(item.FullPath);
        }
        else if (Mode == PickerMode.PickItems)
        {
            // 双击文件：切换勾选累积（IsSelected setter 经订阅累积），不关闭
            item.IsSelected = !item.IsSelected;
        }
        else if (Mode == PickerMode.SaveFile)
        {
            // 保存模式：双击文件 → 填入文件名框（可再改格式）
            SyncFileNameBox(item.DisplayName);
        }
        else
        {
            TryConfirmFile(item.FullPath);
        }
    }

    private void FileList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (FileList.SelectedItem is FileBrowserItem item)
            {
                if (item.IsDirectory)
                    NavigateTo(item.FullPath);
                else if (Mode == PickerMode.PickItems)
                {
                    // PickItems：Enter 切换勾选（同双击），不关闭
                    item.IsSelected = !item.IsSelected;
                }
                else if (Mode == PickerMode.SaveFile)
                    SyncFileNameBox(item.DisplayName); // 保存模式 Enter 文件 → 填入文件名
                else
                    TryConfirmFile(item.FullPath);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Back)
        {
            Up_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Left && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            Back_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Right && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            Forward_Click(sender, e);
            e.Handled = true;
        }
    }

    // ── PickItems accumulation ─────────────────────────────────────────────

    /// <summary>统一累积入口：勾选框/双击/批量/清空/回填全部经过此方法。</summary>
    private void ToggleAccumulated(string path, bool isChecked)
    {
        if (isChecked)
        {
            if (!_accumulatedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                _accumulatedPaths.Add(path);
        }
        else
        {
            _accumulatedPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        }
        UpdatePickPanel();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>批量添加当前高亮项（勾选 + 累积）。IsSelected setter 经订阅自动累积。</summary>
    private void AddSelected_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var item in (FileList.SelectedItems ?? Array.Empty<object>()).OfType<FileBrowserItem>().Where(i => i.CanCheck))
        {
            item.IsSelected = true;
        }
    }

    /// <summary>批量移除当前高亮项（取消勾选 + 取消累积）。IsSelected setter 经订阅自动移除。</summary>
    private void RemoveSelected_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var item in (FileList.SelectedItems ?? Array.Empty<object>()).OfType<FileBrowserItem>())
        {
            item.IsSelected = false;
        }
    }

    /// <summary>累积面板逐项移除（× 按钮）。同步回当前可见列表中对应项。</summary>
    private void RemoveItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            ToggleAccumulated(path, false);
            foreach (var item in _fileItems.Where(i =>
                         string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                item.IsSelected = false;
            }
        }
    }

    /// <summary>清空全部累积。</summary>
    private void ClearAccumulated_Click(object? sender, RoutedEventArgs e) => ClearAccumulatedSelection();

    /// <summary>
    /// 清空已选（累积集合 + 列表勾选/选中态 + 右侧面板）。
    /// 简单模式切目录、以及宿主外部回填时都走这里，保证三处状态一致。
    /// </summary>
    private void ClearAccumulatedSelection()
    {
        _syncingSelection = true;
        try
        {
            _accumulatedPaths.Clear();
            foreach (var item in _fileItems)
            {
                item.IsSelected = false;
            }
            FileList.SelectedItems?.Clear();
        }
        finally
        {
            _syncingSelection = false;
        }
        UpdatePickPanel();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 宿主回填已选项（压缩窗的「已选源文件」↔ 选择器双向同步用）。
    /// 会同步复选框与（简单模式下的）列表选中态，并触发 <see cref="SelectionChanged"/>。
    /// </summary>
    public void SetSelectedPaths(IEnumerable<string>? paths)
    {
        _syncingSelection = true;
        try
        {
            _accumulatedPaths.Clear();
            if (paths is not null)
            {
                foreach (var p in paths)
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    if (!_accumulatedPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                        _accumulatedPaths.Add(p);
                }
            }
            foreach (var item in _fileItems)
            {
                item.IsSelected = _accumulatedPaths.Contains(item.FullPath, StringComparer.OrdinalIgnoreCase);
            }
            if (_isSimpleMode) SyncListSelectionFromAccumulatedCore();
        }
        finally
        {
            _syncingSelection = false;
        }
        UpdatePickPanel();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>切换简单/高级时就地刷新已加载列表的复选框列；切回简单模式时把已选同步到列表选中态。</summary>
    private void ApplySimpleModeToItems()
    {
        _syncingSelection = true;
        try
        {
            foreach (var item in _fileItems)
            {
                item.CanCheck = Mode == PickerMode.PickItems && !_isSimpleMode;
                item.IsSelected = _accumulatedPaths.Contains(item.FullPath, StringComparer.OrdinalIgnoreCase);
            }
            if (_isSimpleMode) SyncListSelectionFromAccumulatedCore();
        }
        finally
        {
            _syncingSelection = false;
        }
        UpdatePickPanel();
    }

    /// <summary>简单模式：列表里选中的文件就是结果（Shift/Ctrl 由 ListBox 自身处理，目录不计）。</summary>
    private void SyncAccumulatedFromListSelection()
    {
        if (_syncingSelection) return;
        var selected = (FileList.SelectedItems ?? Array.Empty<object>())
            .OfType<FileBrowserItem>()
            .Where(i => !i.IsDirectory)
            .Select(i => i.FullPath)
            .ToList();
        _syncingSelection = true;
        try
        {
            _accumulatedPaths.Clear();
            _accumulatedPaths.AddRange(selected);
        }
        finally
        {
            _syncingSelection = false;
        }
        UpdatePickPanel();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 把已选集合回显到列表选中态（宿主回填、切回简单模式时用）。
    /// 调用方需已置 <see cref="_syncingSelection"/>，否则会与选择变化互相触发。
    /// </summary>
    private void SyncListSelectionFromAccumulatedCore()
    {
        if (FileList.SelectedItems is not { } selectedItems) return;
        selectedItems.Clear();
        foreach (var item in _fileItems)
        {
            if (item.IsDirectory) continue;
            if (_accumulatedPaths.Contains(item.FullPath, StringComparer.OrdinalIgnoreCase))
                selectedItems.Add(item);
        }
    }

    /// <summary>刷新右侧累积面板：标题计数、累积列表、批量按钮计数、空占位。</summary>
    private void UpdatePickPanel()
    {
        var count = _accumulatedPaths.Count;

        // 标题计数
        PickTitleText.Text = LocalizationManager.T("Picker_SelectedCount", count);

        // 累积列表（按路径排序）
        _accumulatedItems.Clear();
        foreach (var p in _accumulatedPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            _accumulatedItems.Add(new AccumulatedPathItem(p));
        }

        // 高亮项 → 添加/移除按钮可用性 + 计数文本
        var highlighted = (FileList.SelectedItems ?? Array.Empty<object>())
            .OfType<FileBrowserItem>()
            .Where(i => i.CanCheck)
            .ToList();
        var addable = highlighted.Count(i => !_accumulatedPaths.Contains(i.FullPath, StringComparer.OrdinalIgnoreCase));
        var removable = highlighted.Count(i => _accumulatedPaths.Contains(i.FullPath, StringComparer.OrdinalIgnoreCase));
        AddSelectedButton.IsEnabled = addable > 0;
        RemoveSelectedButton.IsEnabled = removable > 0;
        if (addable > 0 || removable > 0)
        {
            PickActionCountText.Text = LocalizationManager.T("Picker_AddRemoveCount", addable, removable);
            PickActionCountText.Foreground = GetThemeBrush("ThemeTextPrimaryBrush");
        }
        else
        {
            PickActionCountText.Text = LocalizationManager.T("Picker_AddRemoveEmpty");
            PickActionCountText.Foreground = GetThemeBrush("ThemeTextSecondaryBrush");
        }

        // 空占位
        PickEmptyText.Text = LocalizationManager.T("Picker_AccumulatedEmpty");
        PickEmptyText.IsVisible = count == 0;
    }

    /// <summary>从当前应用资源中取主题画刷（主题切换后动态解析）。</summary>
    private static IBrush GetThemeBrush(string key)
    {
        if (Application.Current?.TryFindResource(key, out var brush) == true && brush is IBrush b)
        {
            return b;
        }
        return Brushes.Gray;
    }

    private void QuickPath_PathSelected(object? sender, string path)
    {
        if (Directory.Exists(path))
        {
            NavigateTo(path);
        }
        else if (File.Exists(path) && Mode is PickerMode.SaveFile or PickerMode.OpenFile)
        {
            TryConfirmFile(path);
        }
    }

    // ── Add to favorites ────────────────────────────────────────────────────

    /// <summary>
    /// 收藏按钮点击：弹出 <see cref="AddFavoriteDialog"/>（预填当前目录名+路径），
    /// 确认后加入收藏并刷新速选面板。
    /// </summary>
    private async void AddFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentDir) || FavoritePathManager.Exists(_currentDir)) return;
        // 嵌入场景可能没有宿主窗口（无主窗口 → 无法弹模态框）；窗口内嵌时取所在窗口
        if (OwnerWindow is not { } owner) return;

        var dirName = Path.GetFileName(_currentDir.TrimEnd('\\', '/'));
        var dialog = new AddFavoriteDialog(dirName, _currentDir);
        if (await dialog.ShowDialog<bool>(owner))
        {
            FavoritePathManager.Add(dialog.FavoriteName, dialog.FavoritePath);
            QuickPath.RefreshSources();
            QuickPath.SetCurrentPath(_currentDir);
            UpdateFavoriteButtonState();
        }
    }

    /// <summary>
    /// 同步收藏按钮状态：当前目录已在收藏（含系统路径）时置灰并提示，否则可点击。
    /// 导航路径变化时调用。
    /// </summary>
    private void UpdateFavoriteButtonState()
    {
        var isFavorite = !string.IsNullOrEmpty(_currentDir) && FavoritePathManager.Exists(_currentDir);
        AddFavoriteButton.IsEnabled = !isFavorite;
        ToolTip.SetTip(AddFavoriteButton,
            isFavorite
                ? LocalizationManager.T("Picker_AlreadyFavorite")
                : LocalizationManager.T("Picker_AddFavorite"));
    }

    // ── Confirm / Cancel ───────────────────────────────────────────────────

    /// <summary>双击 / 回车 / 地址栏输入文件路径：上抛 <see cref="ConfirmRequested"/> 由宿主确认。</summary>
    private void TryConfirmFile(string filePath)
    {
        if (!File.Exists(filePath)) return;
        ConfirmRequested?.Invoke(this, filePath);
    }

    /// <summary>
    /// 解析文件名框输入的文件路径。
    /// 支持绝对路径或相对当前目录的文件名；文件不存在时返回 null。
    /// </summary>
    private string? ResolveTypedFilePath(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = input.Trim();

        // 绝对路径
        if (Path.IsPathRooted(text))
        {
            return File.Exists(text) ? text : null;
        }

        // 相对当前目录
        var combined = Path.Combine(_currentDir, text);
        return File.Exists(combined) ? combined : null;
    }

    // ── System dialog fallback ─────────────────────────────────────────────

    /// <summary>
    /// 「系统浏览」按钮：仅上抛 <see cref="SystemBrowseRequested"/> 意图。
    /// 原生 StorageProvider 调用属规则 15 唯一豁免，由壳层 CustomFilePickerDialog 处理并关窗。
    /// </summary>
    private void SystemBrowse_Click(object? sender, RoutedEventArgs e)
        => SystemBrowseRequested?.Invoke(this, EventArgs.Empty);
}
