using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using Microsoft.Win32;
using System.Text.Json;
using System.Text;
using Markdig;
using Ude;
using MantisZip.UI.Localization;
using WpfAnimatedGif;

namespace MantisZip.UI;

/// <summary>
/// MainWindow.xaml 的交互逻辑
/// </summary>
public partial class MainWindow : Window
{
    private string? _currentArchivePath;
    private ArchiveFormat _currentFormat;
    private string? _currentPassword;  // 当前压缩包的密码（打开时自动匹配）
    private string? _currentPasswordDescription; // 匹配的密码描述
    private List<string>? _currentPasswordPatterns; // 匹配的密码规则
    private bool _hasEncryptedArchive; // 当前压缩包是否有加密条目
    private string? _archiveComment;   // 压缩包注释（仅 ZIP 格式支持）
    private List<ArchiveItem> _allItems = new();  // 存储所有文件项
    private readonly RecentFileManager _recentFileManager = new();
    private readonly Dictionary<string, (int Count, long Size, long CompressedSize)> _dirStats = new(); // 目录统计缓存
    private string _currentFolder = "";  // 当前目录
    private string? _previewTempDir;        // L.T(L.Settings_Tab_Preview)临时目录
    private readonly Dictionary<int, double> _lastPreviewSizes = new()
    {
        { 1, 341 }, { 2, 416 }, { 3, 479 }, { 4, 678 }
    }; // 每个位置独立记忆大小（高度:位置1/2/3, 宽度:位置4）
    private int _lastAppliedPosition = 1;    // 上次应用的布局位置，用于检测变更
    private bool _isProgrammaticFilter;      // 编程触发的 FilterFiles，应跳过 SelectionChanged 预览
    private bool _showSubfolders;            // 是否展开所有子目录文件（扁平视图）
    private bool _rebasedBaseline;           // 比例基准是否使用筛选后列表
    private List<ArchiveItem>? _currentUnfilteredItems; // FilterFiles 处理后的完整（未过滤）列表，供 RefreshFilter 读取
    private string? _searchText;              // 当前文字搜索词
    private DateTime? _dateFrom;              // 日期范围开始
    private DateTime? _dateTo;                // 日期范围结束
    private long? _sizeMin;                   // 大小下限（字节）
    private long? _sizeMax;                   // 大小上限（字节）
    private string? _excludeText;              // 排除文字
    private FilterMatchMode _matchMode;        // 匹配模式
    private string? _savedSortColumnPath;    // 持久化的排序列 SortMemberPath
    private int _savedSortDirection;         // 持久化的排序方向 (0=无, 1=升, 2=降)
    private bool _previewPanelEnabled = true; // 工具栏预览开关状态
    private Point _dragStartPoint;           // 文件列表拖拽起点
    private string? _dragTempDir;            // 拖拽提取临时目录
    private bool _isOwnDrag;                 // 当前拖拽是否来自本窗口
    private bool _isDragExtracting;          // 文件列表拖出提取中，防止重入
    private List<ArchiveItem>? _dragPreservedSelection; // 保存点按前的多选集（DataGrid 处理后会被清空）
    private CancellationTokenSource? _previewCts; // 预览取消令牌
    private bool _transparentBgEnabled;
    private BitmapSource? _originalPreviewImage; // 原始图片缓存，用于恢复
    private bool _flattenAlphaEnabled;           // 是否抛弃了透明
    private List<(BitmapSource frame, int w, int h)>? _icoOriginalFrames; // ICO 画廊原始帧
    private List<Image>? _icoImages;              // ICO 画廊 Image 控件引用
    private List<Border>? _icoBorders;            // ICO 画廊 Border 控件引用
    private ImageAnimationController? _gifController;
    private TextBox? _gifFrameInput;
    private TextBlock? _gifFrameTotal;

    public MainWindow()
    {
        InitializeComponent();

        // 列标题右键菜单（切换列显隐）
        if (Resources["ColumnHeaderContextMenu"] is ContextMenu headerMenu)
            headerMenu.Opened += ColumnHeaderContextMenu_Opened;

        // 进度条整体显隐：图标透明度与 IsChecked 同步
        if (ShowProgressBarsMenu.Icon is Emoji.Wpf.TextBlock progIcon)
        {
            ShowProgressBarsMenu.IsChecked = AppSettings.Instance.ShowProgressBars;
            progIcon.Opacity = ShowProgressBarsMenu.IsChecked ? 1.0 : 0.2;
        }

        // 目录独立基准菜单项：图标透明度与 IsChecked 同步（与列标题右键菜单风格一致）
        if (SepDirBaselineMenu.Icon is Emoji.Wpf.TextBlock sepIcon)
        {
            SepDirBaselineMenu.IsChecked = AppSettings.Instance.SeparateDirBaseline;
            sepIcon.Opacity = SepDirBaselineMenu.IsChecked ? 1.0 : 0.2;
        }

        LoadWindowSettings();
        _recentFileManager.Load();
        RecentFilesMenu.SubmenuOpened += RecentFilesMenu_SubmenuOpened;
        // WPF 需要至少一个子项才渲染为子菜单（有下拉箭头），SubmenuOpened 时会清空重建
        RecentFilesMenu.Items.Add(new MenuItem { Visibility = Visibility.Collapsed });
        ApplyPreviewPosition(AppSettings.Instance.PreviewPosition);
        ApplyInfoPanelOrientation(AppSettings.Instance.InfoPanelOrientation);
        _previewPanelEnabled = AppSettings.Instance.ShowPreviewPanel;
        if (PreviewToggleMenu.Icon is Emoji.Wpf.TextBlock previewIcon)
        {
            PreviewToggleMenu.IsChecked = _previewPanelEnabled;
            previewIcon.Opacity = _previewPanelEnabled ? 1.0 : 0.2;
        }
        if (!_previewPanelEnabled)
            PreviewPanel.Visibility = Visibility.Collapsed;
        Activated += MainWindow_Activated;
        Loaded += async (_, _) =>
        {
            try { await EnsureWebView2InitializedAsync(); }
            catch (Exception ex) { App.LogDebug("WebView2 pre-init failed: {0}", ex.Message); }
        };
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        var currentPos = AppSettings.Instance.PreviewPosition;
        if (currentPos != _lastAppliedPosition)
        {
            _lastAppliedPosition = currentPos;
            if (PreviewPanel.Visibility == Visibility.Visible)
            {
                ShowPreviewPanel();
            }
            else
            {
                ApplyPreviewPosition(currentPos);
            }
        }
        // 始终刷新信息面板方向（可能从 SettingsWindow 更改）
        ApplyInfoPanelOrientation(AppSettings.Instance.InfoPanelOrientation);
    }

    #region 窗口大小持久化

    private string GetWindowConfigPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(localAppData, "MantisZip");
        return Path.Combine(folder, "window.json");
    }

    private class ColumnState
    {
        /// <summary>列身份标识：SortMemberPath 或 Tag</summary>
        public string? ColumnId { get; set; }
        public double Width { get; set; }
        public bool Visible { get; set; }
        public int DisplayIndex { get; set; }
    }

    private class WindowSize
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double TreeColumnWidth { get; set; }
        public double PreviewRowHeight { get; set; }
        public double PreviewColumnWidth { get; set; }
        public double PreviewTreeHeight { get; set; }   // 位置2
        public double PreviewFilesHeight { get; set; }  // 位置3
        public List<ColumnState> ColumnStates { get; set; } = new();
        public string? SortColumnPath { get; set; }
        public int SortDirection { get; set; }  // 0=无, 1=升序, 2=降序
    }

    private void LoadWindowSettings()
    {
        try
        {
            var configPath = GetWindowConfigPath();
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var obj = JsonSerializer.Deserialize<WindowSize>(json);
                if (obj?.Width > 0 && obj?.Height > 0)
                {
                    Width = obj.Width;
                    Height = obj.Height;
                }

                // 恢复目录树列宽度
                if (obj?.TreeColumnWidth > 0)
                {
                    var treeGrid = FolderTree.Parent as Grid;
                    if (treeGrid?.ColumnDefinitions.Count > 0)
                    {
                        treeGrid.ColumnDefinitions[0].Width = new GridLength(obj.TreeColumnWidth);
                    }
                }

                // 恢复各位置的预览大小
                if (obj != null)
                {
                    if (obj.PreviewRowHeight > 0) _lastPreviewSizes[1] = obj.PreviewRowHeight;
                    if (obj.PreviewTreeHeight > 0) _lastPreviewSizes[2] = obj.PreviewTreeHeight;
                    if (obj.PreviewFilesHeight > 0) _lastPreviewSizes[3] = obj.PreviewFilesHeight;
                    if (obj.PreviewColumnWidth > 0) _lastPreviewSizes[4] = obj.PreviewColumnWidth;
                }

                // 恢复各列的宽度、可见性和顺序
                if (obj?.ColumnStates != null && obj.ColumnStates.Count > 0)
                {
                    // 构建身份→列的字典
                    var columnDict = new Dictionary<string, DataGridColumn>();
                    foreach (var col in FileListGrid.Columns)
                    {
                        var id = GetColumnId(col);
                        if (id != null) columnDict[id] = col;
                    }

                    // 先按保存的 DisplayIndex 排序，避免设置时冲突
                    var ordered = obj.ColumnStates
                        .Where(s => s.ColumnId != null)
                        .OrderBy(s => s.DisplayIndex)
                        .ToList();

                    // 如果所有 ColumnId 都为空（旧版 JSON），回退到位置索引匹配
                    if (ordered.Count == 0)
                    {
                        for (int i = 0; i < FileListGrid.Columns.Count && i < obj.ColumnStates.Count; i++)
                        {
                            var col = FileListGrid.Columns[i];
                            var state = obj.ColumnStates[i];
                            if (state.Width > 0) col.Width = new DataGridLength(state.Width);
                            if (i > 0) col.Visibility = state.Visible ? Visibility.Visible : Visibility.Collapsed;
                        }
                    }
                    else
                    {
                        // 新版：按 ColumnId 匹配加载
                        foreach (var state in ordered)
                        {
                            if (state.ColumnId == null || !columnDict.TryGetValue(state.ColumnId, out var col))
                                continue;

                            if (state.Width > 0)
                                col.Width = new DataGridLength(state.Width);

                            // 跳过名称列（不允许隐藏）
                            if (state.ColumnId != "Name")
                                col.Visibility = state.Visible ? Visibility.Visible : Visibility.Collapsed;

                            col.DisplayIndex = state.DisplayIndex;
                        }
                    }
                }

                // 记住排序列和方向，FilterFiles 设置 ItemsSource 后再应用
                _savedSortColumnPath = obj?.SortColumnPath;
                _savedSortDirection = obj?.SortDirection ?? 0;
                App.LogDebug("LoadWindowSettings: sortPath={0}, sortDir={1}",
                    _savedSortColumnPath ?? "(null)", _savedSortDirection);
            }
        }
        catch (Exception ex)
        {
            App.LogDebug("LoadWindowConfig: failed to read config: {0}", ex.Message);
        }
    }

    private void SaveWindowSettings()
    {
        try
        {
            var configPath = GetWindowConfigPath();
            var dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 保存目录树列宽
            double treeWidth = 200;
            var treeGrid = FolderTree.Parent as Grid;
            if (treeGrid?.ColumnDefinitions.Count > 0)
            {
                var col = treeGrid.ColumnDefinitions[0];
                if (col.Width.GridUnitType == GridUnitType.Pixel)
                {
                    treeWidth = col.Width.Value;
                }
            }

            // 保存预览大小（从 per-position 字典读取）
            double previewHeight = _lastPreviewSizes[1];
            double previewTreeHeight = _lastPreviewSizes[2];
            double previewFilesHeight = _lastPreviewSizes[3];
            double previewColumnWidth = _lastPreviewSizes[4];

            // 保存各列的宽度、可见性、顺序（按列身份匹配，而非位置索引）
            var columnStates = new List<ColumnState>();
            foreach (var col in FileListGrid.Columns)
            {
                columnStates.Add(new ColumnState
                {
                    ColumnId = GetColumnId(col),
                    Width = col.Width.Value,
                    Visible = col.Visibility == Visibility.Visible,
                    DisplayIndex = col.DisplayIndex
                });
            }

            // 保存当前排序列和方向
            string? sortColumnPath = null;
            int sortDirection = 0;
            foreach (var col in FileListGrid.Columns)
            {
                if (col.SortDirection.HasValue)
                {
                    sortColumnPath = col.SortMemberPath;
                    sortDirection = col.SortDirection.Value == ListSortDirection.Ascending ? 1 : 2;
                    break;
                }
            }
            App.LogDebug("SaveWindowSettings: sortPath={0}, sortDir={1}",
                sortColumnPath ?? "(null)", sortDirection);

            var obj = new WindowSize
            {
                Width = Width,
                Height = Height,
                TreeColumnWidth = treeWidth,
                PreviewRowHeight = previewHeight,
                PreviewColumnWidth = previewColumnWidth,
                PreviewTreeHeight = previewTreeHeight,
                PreviewFilesHeight = previewFilesHeight,
                ColumnStates = columnStates,
                SortColumnPath = sortColumnPath,
                SortDirection = sortDirection
            };
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
        }
        catch (Exception ex)
        {
            App.LogDebug("SaveWindowConfig: failed to save: {0}", ex.Message);
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveWindowSettings();
        // 释放预览 CancellationTokenSource
        if (_previewCts != null)
        {
            _previewCts.Cancel();
            _previewCts.Dispose();
            _previewCts = null;
        }
    }

#endregion

// Drag & drop moved to MainWindow.DragDrop.cs
// Menu moved to MainWindow.Menu.cs

// Menu event handlers moved to MainWindow.Menu.cs

// Context menu moved to MainWindow.Menu.cs

    #region 列排序持久化

    /// <summary>
    /// 获取列的唯一标识：优先用 SortMemberPath，模板列用 Tag
    /// </summary>
    private static string? GetColumnId(DataGridColumn col)
    {
        return col.SortMemberPath;
    }

    /// <summary>
    /// 从 DataGrid 当前状态捕获排序列和方向到实例字段
    /// </summary>
    private void CaptureCurrentSort()
    {
        foreach (var col in FileListGrid.Columns)
        {
            if (col.SortDirection.HasValue)
            {
                _savedSortColumnPath = col.SortMemberPath;
                _savedSortDirection = col.SortDirection.Value == ListSortDirection.Ascending ? 1 : 2;
                App.LogDebug("CaptureCurrentSort: captured col={0}, path={1}, dir={2}",
                    col.Header?.ToString()?.TrimEnd('▲', '▼', ' ').TrimEnd(),
                    _savedSortColumnPath ?? "(null)",
                    _savedSortDirection);
                return;
            }
        }
        // 当前没有排序列 → 不覆盖 _savedSortColumnPath（保留从 JSON 加载或之前捕获的值）
    }

    /// <summary>
    /// 在设置新的 ItemsSource 后重新应用排序
    /// </summary>
    private void ApplySavedSort()
    {
        if (string.IsNullOrEmpty(_savedSortColumnPath) || _savedSortDirection <= 0)
        {
            App.LogDebug("ApplySavedSort: skipped (path={0}, dir={1})",
                _savedSortColumnPath ?? "(null)", _savedSortDirection);
            return;
        }

        var sortCol = FileListGrid.Columns.FirstOrDefault(c => c.SortMemberPath == _savedSortColumnPath);
        if (sortCol == null)
        {
            App.LogDebug("ApplySavedSort: column not found for path={0}", _savedSortColumnPath);
            return;
        }

        var direction = _savedSortDirection == 1
            ? ListSortDirection.Ascending
            : ListSortDirection.Descending;

        App.LogDebug("ApplySavedSort: applying path={0}, dir={1}", _savedSortColumnPath, _savedSortDirection);

        // 通过 CollectionView 排序（WPF 官方推荐方式）
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(FileListGrid.ItemsSource);
        if (view != null)
        {
            view.SortDescriptions.Clear();
            // 目录独立基准开启时：第一排序键为目录/文件分离
            if (AppSettings.Instance.SeparateDirBaseline)
            {
                view.SortDescriptions.Add(
                    new System.ComponentModel.SortDescription("SortOrder", ListSortDirection.Ascending));
            }
            view.SortDescriptions.Add(
                new System.ComponentModel.SortDescription(sortCol.SortMemberPath, direction));
            App.LogDebug("ApplySavedSort: view type={0}, SortDescriptions count={1}",
                view.GetType().Name, view.SortDescriptions.Count);
        }

        sortCol.SortDirection = direction;

        if (sortCol.Header is string header)
        {
            var clean = header.TrimEnd('▲', '▼', ' ').TrimEnd();
            sortCol.Header = clean + (direction == ListSortDirection.Ascending ? " ▲" : " ▼");
        }
    }

    #endregion

    #region 核心功能

    private bool IsArchiveFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".zip" or ".7z" or ".rar" or ".tar" or ".tgz" or ".gz" or ".iso";
    }

    private static ArchiveFormat GetFormatByExtension(string path) =>
        ArchiveEngineFactory.GetFormatByExtension(path);

    /// <summary>
    /// 根据压缩包路径和格式返回压缩后大小的显示模式。
    /// .tar 和 .iso 是不压缩的格式（显示实际大小，100%）。
    /// .7z/.rar 和 .tgz/.tar.gz/.gz 是有压缩但无法获得逐项压缩后大小的格式（显示 ---）。
    /// .zip 是正常模式（显示实际压缩后大小）。
    /// </summary>
    private static ArchiveItem.CompressedDisplayMode GetCompressedDisplayMode(string archivePath, ArchiveFormat format)
    {
        if (format == ArchiveFormat.Zip)
            return ArchiveItem.CompressedDisplayMode.Normal;
        if (format == ArchiveFormat.Iso)
            return ArchiveItem.CompressedDisplayMode.NotCompressed;
        if (format == ArchiveFormat.Tar)
        {
            // .tar 是未压缩的容器格式，.tgz/.tar.gz/.gz 是有压缩的
            var ext = Path.GetExtension(archivePath).ToLowerInvariant();
            if (ext == ".tar")
                return ArchiveItem.CompressedDisplayMode.NotCompressed;
            // .tgz / .gz / .tar.gz（已在上层被映射为 Tar）
            return ArchiveItem.CompressedDisplayMode.Unavailable;
        }
        // 7z, RAR
        return ArchiveItem.CompressedDisplayMode.Unavailable;
    }

    /// <summary>
    /// 读取压缩包注释（仅 ZIP 格式支持，通过 ZipCommentHelper 读取 EOCD 注释字段）。
    /// 其他格式返回 null。
    /// </summary>
    private static string? ReadArchiveComment(string archivePath, ArchiveFormat format)
    {
        if (format != ArchiveFormat.Zip) return null;

        try
        {
            var comment = ZipCommentHelper.ReadComment(archivePath);
            return string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        }
        catch (Exception ex)
        {
            App.LogDebug("ReadArchiveComment: failed to read comment: {0}", ex.Message);
            return null;
        }
    }

    internal async Task LoadArchiveAsync(string archivePath)
    {
        App.TraceLog("LoadArchiveAsync: entering archivePath={0}", archivePath);
        try
        {
            ClearPreviewTemp();
            ClearPreviewContent();

            // 清空状态栏统计
            DirStatsText.Text = "";
            SelectionStatsText.Text = "";
            ArchiveStatsText.Text = "";

            // 重置过滤状态
            _searchText = null;
            _dateFrom = null;
            _dateTo = null;
            _sizeMin = null;
            _sizeMax = null;
            _showSubfolders = false;
            ShowSubfoldersBtn.IsChecked = false;
            UpdateShowSubfoldersBtnToolTip();
            ToggleFilterBarBtn.IsChecked = false;
            FilterBar.Visibility = Visibility.Collapsed;
            _rebasedBaseline = false;
            RebaseBaselineBtn.IsChecked = false;
            // 清空过滤输入控件
            FileSearchBox.Text = "";
            DateFromPicker.SelectedDate = null;
            DateToPicker.SelectedDate = null;
            SizeMinBox.Text = "";
            SizeMaxBox.Text = "";
            SizeMinUnit.SelectedIndex = 0;
            SizeMaxUnit.SelectedIndex = 0;

            SetStatus(L.T(L.Main_Status_Loading));
            _currentArchivePath = archivePath;

            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            if (engine == null)
            {
                AppMessageBox.Show(L.T(L.Main_DragFormatUnsupported), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus(L.T(L.Main_Status_Ready));
                return;
            }

            // Show loading overlay before potentially slow ListEntriesAsync
            FileListPanel.Visibility = Visibility.Visible;
            ArchiveLoadingOverlay.Visibility = Visibility.Visible;
            ArchiveLoadingBar.IsIndeterminate = true;
            ArchiveLoadingText.Text = L.T(L.Main_Status_Loading);
            ArchiveLoadingPercent.Text = "";
            DropHint.Visibility = Visibility.Collapsed;

            _currentFormat = GetFormatByExtension(archivePath);
            _archiveComment = ReadArchiveComment(archivePath, _currentFormat);
            var compressedDisplay = GetCompressedDisplayMode(archivePath, _currentFormat);

            var items = await engine.ListEntriesAsync(archivePath);

            // Update overlay to show entry count
            ArchiveLoadingText.Text = L.TF(L.Main_Status_ProcessingEntries, items.Count);

            // 检测加密条目 → 自动尝试匹配已保存的密码
            _currentPassword = null;
            _currentPasswordDescription = null;
            _currentPasswordPatterns = null;
            _hasEncryptedArchive = items.Any(i => i.IsEncrypted);
            if (_hasEncryptedArchive)
            {
                var match = App.TryMatchPassword(archivePath, engine, null, false, out var limitReached);
                if (match != null)
                {
                    _currentPassword = match.Value.Password;
                    _currentPasswordDescription = match.Value.Description;
                    // 从密码库补全 patterns
                    var matchedEntry = PasswordManager.Instance.FindMatchingPasswords(archivePath)
                        .FirstOrDefault(e => e.Password == match.Value.Password && e.Description == match.Value.Description);
                    _currentPasswordPatterns = matchedEntry?.Patterns?.ToList();
                    App.LogDebug("LoadArchiveAsync: matched password desc={0}", match.Value.Description);
                }
                else
                {
                    if (limitReached)
                    {
                        AppMessageBox.Show(L.TF(L.PwdMgr_AutoTry_LimitReached, 100),
                            L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    // 所有保存密码都失败 → 弹密码输入框让用户输入（密码错误时循环重试）
                    App.LogDebug("LoadArchiveAsync: no saved password matched, showing dialog");
                    while (true)
                    {
                        var pwdDialog = new PasswordDialog(Path.GetFileName(archivePath));
                        pwdDialog.Owner = this;
                        if (pwdDialog.ShowDialog() != true)
                            break; // 用户取消 → 以无密码状态加载（只读浏览文件名）

                        var userPwd = pwdDialog.ResultPassword;
                        if (string.IsNullOrEmpty(userPwd))
                            break;

                        if (App.QuickVerifyPassword(archivePath, userPwd, engine))
                        {
                            _currentPassword = userPwd;
                            _currentPasswordDescription = pwdDialog.Description;
                            _currentPasswordPatterns = pwdDialog.Patterns?.ToList();
                            if (pwdDialog.RememberPassword)
                            {
                                App.TrySavePassword(userPwd, archivePath, pwdDialog.Patterns, pwdDialog.Description);
                            }
                            break; // 密码正确，退出循环
                        }

                        // 密码错误 → 提示并重试
                        App.LogDebug("LoadArchiveAsync: wrong password entered for '{0}'", archivePath);
                        AppMessageBox.Show(L.T(L.Main_PasswordWrong),
                            L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }

            // 转换为 UI 的 ArchiveItem
            _allItems = items.Select(i => new ArchiveItem
            {
                Name = i.Name,
                FullPath = i.FullPath,
                Size = i.Size,
                CompressedSize = i.CompressedSize,
                LastModified = i.LastModified,
                IsDirectory = i.IsDirectory,
                IsEncrypted = i.IsEncrypted,
                Crc32 = i.Crc32,
                CompressedDisplay = compressedDisplay,
                IconSource = i.IsDirectory
                    ? SystemIconHelper.GetFolderIcon()
                    : SystemIconHelper.GetFileIcon(Path.GetExtension(i.Name))
            }).ToList();

            // 预计算目录统计（每个目录包含的文件数、总大小、压缩后大小）
            _dirStats.Clear();
            foreach (var ai in _allItems)
            {
                if (ai.IsDirectory) continue;
                var name = ai.Name;
                var lastSlash = name.LastIndexOf('/');
                if (lastSlash < 0) continue; // 根目录文件，不属于任何子目录
                var parts = name[..lastSlash].Split('/');
                for (int i = 0; i < parts.Length; i++)
                {
                    var dirPath = string.Join("/", parts, 0, i + 1);
                    var stat = _dirStats.GetValueOrDefault(dirPath);
                    _dirStats[dirPath] = (stat.Count + 1, stat.Size + ai.Size, stat.CompressedSize + ai.CompressedSize);
                }
            }

            // 构建目录树
            BuildFolderTree();

            // 显示根目录内容
            FilterFiles("");

            ArchiveLoadingOverlay.Visibility = Visibility.Collapsed;

            ArchiveNameText.Text = Path.GetFileName(archivePath);
            var totalSize = items.Sum(i => i.Size);
            // 对于 Unavailable 模式的格式，用实际文件大小作为总压缩大小
            var totalCompressed = compressedDisplay == ArchiveItem.CompressedDisplayMode.Unavailable
                ? new FileInfo(archivePath).Length
                : items.Sum(i => i.CompressedSize);
            ArchiveInfoText.Text = L.TF(L.Main_ArchiveInfo, items.Count, FormatSize(totalSize), FormatSize(totalCompressed));
            ArchiveStatsText.Text = L.TF(L.Main_ArchiveStats, items.Count, FormatSize(totalSize), FormatSize(totalCompressed));

            _recentFileManager.Add(archivePath);
            SetStatus(L.TF(L.Main_Status_Loaded, Path.GetFileName(archivePath)));
            UpdatePasswordStatus();
            UpdateSmartExtractBtnState();
            UpdateFilterBtnState();

            // L.T(L.Settings_Menu_Btn_Apply)L.T(L.Settings_Preview_Position)L.T(L.Settings_Title)
            ApplyPreviewPosition(AppSettings.Instance.PreviewPosition);
            // 显示L.T(L.Compress_Archive_Group)信息
            ShowArchiveInfo();
        }
        catch (Exception ex)
        {
            App.TraceLog("LoadArchiveAsync: failed: {0}", ex.ToString());
            App.LogDebug("LoadArchiveAsync: failed: {0}", ex.ToString());
            AppMessageBox.Show(L.TF(L.Main_Status_LoadFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            DirStatsText.Text = "";
            SelectionStatsText.Text = "";
            ArchiveStatsText.Text = "";
            PasswordStatusText.Text = "";
            _archiveComment = null;
            UpdateAddDeleteBtnState();
            UpdateSmartExtractBtnState();
            UpdateFilterBtnState();
            SetStatus(L.T(L.Main_Status_LoadFailed));

            // Reset UI state on error
            ArchiveLoadingOverlay.Visibility = Visibility.Collapsed;
            FileListPanel.Visibility = Visibility.Collapsed;
            DropHint.Visibility = Visibility.Visible;
        }
    }

    private async Task ExtractAsync(string archivePath, string destinationPath)
    {
        App.LogDebug("ExtractAsync: archive='{0}', dest='{1}', password={2}",
            archivePath, destinationPath, _currentPassword != null ? "***" : "null");
        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();

        try
        {
            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            if (engine == null)
            {
                App.LogDebug("ExtractAsync: no engine found for '{0}'", archivePath);
                return;
            }

            var ct = progressWindow.CancellationToken;
            var progress = progressWindow.CreatePauseAwareProgress(
                ProgressWindow.CreateBackgroundProgress(progressWindow));

            bool showPwd = _hasEncryptedArchive && AppSettings.Instance.ShowPasswordMatchNotification;

            // 先试已保存密码
            var match = App.TryMatchPassword(archivePath, engine, progressWindow, showPwd, out var limitReached);
            if (match != null)
            {
                var (pwd, desc) = match.Value;
                if (showPwd) progressWindow.ShowPasswordMatched(pwd, desc);

                var opts = App.CreateExtractOptions();
                await engine.ExtractAsync(archivePath, destinationPath, pwd, progress, ct, opts);

                progressWindow.Close();
                App.LogDebug("ExtractAsync: done (saved password match), dest='{0}'", destinationPath);
                SetStatus(L.TF(L.Main_Status_ExtractDone, Path.GetFileName(archivePath)));
                if (AppSettings.Instance.OpenFolderAfterExtract) OpenInExplorer(destinationPath);
                return;
            }

            // 自动尝试达到上限 → 提示用户
            if (limitReached)
            {
                progressWindow.Dispatcher.Invoke(() =>
                {
                    AppMessageBox.Show(L.TF(L.PwdMgr_AutoTry_LimitReached, 100),
                        L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }

            // 所有已L.T(L.PwdEdit_Save)L.T(L.PwdMgr_Col_Password)都失败，且L.T(L.Compress_Archive_Group)有加密条目 → 弹密码输入框
            if (!_hasEncryptedArchive)
            {
                // 非加密压缩包：直接解压，不需要密码
                App.LogDebug("ExtractAsync: no encryption, extracting without password");
                var opts = App.CreateExtractOptions();
                await engine.ExtractAsync(archivePath, destinationPath, null, progress, ct, opts);
                progressWindow.Close();
                App.LogDebug("ExtractAsync: done (no password), dest='{0}'", destinationPath);
                SetStatus(L.TF(L.Main_Status_ExtractDone, Path.GetFileName(archivePath)));
                if (AppSettings.Instance.OpenFolderAfterExtract) OpenInExplorer(destinationPath);
                return;
            }

            App.LogDebug("ExtractAsync: all saved passwords failed, prompting user for password");
            var pwdResult = App.PromptForPassword(archivePath, progressWindow, this);
            if (pwdResult == null)
            {
                App.LogDebug("ExtractAsync: user cancelled password prompt");
                progressWindow.Close();
                SetStatus(L.T(L.Main_Status_ExtractCancel));
                return;
            }

            var (userPwd, remember, pwdDesc, pwdPatterns) = pwdResult.Value;
            if (string.IsNullOrEmpty(userPwd)) { App.LogDebug("ExtractAsync: empty password provided"); progressWindow.Close(); SetStatus(L.T(L.Main_Status_ExtractCancel)); return; }
            bool showPwdManual = _hasEncryptedArchive && AppSettings.Instance.ShowPasswordMatchNotification;

            if (!await App.ExtractWithPasswordAsync(archivePath, destinationPath, engine,
                    userPwd, L.T(L.Main_ForceLoadPwd), progressWindow, progress, ct, showPwdManual, remember, pwdDesc, pwdPatterns))
            {
                progressWindow.Close();
                App.LogDebug("ExtractAsync: manual password failed (wrong password)");
                AppMessageBox.Show(L.T(L.Main_Status_WrongPwd), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus(L.T(L.Main_Status_WrongPwd));
                return;
            }

            progressWindow.Close();
            App.LogDebug("ExtractAsync: done (manual password), dest='{0}'", destinationPath);
            SetStatus(L.TF(L.Main_Status_ExtractDone, Path.GetFileName(archivePath)));
            if (AppSettings.Instance.OpenFolderAfterExtract) OpenInExplorer(destinationPath);
        }
        catch (OperationCanceledException)
        {
            App.LogDebug("ExtractAsync: cancelled by user");
            progressWindow.Close();
            SetStatus(L.T(L.Main_Status_AddCancel));
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            if (App.IsPasswordError(ex))
            {
                App.LogDebug("ExtractAsync: password error: {0}", ex.Message);
            }
            else
            {
                App.LogDebug("ExtractAsync: failed: {0}", ex.Message);
                AppMessageBox.Show(L.TF(L.Main_Status_ExtractFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            SetStatus(L.TF(L.Main_Status_ExtractFailed, ""));
        }
    }

    private async Task CompressAsync(string[] sourcePaths, string outputPath)
    {
        try
        {
            SetStatus(L.T(L.Main_Status_Compressing));
            ShowProgress(true);

            var options = App.CreateCompressOptions();
            options.CompressionLevel = AppSettings.Instance.DefaultLevel;
            options.Format = GetFormatByExtension(outputPath);

            var engine = ArchiveEngineFactory.GetEngineByExtension(outputPath, new ZipEngine());
            var progress = ProgressWindow.CreateBackgroundProgress(Dispatcher, p =>
            {
                ProgressBar.Value = p.PercentComplete;
                ProgressText.Text = $"{p.CurrentFile} ({p.PercentComplete:F1}%)";
            });

            await engine.CompressAsync(sourcePaths, outputPath, options, progress);

            // 加载新创建的L.T(L.Compress_Archive_Group)
            await LoadArchiveAsync(outputPath);

            SetStatus(L.TF(L.Main_Status_CompressDone, Path.GetFileName(outputPath)));
            ProgressBar.Value = 100;
            ProgressText.Text = "100%";
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(L.TF(L.Main_Status_CompressFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus(L.T(L.Main_Status_CompressFailed));
        }
        finally
        {
            ShowProgress(false);
        }
    }

    private async Task TestArchiveAsync(string archivePath)
    {
        // 先处理密码（在 ProgressWindow 之前）
        var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
        if (engine == null) return;

        string? password = _currentPassword;
        if (_hasEncryptedArchive && password == null)
        {
            var pwdDialog = new PasswordDialog(Path.GetFileName(archivePath));
            pwdDialog.Owner = this;
            if (pwdDialog.ShowDialog() == true)
            {
                var userPwd = pwdDialog.ResultPassword;
                if (!string.IsNullOrEmpty(userPwd) && App.QuickVerifyPassword(archivePath, userPwd, engine))
                {
                    password = userPwd;
                    _currentPassword = userPwd;
                    UpdatePasswordStatus();
                    UpdateEnterPasswordBtnState();
                    if (pwdDialog.RememberPassword)
                    {
                        App.TrySavePassword(userPwd, archivePath, pwdDialog.Patterns, pwdDialog.Description);
                    }
                }
                else
                {
                    AppMessageBox.Show(L.T(L.Main_Status_WrongPwd), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                    SetStatus(L.T(L.Main_Status_TestFailed));
                    return;
                }
            }
            else
            {
                SetStatus(L.T(L.Main_Status_AddCancel));
                return;
            }
        }

        // 打开 ProgressWindow 显示逐文件测试进度
        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Owner = this;
        progressWindow.Show();

        try
        {
            SetStatus(L.T(L.Main_Status_Testing));

            var ct = progressWindow.CancellationToken;
            var progress = ProgressWindow.CreateBackgroundProgress(progressWindow);

            var result = await engine.TestArchiveAsync(archivePath, password, progress, ct);

            // 刷新所有 Background 优先级的进度更新，确保 ProgressWindow 显示最终状态
            // Dispatcher.Invoke 从 UI 线程推一个嵌套帧，处理完 Background 队列中所有
            // 待处理的进度 Report 后再返回，防止结果框抢在进度更新之前弹出
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

            progressWindow.Close();

            // 引擎内部吞掉了 OperationCanceledException，通过 token 检测取消
            if (ct.IsCancellationRequested)
            {
                SetStatus(L.T(L.Main_Status_Cancelled));
                return;
            }

            AppMessageBox.Show(
                result ? L.T(L.Main_Status_TestResultOK) : L.T(L.Main_Status_TestResultBad),
                L.T(L.Main_Status_TestTitle),
                MessageBoxButton.OK,
                result ? MessageBoxImage.Information : MessageBoxImage.Warning);

            SetStatus(result ? L.T(L.Main_Status_TestPassed) : L.T(L.Main_Status_TestFailed));
        }
        catch (OperationCanceledException)
        {
            progressWindow.Close();
            SetStatus(L.T(L.Main_Status_Cancelled));
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            AppMessageBox.Show(L.TF(L.Main_Status_TestFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus(L.T(L.Main_Status_TestFailed));
        }
    }

    #endregion

    // UI helpers moved to MainWindow.UI.cs
    // FolderNode and ArchiveItem moved to MainWindow.UI.cs

    // Preview moved to MainWindow.Preview.cs
}

// FolderNode and ArchiveItem moved to MainWindow.UI.cs