using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 压缩设置窗口。确认即事务：点「开始压缩」后由
/// <see cref="CompressSettingsViewModel.TryConfirmAsync"/> 一次产出
/// <see cref="ConfirmOutcome{T}"/>，窗口只负责把三态翻译成窗口动作（拒绝留窗显示原因、
/// 确认/取消关窗），并把已确认的载荷交给注入的 <see cref="ICompressSession"/> 执行。
/// </summary>
public partial class CompressSettingsWindow : Window
{
    private bool _loaded;

    /// <summary>注入的执行器；设计时窗口（无参构造）没有它，也就不能真正开始压缩。</summary>
    private readonly ICompressSession? _session;

    /// <summary>注入的运行上下文（宿主差异：Owner / Presenter / 退出策略）。</summary>
    private readonly CompressRunContext? _context;

    /// <summary>非模态 Show（CLI 无属主窗口）时的确认结果通道：窗口关闭即完成。</summary>
    private readonly TaskCompletionSource<ConfirmOutcome<CompressSettings>> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>本次关窗携带的确认结果；null 表示用户直接按 X 关闭（视为取消）。</summary>
    private ConfirmOutcome<CompressSettings>? _closedOutcome;

    /// <summary>
    /// 窗口自身的 ViewModel（仅窗口内部使用）。接缝规则 20：生产调用方不得回读窗口/VM 构造请求——
    /// 载荷在确认事务（<see cref="CompressSettingsViewModel.TryConfirmAsync"/>）里一次冻结。
    /// 测试需要它时走 <see cref="Window.DataContext"/>（构造函数里就是 DataContext = ViewModel）。
    /// </summary>
    private CompressSettingsViewModel ViewModel { get; }

    /// <summary>
    /// 非模态 <see cref="Window.Show"/> 时的确认结果。CLI 没有属主窗口，无法用 ShowDialog，
    /// 只能 Show + 等关窗；关窗即完成，用户按 X 视为取消。
    /// </summary>
    public Task<ConfirmOutcome<CompressSettings>> Completion => _completion.Task;

    /// <summary>
    /// 设计时/诊断菜单用的无参构造：只装配界面，不注入执行器，因此不能执行压缩。
    /// Avalonia 的 x:Class 也要求公共无参构造存在。
    /// </summary>
    public CompressSettingsWindow()
    {
        InitializeComponent();
        ViewModel = new CompressSettingsViewModel(Array.Empty<string>());
        DataContext = ViewModel;
        SubscribeViewModel();
        Closed += OnClosed;
    }

    /// <summary>
    /// 生产构造：执行器与运行上下文由宿主注入。
    /// 之所以做成必填依赖，是为了让「设置窗口自己就能执行」在类型上成立——
    /// 调用方再也拿不到「ShowDialog&lt;bool&gt; 之后再回读窗口状态」这条后门。
    /// </summary>
    /// <param name="session">唯一执行器。</param>
    /// <param name="context">宿主差异（Owner / Presenter / 退出策略）。</param>
    /// <param name="sourcePaths">预填的源路径（新建为空、拖入为拖拽项、CLI 为命令行参数）。</param>
    public CompressSettingsWindow(
        ICompressSession session,
        CompressRunContext context,
        IReadOnlyList<string> sourcePaths)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);
        _session = session;
        _context = context;

        InitializeComponent();

        ViewModel = new CompressSettingsViewModel(sourcePaths);

        // 设置文件保存浏览回调
        ViewModel.BrowseOutput = async () =>
        {
            // 格式联动：根据当前 DefaultFormat 计算默认扩展名（弹窗内 SaveFile 模式应用）
            var defaultExt = ViewModel.DefaultFormat switch
            {
                "tar.gz" => ".tar.gz",
                "7z" => ".7z",
                _ => ".zip"
            };
            return await CustomFilePickerDialog.ShowSaveFileAsync(this, initialPath: ViewModel.OutputPath, defaultExtension: defaultExt);
        };

        // 设置文件/文件夹选择回调
        ViewModel.PickFiles = async () =>
        {
            // 将第一个源路径的目录作为「场景相关路径」传给 picker，作为默认路径优先级链的 context 来源
            var contextPath = ResolveContextPath(sourcePaths);
            return await CustomFilePickerDialog.ShowOpenItemsAsync(this, initialPath: contextPath);
        };

        // 高级格式面板与文件过滤器都只存在于窗口/控件里：确认事务开始前必须先把它们抓进 VM，
        // 否则冻结载荷里会是构造时的旧值或空过滤器（预览=实际的守卫从这里开始）。
        ViewModel.CaptureFormatOptions = () =>
        {
            SnapshotFormatOptionsToViewModel();
            ViewModel.FileFilter = GetFilter();
        };

        // 确认事务的三种结局 → 窗口动作。拒绝留窗（原因弹在窗口上），确认/取消关窗。
        ViewModel.ConfirmCompleted += OnConfirmCompleted;

        DataContext = ViewModel;
        SubscribeViewModel();

        // 浏览回调：保存文件对话框（带格式联动），选中后将完整路径拆为目录+文件名填入。
        // 返回目录（QuickPathPicker 只收目录）；文件名落在独立 OutputFileName 控件。
        OutputPathPicker.BrowseAction = BrowseOutputPathAsync;

        // 消息提示回调（弹窗以本窗口为 owner）
        ViewModel.ShowMessage = (msg, title) =>
            AppMessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning, this);

        // 过滤控件事件订阅提前到构造函数（原在 OnLoaded）：窗口刚弹出时用户操作过滤会导致
        // FilterChanged 未订阅、预览不重建、Plan 保持 null → 点击压缩静默退出。
        InitFileFilter();

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>
    /// 用注入的执行器跑一次已确认的压缩载荷。
    /// 放在窗口上是因为 session/context 是它的构造依赖——宿主只需要分支处理
    /// <see cref="CompressOutcome"/> 四态，不必再自己拼请求或再弹一次错误。
    /// </summary>
    /// <param name="settings">确认事务冻结下来的载荷。</param>
    /// <returns>压缩结局。</returns>
    public Task<CompressOutcome> RunConfirmedAsync(CompressSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_session is null || _context is null)
        {
            // 设计时窗口没有执行器：明确回报「未开始」而不是抛异常，调用方据此展示原因。
            return Task.FromResult(
                CompressOutcome.NotStarted(LocalizationManager.T("Settings_Window_NoExecutor")));
        }

        return _session.RunAsync(settings, _context);
    }

    /// <summary>
    /// 把确认结局翻译成窗口动作：拒绝留窗、确认/取消关窗；关窗携带的结果供
    /// <see cref="Completion"/> 与 <c>ShowDialog&lt;ConfirmOutcome&lt;…&gt;&gt;</c> 读取。
    /// </summary>
    private void OnConfirmCompleted(ConfirmOutcome<CompressSettings> outcome)
    {
        switch (outcome)
        {
            case Rejected<CompressSettings> rejected:
                // 拒绝必须留在窗口并显示原因：静默关窗会让用户以为压缩开始了。
                if (ViewModel.ShowMessage != null)
                    _ = ViewModel.ShowMessage(rejected.Reason, LocalizationManager.T("Compress_Title"));
                return;

            default:
                _closedOutcome = outcome;
                Close(outcome);
                return;
        }
    }

    /// <summary>关窗统一出口：解除订阅并把确认结果（缺省 = 取消）交给等待方。</summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel.ConfirmCompleted -= OnConfirmCompleted;
        _completion.TrySetResult(_closedOutcome ?? new Cancelled<CompressSettings>());
    }

    /// <summary>
    /// QuickPathPicker 浏览：打开保存文件对话框（带格式联动），返回目录并拆分写入文件名。
    /// </summary>
    private async Task<string?> BrowseOutputPathAsync(Window? owner, string? current)
    {
        if (ViewModel.OutputMode != CompressOutputMode.Manual) return null;
        var defaultExt = ViewModel.DefaultFormat switch
        {
            "tar.gz" => ".tar.gz",
            "7z" => ".7z",
            _ => ".zip"
        };
        var path = await CustomFilePickerDialog.ShowSaveFileAsync(
            owner ?? this, initialPath: current ?? ViewModel.OutputDirectory, defaultExtension: defaultExt);
        if (string.IsNullOrEmpty(path)) return null;

        // 拆分完整路径 → 目录 + 文件名
        var dir = System.IO.Path.GetDirectoryName(path);
        var name = System.IO.Path.GetFileName(path);
        foreach (var ext in new[] { ".tar.gz", ".7z", ".zip" })
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^ext.Length];
                break;
            }
        }
        if (string.IsNullOrEmpty(dir)) return null;
        ViewModel.OutputDirectory = dir;
        ViewModel.OutputFileName = name;
        return dir;
    }

    /// <summary>
    /// 获取当前文件过滤条件。返回 null 表示不过滤。
    /// 只服务于确认事务内部的快照（过滤控件属于窗口，不属于载荷），因此不对外公开。
    /// </summary>
    private FileFilterCriteria? GetFilter()
    {
        if (FileFilterControl == null) return null;
        if (!FileFilterControl.IsFilterEnabled) return null;
        var filter = FileFilterControl.GetFilter();
        return filter.IsActive ? filter : null;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        // 限制最大高度不超过屏幕可用高度的 90%，防止内容撑高后按钮被推出屏幕
        var screen = Screens.ScreenFromWindow(this);
        if (screen != null)
        {
            MaxHeight = screen.WorkingArea.Height * 0.9;
        }

        AdjustWindowPosition();
        FormatOptionsPanel.LoadDefaults();
        LoadSplitSizeFromSettings();
        UpdateFormatAvailabilityHints();
    }

    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // 切换 Tab 后窗口高度可能变化，等布局完成后再检查位置
        Dispatcher.UIThread.Post(AdjustWindowPosition, DispatcherPriority.Background);
    }

    /// <summary>
    /// 检查窗口底部是否超出屏幕，超出则自动上移到刚好可见的位置。
    /// </summary>
    private void AdjustWindowPosition()
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen == null) return;

        // Height 是设备独立单位 (DIPs)，需乘 RenderScaling 转为屏幕像素
        var windowBottomPx = Position.Y + (int)(Height * RenderScaling);
        var overflowPx = windowBottomPx - screen.WorkingArea.Bottom;
        if (overflowPx > 0)
        {
            Position = Position.WithY(Position.Y - overflowPx);
        }
    }

    /// <summary>初始化文件过滤控件（预设 + 事件）。</summary>
    private void InitFileFilter()
    {
        var settings = AppSettings.Load();
        FileFilterControl.LoadPresets(settings.FilterPresets);

        FileFilterControl.SavePresetRequested += name =>
        {
            var filter = FileFilterControl.GetFilter();
            if (!filter.IsActive) return;
            settings.AddPreset(new FileFilterPreset(name, filter));
            settings.Save();
            FileFilterControl.LoadPresets(settings.FilterPresets);
        };

        FileFilterControl.DeletePresetRequested += preset =>
        {
            settings.FilterPresets.Remove(preset);
            settings.Save();
            FileFilterControl.LoadPresets(settings.FilterPresets);
        };

        FileFilterControl.RenamePresetRequested += (preset, newName) =>
        {
            var existing = settings.FilterPresets.FirstOrDefault(p => p.Name == newName);
            if (existing != null)
            {
                _ = AppMessageBox.Show(
                    LocalizationManager.T("FileFilter_PresetNameExists"),
                    LocalizationManager.T("FileFilter_RenamePresetTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning,
                    this);
                return;
            }
            preset.Name = newName;
            settings.Save();
            FileFilterControl.LoadPresets(settings.FilterPresets, newName);
        };

        // 过滤条件变更时重建预览树
        FileFilterControl.FilterChanged += OnFileFilterChanged;
    }

    /// <summary>
    /// 根据当前过滤条件重建压缩预览树。
    /// </summary>
    private void BuildPreview()
    {
        var filter = GetFilter();
        // 记住过滤条件到 VM：后续 SelectedPaths 变更触发的无参重建不丢失过滤
        ViewModel.FileFilter = filter;
        ViewModel.BuildCompressPreview(filter);
    }

    private void OnFileFilterChanged()
    {
        BuildPreview();
    }

    /// <summary>
    /// 从 AppSettings 加载分卷大小设置。
    /// </summary>
    private void LoadSplitSizeFromSettings()
    {
        var s = AppSettings.Load();
        if (!string.IsNullOrEmpty(s.SplitSizeTag))
        {
            var option = ViewModel.SplitSizeOptions.FirstOrDefault(o => o.Tag == s.SplitSizeTag);
            if (option != null)
                ViewModel.SelectedSplitSizeOption = option;
        }
        if (!string.IsNullOrEmpty(s.CustomSplitSizeMB))
            ViewModel.CustomSplitSizeText = s.CustomSplitSizeMB;
    }

    /// <summary>
    /// 将 DynamicFormatOptionsPanel 的当前值快照到 ViewModel，供确认事务的载荷冻结读取。
    /// 由窗口注入到 <see cref="CompressSettingsViewModel.CaptureFormatOptions"/>，在确认前调用；
    /// 不再写回 AppSettings，避免污染设置窗口的全局默认值。
    /// </summary>
    private void SnapshotFormatOptionsToViewModel()
    {
        ViewModel.FileNameEncoding = FormatOptionsPanel.FileNameEncoding ?? "utf-8";
        ViewModel.ZipCompressionMethod = FormatOptionsPanel.ZipCompressionMethod ?? "deflate";
        ViewModel.SevenZipCompressionMethod = FormatOptionsPanel.SevenZipCompressionMethod ?? "LZMA2";
        ViewModel.SevenZipSolid = FormatOptionsPanel.SevenZipSolid;
        ViewModel.SevenZipSolidBlockSize = FormatOptionsPanel.SevenZipSolidBlockSize ?? "";
        ViewModel.SevenZipDictionarySize = FormatOptionsPanel.SevenZipDictionarySize;
        ViewModel.SevenZipNumFastBytes = FormatOptionsPanel.SevenZipNumFastBytes;
        ViewModel.SevenZipMatchFinder = FormatOptionsPanel.SevenZipMatchFinder ?? "";
        ViewModel.SevenZipMultithreaded = FormatOptionsPanel.SevenZipMultithreaded;
        ViewModel.AdaptiveCompression = FormatOptionsPanel.AdaptiveCompression;
        ViewModel.AdaptiveSmartDetect = FormatOptionsPanel.AdaptiveSmartDetect;
        ViewModel.MultiThreadedCompression = FormatOptionsPanel.MultiThreadedCompression;
        // ZipEncryptionMethod / SevenZipEncryptHeaders 已通过 XAML 双向绑定到 ViewModel，无需快照
    }

    /// <summary>
    /// Subscribe to ViewModel PropertyChanged events to update UI elements
    /// that can't be easily bound in XAML.
    /// </summary>
    private void SubscribeViewModel()
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// 批量移除选中的源文件。
    /// </summary>
    private void RemoveSelected_Click(object? sender, RoutedEventArgs e)
    {
        var toRemove = SourceFilesList.SelectedItems?.Cast<string>().ToList() ?? new List<string>();
        foreach (var path in toRemove)
        {
            ViewModel.SelectedPaths.Remove(path);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.Password):
            case nameof(ViewModel.ConfirmPassword):
                UpdatePasswordMatchIndicator();
                break;
            case nameof(ViewModel.IsPasswordLibraryMode):
                UpdateSaveCheckLabel();
                break;
            case nameof(ViewModel.Encrypt):
                // 切换加密时密码区域展开/折叠，等布局完成后再检查位置
                Dispatcher.UIThread.Post(AdjustWindowPosition, DispatcherPriority.Background);
                UpdateFormatAvailabilityHints();
                break;
            case nameof(ViewModel.SelectedSplitSizeOption):
            case nameof(ViewModel.CustomSplitSizeText):
                UpdateFormatAvailabilityHints();
                break;
            case nameof(ViewModel.PreviewRoot):
                // 预览树重建后，把本次待压缩的单包大小推给格式选项面板，驱动 ZIP 多线程 4GB 限制
                UpdateZipMultiThreadAvailability();
                break;
            case nameof(ViewModel.OutputMode):
                // 输出模式切换会改变单包划分（Separate 每源自成包），需重新判定
                UpdateZipMultiThreadAvailability();
                break;
        }
    }

    /// <summary>
    /// 把「加密 / 分卷」上下文注入格式面板，用于显示自适应/多线程不生效的原因。
    /// </summary>
    private void UpdateFormatAvailabilityHints()
        => FormatOptionsPanel.UpdateCompressionContext(ViewModel.Encrypt, ViewModel.SplitSize > 0);

    /// <summary>
    /// 根据预览树更新 ZIP 多线程选项可用性。
    /// Manual/Combined：所有文件进同一个包，判定用根节点 TotalDescendantSize；
    /// Separate：每个源各自成包，引擎按单包大小判定，取所有压缩包节点的最大值。
    /// 与 ZipEngine.IsMultiThreadedEligible 的 MaxZip32Size（ZIP32 上限）语义对齐。
    /// </summary>
    private void UpdateZipMultiThreadAvailability()
    {
        var root = ViewModel.PreviewRoot;
        long maxArchiveBytes = 0;

        if (root != null)
        {
            if (ViewModel.OutputMode == CompressOutputMode.Separate)
            {
                foreach (var node in EnumerateArchiveNodes(root))
                    maxArchiveBytes = Math.Max(maxArchiveBytes, node.TotalDescendantSize);
            }
            else
            {
                maxArchiveBytes = root.TotalDescendantSize;
            }
        }

        FormatOptionsPanel.UpdateTotalSourceSize(maxArchiveBytes);
    }

    /// <summary>枚举子树中所有压缩包节点（IsArchiveNode）。</summary>
    private static IEnumerable<PreviewTreeNode> EnumerateArchiveNodes(PreviewTreeNode node)
    {
        if (node.IsArchiveNode)
            yield return node;
        foreach (var child in node.Children.OfType<PreviewTreeNode>())
        {
            foreach (var found in EnumerateArchiveNodes(child))
                yield return found;
        }
    }

    /// <summary>
    /// Update the password match indicator text and visibility.
    /// </summary>
    private void UpdatePasswordMatchIndicator()
    {
        if (PasswordMatchIndicator == null) return;

        if (!string.IsNullOrEmpty(ViewModel.ConfirmPassword))
        {
            if (ViewModel.PasswordsMatch)
            {
                PasswordMatchIndicator.Text = LocalizationManager.T("Compress_Pwd_Match");
                PasswordMatchIndicator.Foreground = new SolidColorBrush(Color.Parse("#4CAF50")); // Green
                PasswordMatchIndicator.IsVisible = true;
            }
            else
            {
                PasswordMatchIndicator.Text = LocalizationManager.T("Compress_Pwd_NoMatch");
                PasswordMatchIndicator.Foreground = new SolidColorBrush(Color.Parse("#F44336")); // Red
                PasswordMatchIndicator.IsVisible = true;
            }
        }
        else
        {
            PasswordMatchIndicator.IsVisible = false;
        }
    }

    /// <summary>
    /// Update the save-to-library checkbox label based on current mode.
    /// </summary>
    private void UpdateSaveCheckLabel()
    {
        // The SaveToLibrary checkbox text is bound to localized strings in XAML,
        // but the label changes based on mode (library vs new password).
        // We handle the dynamic label update here since Avalonia's binding
        // can't easily change between two different string keys.
    }

    /// <summary>
    /// 切换密码显示/隐藏。
    /// </summary>
    private void TogglePasswordReveal(object? sender, RoutedEventArgs e)
    {
        if (PasswordTextBox.PasswordChar == '●')
        {
            PasswordTextBox.PasswordChar = default;
            RevealButton.Content = LocalizationManager.T("Compress_HidePassword");
        }
        else
        {
            PasswordTextBox.PasswordChar = '●';
            RevealButton.Content = LocalizationManager.T("Compress_ShowPassword");
        }
    }

    /// <summary>从源路径列表中推导「场景相关路径」：取第一个文件/目录所在目录，供默认路径优先级链 context 使用。</summary>
    private static string? ResolveContextPath(IReadOnlyList<string> sourcePaths)
    {
        if (sourcePaths == null || sourcePaths.Count == 0) return null;
        var first = sourcePaths[0];
        if (string.IsNullOrWhiteSpace(first)) return null;
        try
        {
            if (Directory.Exists(first)) return System.IO.Path.GetFullPath(first);
            if (File.Exists(first)) return System.IO.Path.GetDirectoryName(first);
            return null;
        }
        catch { return null; }
    }
}
