using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Windows.Media;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using Microsoft.Win32;
using System.Text.Json;
using Markdig;

namespace MantisZip.UI;

/// <summary>
/// MainWindow.xaml 的交互逻辑
/// </summary>
public partial class MainWindow : Window
{
    private string? _currentArchivePath;
    private ArchiveFormat _currentFormat;
    private List<ArchiveItem> _allItems = new();  // 存储所有文件项
    private string _currentFolder = "";  // 当前目录
    private string? _previewTempDir;        // 预览临时目录
    private readonly Dictionary<int, double> _lastPreviewSizes = new()
    {
        { 1, 0 }, { 2, 0 }, { 3, 0 }, { 4, 0 }
    }; // 每个位置独立记忆大小（高度:位置1/2/3, 宽度:位置4）
    private int _lastAppliedPosition = 1;    // 上次应用的布局位置，用于检测变更
    private bool _isProgrammaticFilter;      // 编程触发的 FilterFiles，应跳过 SelectionChanged 预览
    private bool _previewPanelEnabled = true; // 工具栏预览开关状态
    private Point _dragStartPoint;           // 文件列表拖拽起点
    private string? _dragTempDir;            // 拖拽提取临时目录
    private bool _isOwnDrag;                 // 当前拖拽是否来自本窗口

    public MainWindow()
    {
        InitializeComponent();
        LoadWindowSettings();
        ApplyPreviewPosition(AppSettings.Instance.PreviewPosition);
        ApplyInfoPanelOrientation(AppSettings.Instance.InfoPanelOrientation);
        _previewPanelEnabled = AppSettings.Instance.ShowPreviewPanel;
        PreviewToggleBtn.IsChecked = _previewPanelEnabled;
        if (!_previewPanelEnabled)
            PreviewPanel.Visibility = Visibility.Collapsed;
        Activated += MainWindow_Activated;
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

    private class WindowSize
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double TreeColumnWidth { get; set; }
        public double PreviewRowHeight { get; set; }
        public double PreviewColumnWidth { get; set; }
        public double PreviewTreeHeight { get; set; }   // 位置2
        public double PreviewFilesHeight { get; set; }  // 位置3
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

            var obj = new WindowSize
            {
                Width = Width,
                Height = Height,
                TreeColumnWidth = treeWidth,
                PreviewRowHeight = previewHeight,
                PreviewColumnWidth = previewColumnWidth,
                PreviewTreeHeight = previewTreeHeight,
                PreviewFilesHeight = previewFilesHeight
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
    }

#endregion

#region 窗口拖入事件

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        // 来自本窗口的拖拽 → 忽略（自己拖给自己的文件已经在临时目录了）
        if (_isOwnDrag) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length == 0) return;

        // 当前有打开压缩包 → 询问是否添加到压缩包
        if (_currentArchivePath != null && File.Exists(_currentArchivePath))
        {
            // 如果拖入的是压缩包 → 打开它
            if (files.Length == 1 && IsArchiveFile(files[0]))
            {
                _ = LoadArchiveAsync(files[0]);
                return;
            }

            // 否则询问是否添加到当前压缩包
            var result = MessageBox.Show(
                this,
                $"将 {files.Length} 个文件/文件夹添加到「{Path.GetFileName(_currentArchivePath)}」？",
                "添加到压缩包",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await AddFilesToCurrentArchiveAsync(files);
            }
            return;
        }

        // 没有打开压缩包 → 原有行为（延后到拖放事件完全结束后再创建窗口，避免 WPF 拖放消息循环中创建新窗口导致死锁）
        var path = files[0];
        if (IsArchiveFile(path))
        {
            _ = LoadArchiveAsync(path);
        }
        else
        {
            var capturedFiles = files.ToArray(); // 拷贝避免闭包捕获可变变量
#pragma warning disable CS4014 // 在 async void 中故意 fire-and-forget
            Dispatcher.BeginInvoke(new Action(() =>
#pragma warning restore CS4014
            {
                var window = new CompressSettingsWindow();
                foreach (var f in capturedFiles)
                {
                    window.AddSourcePath(f);
                }
                window.Owner = this;
                window.Show();
                window.Activate();
            }));
        }
    }

    /// <summary>
    /// 将文件添加到当前打开的压缩包，然后刷新列表。
    /// </summary>
    private async Task AddFilesToCurrentArchiveAsync(string[] files)
    {
        var engine = ArchiveEngineFactory.GetEngine(_currentFormat);
        if (engine == null)
        {
            SetStatus("不支持的压缩格式");
            return;
        }

        try
        {
            SetStatus("正在添加文件到压缩包...");
            ShowProgress(true);

            var progress = new Progress<ArchiveProgress>(p =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ProgressBar.Value = p.PercentComplete;
                    ProgressText.Text = p.CurrentFile;
                });
            });

            await engine.AddToArchiveAsync(
                _currentArchivePath!,
                files,
                new ArchiveOptions { CompressionLevel = AppSettings.Instance.DefaultLevel },
                progress,
                entryBasePath: string.IsNullOrEmpty(_currentFolder) ? null : _currentFolder);

            // 刷新文件列表
            await LoadArchiveAsync(_currentArchivePath!);
            SetStatus("文件已添加到压缩包");
        }
        catch (NotSupportedException ex)
        {
            MessageBox.Show(ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus("就绪");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"添加文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("添加失败");
        }
        finally
        {
            ShowProgress(false);
        }
    }

#endregion

#region 文件列表拖出

    private void FileListGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(FileListGrid);
    }

    private async void FileListGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        // 未按下左键或没有打开压缩包 → 忽略
        if (e.LeftButton != MouseButtonState.Pressed || _currentArchivePath == null)
            return;

        // 检查是否超过拖拽阈值
        var pos = e.GetPosition(FileListGrid);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumHorizontalDragDistance)
            return;

        // 收集选中项
        var selectedItems = FileListGrid.SelectedItems.Cast<ArchiveItem>().ToList();
        if (selectedItems.Count == 0) return;

        // 检查拖动起点所在的行的选中状态
        var hitTest = FileListGrid.InputHitTest(_dragStartPoint) as DependencyObject;
        var row = FindVisualParent<DataGridRow>(hitTest);
        if (row?.Item is ArchiveItem rowItem && !selectedItems.Contains(rowItem))
        {
            // 点击了未选中的行 → 只选中它再拖
            FileListGrid.SelectedItem = rowItem;
            selectedItems = new List<ArchiveItem> { rowItem };
        }

        // 排除目录（只拖文件）
        var filesToDrag = selectedItems.Where(i => !i.IsDirectory).ToList();
        if (filesToDrag.Count == 0) return;

        // 准备临时提取目录
        _dragTempDir = Path.Combine(Path.GetTempPath(), "MantisZip", "DragDrop", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dragTempDir);

        // 阶段 1：提取文件到临时目录（弹出 ProgressWindow 显示进度）
        var pw = new ProgressWindow();
        pw.InitCancellation();
        pw.Owner = this;
        pw.Show();
        var ct = pw.CancellationToken;

        try
        {
            var extractedPaths = new List<string>();
            for (int i = 0; i < filesToDrag.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var item = filesToDrag[i];
                // 使用 FullPath 保留目录结构，避免不同子目录中同名文件互相覆盖
                var outputPath = Path.Combine(_dragTempDir, item.FullPath);

                pw.SetProgress(
                    (double)i / filesToDrag.Count * 100,
                    $"正在提取: {item.NameDisplay ?? item.Name}");

                await ExtractEntryForDragAsync(item, outputPath);
                extractedPaths.Add(outputPath);
            }

            if (ct.IsCancellationRequested)
            {
                SetStatus("已取消");
                return;
            }

            if (extractedPaths.Count == 0)
            {
                SetStatus("没有可拖拽的文件");
                return;
            }

            // 阶段 2：启动拖拽，进度窗口转为拖拽提示
            pw.SetProgress(100, "⏳ 正在拖拽 — 放到目标位置以复制文件");

            _isOwnDrag = true;
            try
            {
                var data = new DataObject(DataFormats.FileDrop, extractedPaths.ToArray());
                DragDrop.DoDragDrop(FileListGrid, data, DragDropEffects.Copy);
            }
            finally
            {
                _isOwnDrag = false;
            }
        }
        catch (NotSupportedException ex)
        {
            MessageBox.Show(this, ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"提取文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { if (pw.IsVisible) pw.Close(); } catch (Exception pwEx) { App.LogDebug("DragDrop finally: close pw failed: {0}", pwEx.Message); }
            CleanupDragTempDir();
            SetStatus("就绪");
        }
    }

    /// <summary>
    /// 从 TAR/GZ 压缩包中提取单个条目（顺序扫描到目标后停止）。
    /// </summary>
    private static void ExtractTarGzSingleEntry(string archivePath, string entryName, string outputPath)
    {
        using var inputStream = File.OpenRead(archivePath);
        var isTarGz = archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)
                   || archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);

        Stream tarStream = inputStream;
        if (isTarGz || Path.GetExtension(archivePath).Equals(".gz", StringComparison.OrdinalIgnoreCase))
            tarStream = new ICSharpCode.SharpZipLib.GZip.GZipInputStream(inputStream);

        using var tarIn = new ICSharpCode.SharpZipLib.Tar.TarInputStream(tarStream, System.Text.Encoding.UTF8);
        ICSharpCode.SharpZipLib.Tar.TarEntry? entry;
        while ((entry = tarIn.GetNextEntry()) != null)
        {
            if (entry.IsDirectory) continue;
            if (entry.Name == entryName)
            {
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                using var outStream = File.Create(outputPath);
                tarIn.CopyEntryContents(outStream);
                return;
            }
        }
        throw new FileNotFoundException($"在压缩包中未找到条目: {entryName}");
    }

    private void CleanupDragTempDir()
    {
        if (_dragTempDir == null) return;
        try
        {
            if (Directory.Exists(_dragTempDir))
                Directory.Delete(_dragTempDir, recursive: true);
        }
        catch (Exception dragCleanupEx) { App.LogDebug("CleanupDragTempDir: failed: {0}", dragCleanupEx.Message); }
        _dragTempDir = null;
    }

    /// <summary>
    /// 拖拽提取单个条目到临时目录。
    /// </summary>
    private async Task ExtractEntryForDragAsync(ArchiveItem item, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        switch (_currentFormat)
        {
            case ArchiveFormat.Zip:
            case ArchiveFormat.SevenZip:
                await ArchiveEntryExtractor.ExtractEntryAsync(
                    _currentArchivePath!, item.FullPath, outputPath, _currentFormat);
                break;

            case ArchiveFormat.Tar:
            case ArchiveFormat.GZip:
                ExtractTarGzSingleEntry(_currentArchivePath!, item.FullPath, outputPath);
                break;

            default:
                throw new NotSupportedException($"格式 {_currentFormat} 不支持拖拽提取");
        }
    }

    /// <summary>
    /// 从 DependencyObject 向上查找指定类型的父级。
    /// </summary>
    private static T? FindVisualParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T t) return t;
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        return null;
    }

#endregion

    #region 菜单事件

    private async void OpenArchive_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "压缩文件|*.zip;*.7z;*.rar;*.tar;*.tar.gz;*.gz;*.bz2;*.cab;*.iso|所有文件|*.*",
            Title = "打开压缩包"
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadArchiveAsync(dialog.FileName);
        }
    }

    private async void Extract_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentArchivePath)) return;

        var dest = ResolveExtractDestination(_currentArchivePath);
        if (dest != null)
        {
            await ExtractAsync(_currentArchivePath, dest);
        }
    }

    private async void Compress_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "所有文件|*.*",
            Title = "选择要压缩的文件",
            Multiselect = true
        };

        if (ofd.ShowDialog() == true)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "ZIP 压缩文件|*.zip",
                Title = "保存为",
                FileName = "archive.zip"
            };

            if (sfd.ShowDialog() == true)
            {
                await CompressAsync(ofd.FileNames, sfd.FileName);
            }
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentArchivePath))
        {
            await LoadArchiveAsync(_currentArchivePath);
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            $"MantisZip - 全功能解压缩软件\n\n版本: {AppConstants.Version}\n基于 .NET 9 + WPF\n\n支持格式: ZIP, 7z, TAR, GZ, RAR (只读)\n\n7-Zip 组件遵循 GNU LGPL 许可证\nhttps://www.7-zip.org",
            "关于 MantisZip",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow();
        // 实时同步字号到预览窗格
        window.OnTextFontSizeChanged = size =>
        {
            if (PreviewTextBox.Visibility == Visibility.Visible)
                PreviewTextBox.FontSize = size;
        };
        window.ShowDialog();
        // 关闭设置后也应用最终值
        if (PreviewTextBox.Visibility == Visibility.Visible)
            PreviewTextBox.FontSize = AppSettings.Instance.TextPreviewFontSize;
    }

    private void ShowSubFolders_Click(object sender, RoutedEventArgs e)
    {
        // 切换子目录显示，重新过滤当前目录
        FilterFiles(_currentFolder);
    }

    private void PasswordManager_Click(object sender, RoutedEventArgs e)
    {
        var window = new PasswordManagerWindow();
        window.ShowDialog();
    }

    private void TestArchive_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentArchivePath))
        {
            _ = TestArchiveAsync(_currentArchivePath);
        }
    }

    private void PreviewToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        _previewPanelEnabled = PreviewToggleBtn.IsChecked == true;
        AppSettings.Instance.ShowPreviewPanel = _previewPanelEnabled;
        AppSettings.Instance.Save();
        PreviewToggleIcon.Text = _previewPanelEnabled ? "👁" : "🚫";

        if (_previewPanelEnabled)
        {
            ShowPreviewPanel();
            // 重新显示当前选中项的预览
            if (FileListGrid.SelectedItem is ArchiveItem selected && !string.IsNullOrEmpty(_currentArchivePath))
            {
                if (selected.IsDirectory)
                    ShowDirectoryPreview(selected);
                else
                    _ = ShowPreviewAsync(selected);
            }
        }
        else
        {
            HidePreview();
        }
    }

    private void OpenHint_Click(object sender, RoutedEventArgs e)
    {
        OpenArchive_Click(sender, e);
    }

    #endregion

    #region 核心功能

    private bool IsArchiveFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".zip" or ".7z" or ".rar" or ".tar" or ".tgz" or ".gz" or ".bz2" or ".cab" or ".iso";
    }

    private static ArchiveFormat GetFormatByExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return ArchiveFormat.Tar;
        return ext switch
        {
            ".zip" => ArchiveFormat.Zip,
            ".7z" => ArchiveFormat.SevenZip,
            ".tar" or ".tgz" or ".gz" => ArchiveFormat.Tar,
            ".rar" => ArchiveFormat.Rar,
            _ => ArchiveFormat.Zip
        };
    }

    internal async Task LoadArchiveAsync(string archivePath)
    {
        try
        {
            ClearPreviewTemp();
            ClearPreviewContent();

            // 清空状态栏统计
            DirStatsText.Text = "";
            SelectionStatsText.Text = "";
            ArchiveStatsText.Text = "";

            SetStatus("正在加载压缩包...");
            _currentArchivePath = archivePath;

            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            if (engine == null)
            {
                MessageBox.Show("不支持的压缩格式", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("就绪");
                return;
            }

            _currentFormat = GetFormatByExtension(archivePath);

            var items = await engine.ListEntriesAsync(archivePath);

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
                IconSource = i.IsDirectory
                    ? SystemIconHelper.GetFolderIcon()
                    : SystemIconHelper.GetFileIcon(Path.GetExtension(i.Name))
            }).ToList();

            // 构建目录树
            BuildFolderTree();

            // 显示根目录内容
            FilterFiles("");

            FileListPanel.Visibility = Visibility.Visible;
            DropHint.Visibility = Visibility.Collapsed;

            ArchiveNameText.Text = Path.GetFileName(archivePath);
            var totalSize = items.Sum(i => i.Size);
            var totalCompressed = items.Sum(i => i.CompressedSize);
            ArchiveInfoText.Text = $"{items.Count} 个文件 | 原始: {FormatSize(totalSize)} | 压缩后: {FormatSize(totalCompressed)}";
            ArchiveStatsText.Text = $"总 {items.Count} 项 | 原始 {FormatSize(totalSize)} → 压缩 {FormatSize(totalCompressed)}";

            SetStatus($"已加载: {Path.GetFileName(archivePath)}");

            // 应用预览面板位置设置
            ApplyPreviewPosition(AppSettings.Instance.PreviewPosition);
            // 显示压缩包信息
            ShowArchiveInfo();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            DirStatsText.Text = "";
            SelectionStatsText.Text = "";
            ArchiveStatsText.Text = "";
            SetStatus("加载失败");
        }
    }

    private async Task ExtractAsync(string archivePath, string destinationPath)
    {
        string? password = null;

        try
        {
            SetStatus("正在解压...");
            ShowProgress(true);

            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            if (engine == null) return;

            var progress = new Progress<ArchiveProgress>(p =>
            {
                ProgressBar.Value = p.PercentComplete;
                ProgressText.Text = $"{p.CurrentFile} ({p.PercentComplete:F1}%)";
            });

            // 先尝试用已保存的密码
            var matchedPasswords = PasswordManager.Instance.FindMatchingPasswords(archivePath);
            if (matchedPasswords.Count > 0)
            {
                password = matchedPasswords[0].Password;
            }

            await engine.ExtractAsync(archivePath, destinationPath, password, progress);

            SetStatus($"解压完成: {Path.GetFileName(archivePath)}");
            ProgressBar.Value = 100;
            ProgressText.Text = "100%";

            if (AppSettings.Instance.OpenFolderAfterExtract)
                OpenInExplorer(destinationPath);
        }
        catch (Exception ex)
        {
            var errorMsg = ex.Message.ToLower();

            // 检查是否需要密码
            if (errorMsg.Contains("password") || errorMsg.Contains("密码") || errorMsg.Contains("encrypted"))
            {
                // 显示密码输入框
                var dialog = new PasswordDialog(Path.GetFileName(archivePath));
                dialog.Owner = this;

                if (dialog.ShowDialog() == true)
                {
                    password = dialog.ResultPassword;

                    // 先尝试用输入的密码解压
                    var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
                    if (engine != null)
                    {
                        try
                        {
                            var progress = new Progress<ArchiveProgress>(p =>
                            {
                                ProgressBar.Value = p.PercentComplete;
                                ProgressText.Text = $"{p.CurrentFile} ({p.PercentComplete:F1}%)";
                            });

                            await engine.ExtractAsync(archivePath, destinationPath, password, progress);

                            SetStatus($"解压完成: {Path.GetFileName(archivePath)}");

                            if (AppSettings.Instance.OpenFolderAfterExtract)
                                OpenInExplorer(destinationPath);

                            // 如果选择记住密码，保存
                            if (dialog.RememberPassword && !string.IsNullOrEmpty(password))
                            {
                                var patterns = new List<string> { Path.GetFileName(archivePath) };
                                PasswordManager.Instance.AddPassword(password, "", patterns);
                            }
                        }
                        catch (Exception pwdEx)
                        {
                            App.LogDebug("ExtractAsync: wrong password: {0}", pwdEx.Message);
                            ShowProgress(false);
                            MessageBox.Show("密码错误", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                            SetStatus("解压失败");
                            return;
                        }
                    }
                }
                else
                {
                    ShowProgress(false);
                    SetStatus("取消解压");
                    return;
                }
            }
            else
            {
                MessageBox.Show($"解压失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("解压失败");
            }
        }
        finally
        {
            ShowProgress(false);
        }
    }

    private async Task CompressAsync(string[] sourcePaths, string outputPath)
    {
        try
        {
            SetStatus("正在压缩...");
            ShowProgress(true);

            var options = new ArchiveOptions
            {
                CompressionLevel = CompressionLevelCombo.SelectedIndex + 1,
                Format = ArchiveFormat.Zip
            };

            var engine = new ZipEngine();
            var progress = new Progress<ArchiveProgress>(p =>
            {
                ProgressBar.Value = p.PercentComplete;
                ProgressText.Text = $"{p.CurrentFile} ({p.PercentComplete:F1}%)";
            });

            await engine.CompressAsync(sourcePaths, outputPath, options, progress);

            // 加载新创建的压缩包
            await LoadArchiveAsync(outputPath);

            SetStatus($"压缩完成: {Path.GetFileName(outputPath)}");
            ProgressBar.Value = 100;
            ProgressText.Text = "100%";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"压缩失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("压缩失败");
        }
        finally
        {
            ShowProgress(false);
        }
    }

    private async Task TestArchiveAsync(string archivePath)
    {
        try
        {
            SetStatus("正在测试压缩包...");
            ShowProgress(true);

            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            if (engine == null) return;

            var result = await engine.TestArchiveAsync(archivePath);

            MessageBox.Show(
                result ? "压缩包完整 ✓" : "压缩包已损坏 ✗",
                "测试结果",
                MessageBoxButton.OK,
                result ? MessageBoxImage.Information : MessageBoxImage.Warning);

            SetStatus(result ? "测试通过" : "测试失败");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"测试失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("测试失败");
        }
        finally
        {
            ShowProgress(false);
        }
    }

    #endregion

    #region UI 辅助

    private void SetStatus(string text)
    {
        StatusText.Text = text;
    }

    private void ShowProgress(bool show)
    {
        ProgressBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ProgressText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 根据设置或用户选择返回解压目标目录。返回 null 表示用户取消了操作。
    /// </summary>
    private string? ResolveExtractDestination(string archivePath)
    {
        var settings = AppSettings.Instance;
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

    /// <summary>
    /// 在文件资源管理器中打开指定路径。
    /// </summary>
    private static void OpenInExplorer(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start("explorer.exe", path);
        }
        catch (Exception explorerEx) { App.LogDebug("OpenInExplorer: failed: {0}", explorerEx.Message); }
    }

    private string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// 构建目录树
    /// </summary>
    private void BuildFolderTree()
    {
        var root = new FolderNode { Name = "📦 根目录", FullPath = "" };

        foreach (var item in _allItems.Where(i => i.IsDirectory))
        {
            var path = item.FullPath.TrimEnd('/');
            AddFolderNode(root.Children, path.Split('/'), 0, path);
        }

        FolderTree.ItemsSource = new List<FolderNode> { root };

        // 通过绑定展开并选中根目录（等待 TreeViewItem 生成）
        Dispatcher.BeginInvoke(new Action(() =>
        {
            root.IsExpanded = true;
            root.IsSelected = true;
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void AddFolderNode(List<FolderNode> nodes, string[] parts, int index, string fullPath)
    {
        if (index >= parts.Length) return;

        var name = parts[index];
        var currentPath = string.Join("/", parts.Take(index + 1));

        // 用 FullPath 检查，避免重复
        var existing = nodes.FirstOrDefault(n => n.FullPath == currentPath);
        if (existing == null)
        {
            existing = new FolderNode { Name = "📁 " + name, FullPath = currentPath };
            nodes.Add(existing);
        }

        if (index < parts.Length - 1)
        {
            AddFolderNode(existing.Children, parts, index + 1, fullPath);
        }
    }

/// <summary>
    /// 目录树选择改变
    /// </summary>
private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (FolderTree.SelectedItem is FolderNode node)
        {
            FilterFiles(node.FullPath);
        }
    }

    /// <summary>
    /// 过滤显示指定目录的文件
    /// </summary>
    private void FilterFiles(string folderPath)
    {
        _isProgrammaticFilter = true;
        try
        {
            _currentFolder = folderPath;

            IEnumerable<ArchiveItem> filtered;

            if (string.IsNullOrEmpty(folderPath))
            {
                // 根目录：计算 / 的层数，只有 1 层的就是直接子项
                filtered = _allItems.Where(i =>
                {
                    // 文件：不含 /
                    if (!i.Name.Contains("/")) return true;

                    // 目录：去掉最后的 /，然后看被分成几部分
                    var trimmed = i.Name.TrimEnd('/');
                    var parts = trimmed.Split('/');
                    return parts.Length == 1;  // 只有 1 部分 = 直接子目录
                });
            }
            else
            {
                // 非根目录
                var prefix = folderPath + "/";
                filtered = _allItems.Where(i =>
                {
                    // 1. 以 prefix 开头
                    if (!i.Name.StartsWith(prefix)) return false;

                    // 2. 排除目录本身（FullPath == folderPath）
                    if (i.FullPath == folderPath) return false;

                    // 3. 去掉 prefix 后，看被分成几部分
                    var trimmed = i.Name.Substring(prefix.Length).TrimEnd('/');
                    var parts = trimmed.Split('/');
                    return parts.Length == 1;  // 只有 1 部分 = 直接子项
                });
            }

            var sortedItems = filtered
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.Name)
                .ToList();

            // 设置显示名称
            foreach (var item in sortedItems)
            {
                if (string.IsNullOrEmpty(folderPath))
                {
                    // 根目录：只显示文件名（去掉最后的 /）
                    item.DisplayName = item.Name.TrimEnd('/');
                }
                else
                {
                    // 子目录：显示文件名（去掉前缀和最后的 /）
                    var prefixToRemove = folderPath + "/";
                    if (item.Name.StartsWith(prefixToRemove))
                    {
                        item.DisplayName = item.Name.Substring(prefixToRemove.Length).TrimEnd('/');
                    }
                    else
                    {
                        item.DisplayName = item.Name;
                    }
                }
            }

            FileListGrid.ItemsSource = sortedItems;
            FileListGrid.Items.Refresh();

            // 更新状态栏目录统计
            var fileCount = sortedItems.Count(i => !i.IsDirectory);
            var dirCount = sortedItems.Count(i => i.IsDirectory);
            DirStatsText.Text = $"{sortedItems.Count} 项 (文件 {fileCount}, 目录 {dirCount})";
        }
        finally
        {
            _isProgrammaticFilter = false;
        }
    }

    /// <summary>
    /// 双击进入子目录（使用 Preview 事件避免 Extended 模式下被 DataGrid 内部消费）
    /// </summary>
    private void FileListGrid_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListGrid.SelectedItem is ArchiveItem item)
        {
            if (item.IsDirectory)
            {
                FilterFiles(item.FullPath);

                // 更新目录树选中状态（不重建树，只选中已有节点）
                SelectFolderInTree(item.FullPath);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// 文件选择变化 → 预览
    /// </summary>
    private async void FileListGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 编程切换目录时（FilterFiles），不触发预览，也不关闭现有的预览
        if (_isProgrammaticFilter)
        {
            UpdateSelectionStats();
            return;
        }

        // 从 SelectionChanged 事件参数中推断"鼠标最后点击的文件"：
        //   - 加选：e.AddedItems 最后一项
        //   - 减选（Ctrl+click 取消选中）：e.RemovedItems 唯一的一项
        ArchiveItem? lastClicked = e.AddedItems.Count > 0
            ? e.AddedItems[e.AddedItems.Count - 1] as ArchiveItem
            : e.RemovedItems.Count == 1
                ? e.RemovedItems[0] as ArchiveItem
                : null;

        if (lastClicked != null && !string.IsNullOrEmpty(_currentArchivePath))
        {
            if (lastClicked.IsDirectory)
            {
                ShowDirectoryPreview(lastClicked);
            }
            else
            {
                await ShowPreviewAsync(lastClicked);
            }
        }

        UpdateSelectionStats();
    }

    private void UpdateSelectionStats()
    {
        if (string.IsNullOrEmpty(_currentArchivePath))
        {
            SelectionStatsText.Text = "";
            return;
        }

        var count = FileListGrid.SelectedItems.Count;
        if (count == 0)
        {
            SelectionStatsText.Text = "";
            return;
        }

        if (count == 1 && FileListGrid.SelectedItems[0] is ArchiveItem single)
        {
            SelectionStatsText.Text = single.IsDirectory
                ? $"📁 {single.Name.TrimEnd('/')}"
                : $"📄 {single.Name} ({FormatSize(single.Size)})";
            return;
        }

        // 多选：统计数量和总大小
        int fileCount = 0, dirCount = 0;
        long totalSize = 0;
        foreach (ArchiveItem ai in FileListGrid.SelectedItems)
        {
            if (ai.IsDirectory)
                dirCount++;
            else
            {
                fileCount++;
                totalSize += ai.Size;
            }
        }

        SelectionStatsText.Text = $"已选 {count} 项 (文件 {fileCount}, 目录 {dirCount}) | 共 {FormatSize(totalSize)}";
    }

    /// <summary>
    /// 在目录树中选中指定路径
    /// </summary>
    private void SelectFolderInTree(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        FolderNode? root = FolderTree.Items.Count > 0
            ? FolderTree.Items[0] as FolderNode
            : null;
        if (root == null) return;

        var target = FindFolderNode(root, path);
        if (target == null) return;

        // 展开从根到目标的各级父节点，让目标 TreeViewItem 可见
        ExpandAncestors(root, target);

        // 通过绑定选中目标节点（Binding 自动更新 TreeViewItem）
        target.IsSelected = true;
    }

    private static FolderNode? FindFolderNode(FolderNode node, string targetPath)
    {
        if (node.FullPath == targetPath) return node;
        foreach (var child in node.Children)
        {
            var found = FindFolderNode(child, targetPath);
            if (found != null) return found;
        }
        return null;
    }

    private static void ExpandAncestors(FolderNode current, FolderNode target)
    {
        if (current == target) return;
        foreach (var child in current.Children)
        {
            if (ContainsNode(child, target))
            {
                child.IsExpanded = true;
                ExpandAncestors(child, target);
                return;
            }
        }
    }

    private static bool ContainsNode(FolderNode parent, FolderNode target)
    {
        if (parent == target) return true;
        return parent.Children.Any(c => ContainsNode(c, target));
    }

    #endregion

    #region 文件预览

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".webp"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".ini", ".cfg", ".conf", ".csv", ".xml", ".json",
        ".cs", ".csproj", ".yaml", ".yml", ".toml",
        ".sh", ".bat", ".cmd", ".ps1", ".py", ".js", ".ts", ".tsx",
        ".css", ".scss", ".less",
        ".sql", ".gitignore", ".editorconfig", ".sln", ".props", ".targets",
        ".ruleset", ".rc", ".resx", ".nuspec", ".gradle", ".dockerfile",
        ".env", ".yml", ".yaml", ".json5", ".h", ".c", ".cpp", ".hpp",
        ".swift", ".kt", ".java", ".rb", ".go", ".rs", ".php", ".vue"
    };

    private static readonly HashSet<string> HtmlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm"
    };

    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown"
    };

    private async Task ShowPreviewAsync(ArchiveItem item)
    {
        try
        {
            // 清理上次预览
            ClearPreviewTemp();
            ClearPreviewContent();

            var s = AppSettings.Instance;
            var ext = Path.GetExtension(item.Name);

            if (ImageExtensions.Contains(ext))
            {
                if (!s.EnableImagePreview)
                {
                    ShowUnsupportedPreview(item, "🔍 图片预览已禁用（可在设置中启用）");
                    return;
                }

                _previewTempDir = Path.Combine(Path.GetTempPath(), "MantisZip", Guid.NewGuid().ToString());
                Directory.CreateDirectory(_previewTempDir);
                var tempFile = Path.Combine(_previewTempDir, Path.GetFileName(item.Name) ?? "preview" + ext);

                await Core.Utils.ArchiveEntryExtractor.ExtractEntryAsync(
                    _currentArchivePath!, item.Name, tempFile, _currentFormat);

                await ShowImagePreviewAsync(tempFile, item);
            }
            else if (HtmlExtensions.Contains(ext) || MarkdownExtensions.Contains(ext))
            {
                if (!s.EnableTextPreview)
                {
                    ShowUnsupportedPreview(item, "📄 文本预览已禁用（可在设置中启用）");
                    return;
                }

                // 检查文件大小
                if (item.Size > s.MaxTextPreviewBytes)
                {
                    var limitMb = s.MaxTextPreviewBytes / (1024.0 * 1024.0);
                    ShowUnsupportedPreview(item, $"📄 文件过大 ({(double)item.Size / 1024 / 1024:F1} MB)，超过文本预览限制 ({limitMb:F0} MB)");
                    return;
                }

                _previewTempDir = Path.Combine(Path.GetTempPath(), "MantisZip", Guid.NewGuid().ToString());
                Directory.CreateDirectory(_previewTempDir);
                var tempFile = Path.Combine(_previewTempDir, Path.GetFileName(item.Name) ?? "preview.html");

                await Core.Utils.ArchiveEntryExtractor.ExtractEntryAsync(
                    _currentArchivePath!, item.Name, tempFile, _currentFormat);

                if (MarkdownExtensions.Contains(ext))
                    ShowMarkdownPreview(tempFile, item);
                else
                    ShowHtmlPreview(tempFile, item);
            }
            else if (TextExtensions.Contains(ext))
            {
                if (!s.EnableTextPreview)
                {
                    ShowUnsupportedPreview(item, "📄 文本预览已禁用（可在设置中启用）");
                    return;
                }

                // 检查文件大小
                if (item.Size > s.MaxTextPreviewBytes)
                {
                    var limitMb = s.MaxTextPreviewBytes / (1024.0 * 1024.0);
                    ShowUnsupportedPreview(item, $"📄 文件过大 ({(double)item.Size / 1024 / 1024:F1} MB)，超过文本预览限制 ({limitMb:F0} MB)");
                    return;
                }

                _previewTempDir = Path.Combine(Path.GetTempPath(), "MantisZip", Guid.NewGuid().ToString());
                Directory.CreateDirectory(_previewTempDir);
                var tempFile = Path.Combine(_previewTempDir, Path.GetFileName(item.Name) ?? "preview.txt");

                await Core.Utils.ArchiveEntryExtractor.ExtractEntryAsync(
                    _currentArchivePath!, item.Name, tempFile, _currentFormat);

                ShowTextPreview(tempFile, ext, item);
            }
            else
            {
                ShowUnsupportedPreview(item);
            }
        }
        catch (Exception ex)
        {
            ShowUnsupportedPreview(item, $"预览失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 异步加载并显示图片预览。在后台线程解码以避免卡 UI，
    /// 并限制解码尺寸 (DecodePixelWidth=1920) 减少内存开销。
    /// </summary>
    private async Task ShowImagePreviewAsync(string filePath, ArchiveItem item)
    {
        try
        {
            // 后台线程解码，不阻塞 UI
            var bitmap = await Task.Run(() =>
            {
                // 先获取实际尺寸，仅对超过 1920px 的图做降采样
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                    new Uri(filePath),
                    System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                    System.Windows.Media.Imaging.BitmapCacheOption.None);
                int actualWidth = decoder.Frames[0].PixelWidth;

                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(filePath);
                // 只有大图才降采样，小图保持原生清晰度
                if (actualWidth > 1920)
                    bmp.DecodePixelWidth = 1920;
                bmp.EndInit();
                bmp.Freeze(); // 跨线程安全
                return bmp;
            });

            PreviewImage.Source = bitmap;
            PreviewImage.MaxWidth = bitmap.PixelWidth;
            PreviewImage.MaxHeight = bitmap.PixelHeight;
            PreviewImage.Visibility = Visibility.Visible;
            PreviewTextBox.Visibility = Visibility.Collapsed;
            PreviewFileIcon.Visibility = Visibility.Collapsed;
            PreviewUnsupported.Visibility = Visibility.Collapsed;
            PreviewHeader.Text = $"🔍 预览: {Path.GetFileName(filePath)}";

            // 图片信息
            PreviewInfoTitle.Text = "图片信息";
            var imgInfo = BuildInfoText(item);
            imgInfo += $"\n尺寸: {bitmap.PixelWidth} × {bitmap.PixelHeight}";
            PreviewInfoText.Text = imgInfo;
            PreviewInfoPanel.Visibility = Visibility.Visible;

            ShowPreviewPanel();
        }
        catch (Exception imgEx)
        {
            App.LogDebug("ShowImagePreviewAsync: failed: {0}", imgEx.Message);
            ShowUnsupportedPreview(null, "无法加载此图像");
        }
    }

    private void ShowTextPreview(string filePath, string extension, ArchiveItem item)
    {
        try
        {
            // 尝试多种编码读取文本
            string content;
            try
            {
                content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            }
            catch (Exception utfEx)
            {
                App.LogDebug("ShowTextPreview: UTF8 failed, trying GBK: {0}", utfEx.Message);
                content = File.ReadAllText(filePath, System.Text.Encoding.GetEncoding("GBK"));
            }

            PreviewTextBox.Text = content;
            PreviewTextBox.FontSize = AppSettings.Instance.TextPreviewFontSize;
            PreviewTextBox.Visibility = Visibility.Visible;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewFileIcon.Visibility = Visibility.Collapsed;
            PreviewUnsupported.Visibility = Visibility.Collapsed;
            PreviewInfoTitle.Text = "文本信息";
            PreviewInfoText.Text = BuildInfoText(item) + $"\n字符数: {content.Length}";
            PreviewInfoPanel.Visibility = Visibility.Visible;
            PreviewHeader.Text = $"📄 预览: {Path.GetFileName(filePath)} ({content.Length} 字符)";
            ShowPreviewPanel();
        }
        catch (Exception textEx)
        {
            App.LogDebug("ShowTextPreview: failed: {0}", textEx.Message);
            ShowUnsupportedPreview(null, "无法读取此文件");
        }
    }

    private void ShowHtmlPreview(string filePath, ArchiveItem item)
    {
        // WebBrowser 需要绝对路径或 URL
        PreviewWebBrowser.Navigate(new Uri(filePath));
        PreviewWebBrowser.Visibility = Visibility.Visible;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewTextBox.Visibility = Visibility.Collapsed;
        PreviewFileIcon.Visibility = Visibility.Collapsed;
        PreviewUnsupported.Visibility = Visibility.Collapsed;
        PreviewInfoTitle.Text = "文件信息";
        PreviewInfoText.Text = BuildInfoText(item);
        PreviewInfoPanel.Visibility = Visibility.Visible;
        PreviewHeader.Text = $"🌐 预览: {Path.GetFileName(filePath)}";
        ShowPreviewPanel();
    }

    private void ShowMarkdownPreview(string filePath, ArchiveItem item)
    {
        try
        {
            var mdContent = File.ReadAllText(filePath);
            var html = Markdig.Markdown.ToHtml(mdContent);
            // 包裹基本样式以便阅读
            var styledHtml = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'/>
<style>
  body {{ font-family: system-ui, sans-serif; font-size: 14px; line-height: 1.6; padding: 16px; color: #222; }}
  pre {{ background: #f4f4f4; padding: 12px; border-radius: 4px; overflow-x: auto; }}
  code {{ background: #f0f0f0; padding: 2px 4px; border-radius: 2px; font-family: Consolas, monospace; }}
  pre code {{ background: none; padding: 0; }}
  img {{ max-width: 100%; }}
  table {{ border-collapse: collapse; }}
  td, th {{ border: 1px solid #ccc; padding: 6px 10px; }}
</style></head>
<body>{html}</body></html>";

            var tempHtml = Path.Combine(Path.GetDirectoryName(filePath) ?? _previewTempDir!, "markdown_preview.html");
            File.WriteAllText(tempHtml, styledHtml);
            PreviewWebBrowser.Navigate(new Uri(tempHtml));
            PreviewWebBrowser.Visibility = Visibility.Visible;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewTextBox.Visibility = Visibility.Collapsed;
            PreviewFileIcon.Visibility = Visibility.Collapsed;
            PreviewUnsupported.Visibility = Visibility.Collapsed;
            PreviewInfoTitle.Text = "文件信息";
            PreviewInfoText.Text = BuildInfoText(item);
            PreviewInfoPanel.Visibility = Visibility.Visible;
            PreviewHeader.Text = $"📝 预览: {Path.GetFileName(filePath)}";
            ShowPreviewPanel();
        }
        catch (Exception mdEx)
        {
            App.LogDebug("ShowMarkdownPreview: failed: {0}", mdEx.Message);
            ShowUnsupportedPreview(null, "无法解析 Markdown 文件");
        }
    }

    private void ShowUnsupportedPreview(ArchiveItem? item, string? message = null)
    {
        PreviewUnsupported.Visibility = Visibility.Collapsed; // hide text fallback
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewTextBox.Visibility = Visibility.Collapsed;
        PreviewWebBrowser.Visibility = Visibility.Collapsed;
        PreviewInfoPanel.Visibility = Visibility.Visible;

        if (item != null)
        {
            // 显示系统图标
            var ext = Path.GetExtension(item.Name);
            var icon = SystemIconHelper.GetFileIcon(ext);
            PreviewFileIcon.Source = icon;
            PreviewFileIcon.Visibility = Visibility.Visible;

            PreviewInfoTitle.Text = "文件信息";
            var info = BuildInfoText(item);
            if (!string.IsNullOrEmpty(message))
                info += $"\n\n{message}";
            PreviewInfoText.Text = info;
            PreviewHeader.Text = $"📄 {item.Name}";
        }
        else
        {
            PreviewFileIcon.Visibility = Visibility.Collapsed;
            PreviewInfoTitle.Text = "";
            PreviewInfoText.Text = message ?? "";
            PreviewHeader.Text = "预览";
        }

        ShowPreviewPanel();
    }

    /// <summary>
    /// 显示目录预览：系统文件夹图标 + 目录名
    /// </summary>
    private void ShowDirectoryPreview(ArchiveItem item)
    {
        if (!item.IsDirectory) return;

        PreviewHeader.Text = $"📁 {item.Name.TrimEnd('/')}";

        // 内容区：文件夹图标
        PreviewFileIcon.Source = SystemIconHelper.GetFolderIcon();
        PreviewFileIcon.Visibility = Visibility.Visible;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewTextBox.Visibility = Visibility.Collapsed;
        PreviewWebBrowser.Visibility = Visibility.Collapsed;
        PreviewUnsupported.Visibility = Visibility.Collapsed;

        // 信息面板
        PreviewInfoTitle.Text = "目录信息";
        PreviewInfoText.Text = $"目录名: {item.Name.TrimEnd('/')}\n" +
                               $"原始大小: {FormatSize(item.Size)}";
        PreviewInfoPanel.Visibility = Visibility.Visible;

        ShowPreviewPanel();
    }

    /// <summary>
    /// 显示压缩包总览信息（首次打开时）。
    /// </summary>
    private void ShowArchiveInfo()
    {
        if (string.IsNullOrEmpty(_currentArchivePath) || _allItems.Count == 0)
            return;

        var totalSize = _allItems.Sum(i => i.Size);
        var totalCompressed = _allItems.Sum(i => i.CompressedSize);
        int fileCount = _allItems.Count(i => !i.IsDirectory);
        int dirCount = _allItems.Count(i => i.IsDirectory);

        PreviewHeader.Text = $"📦 {Path.GetFileName(_currentArchivePath)}";

        // 内容区：压缩包图标
        PreviewFileIcon.Source = SystemIconHelper.GetFileIcon(Path.GetExtension(_currentArchivePath));
        PreviewFileIcon.Visibility = Visibility.Visible;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewTextBox.Visibility = Visibility.Collapsed;
        PreviewWebBrowser.Visibility = Visibility.Collapsed;
        PreviewUnsupported.Visibility = Visibility.Collapsed;

        // 信息面板
        PreviewInfoTitle.Text = "压缩包信息";
        var ratio = totalSize > 0 ? $"{(double)totalCompressed / totalSize * 100:F1}%" : "--";
        PreviewInfoText.Text =
            $"文件名: {Path.GetFileName(_currentArchivePath)}\n" +
            $"文件数: {fileCount} 个文件, {dirCount} 个目录\n" +
            $"原始大小: {FormatSize(totalSize)}\n" +
            $"压缩后: {FormatSize(totalCompressed)}\n" +
            $"压缩率: {ratio}";
        PreviewInfoPanel.Visibility = Visibility.Visible;

        ShowPreviewPanel();
    }

    /// <summary>
    /// 确保 PreviewPanel 位于正确的父 Grid 中。
    /// 位置 1/4 → 外层 ContentGrid；位置 2/3 → 内层 InnerContentGrid。
    /// </summary>
    private void EnsurePreviewInCorrectGrid(int position)
    {
        var currentParent = VisualTreeHelper.GetParent(PreviewPanel) as Grid;
        var target = (position == 2 || position == 3) ? InnerContentGrid : ContentGrid;

        if (currentParent == target) return;

        // 从当前父 Grid 中移除
        currentParent?.Children.Remove(PreviewPanel);
        // 添加到目标父 Grid（保持 z-order 合理：最后添加）
        target.Children.Add(PreviewPanel);
    }

    /// <summary>
    /// 根据 AppSettings.PreviewPosition 重新布局预览面板位置。
    /// 1=底部, 2=目录树下方, 3=文件列表下方, 4=文件列表右侧
    /// </summary>
    private void ApplyPreviewPosition(int position)
    {
        // 移动 PreviewPanel 到正确的父 Grid
        EnsurePreviewInCorrectGrid(position);

        // 先重置所有元素到默认状态
        PreviewSplitter.Visibility = Visibility.Collapsed;
        PreviewColSplitter.Visibility = Visibility.Collapsed;
        InnerPreviewSplitter.Visibility = Visibility.Collapsed;
        PreviewSplitterRow.Height = new GridLength(0);
        PreviewRow.Height = new GridLength(0);
        InnerPreviewSplitterRow.Height = new GridLength(0);
        InnerPreviewRow.Height = new GridLength(0);
        PreviewColumnDef.Width = new GridLength(0);
        PreviewColSplitterDef.Width = new GridLength(0);
        Grid.SetRowSpan(FolderTree, 1);
        Grid.SetRowSpan(TreeFileSplitter, 1);
        Grid.SetRowSpan(FileListGrid, 1);
        Grid.SetColumnSpan(FileListGrid, 1);
        Grid.SetColumnSpan(PreviewPanel, 1);
        Grid.SetRowSpan(PreviewPanel, 1);
        Grid.SetRowSpan(InnerPreviewSplitter, 1);
        Grid.SetColumnSpan(InnerPreviewSplitter, 5);
        Grid.SetRow(InnerPreviewSplitter, 1);
        Grid.SetColumn(InnerPreviewSplitter, 0);
        // 默认 InnerContentGrid 横跨全部5列
        Grid.SetColumnSpan(InnerContentGrid, 5);

        switch (position)
        {
            case 1: // 底部（当前默认）
                PreviewSplitter.Visibility = Visibility.Visible;
                PreviewSplitterRow.Height = new GridLength(4);
                Grid.SetRow(PreviewPanel, 2);
                Grid.SetColumn(PreviewPanel, 0);
                Grid.SetColumnSpan(PreviewPanel, 5);
                break;

            case 2: // 目录树下方
                Grid.SetRowSpan(FileListGrid, 3);
                Grid.SetColumnSpan(FileListGrid, 3);
                Grid.SetRowSpan(TreeFileSplitter, 3);
                Grid.SetRow(PreviewPanel, 2);
                Grid.SetColumn(PreviewPanel, 0);
                Grid.SetColumnSpan(PreviewPanel, 1);
                // 内部分隔条 Row 1
                InnerPreviewSplitter.Visibility = Visibility.Visible;
                InnerPreviewSplitterRow.Height = new GridLength(4);
                Grid.SetColumnSpan(InnerPreviewSplitter, 1);
                Grid.SetColumn(InnerPreviewSplitter, 0);
                break;

            case 3: // 文件列表下方
                Grid.SetRowSpan(FolderTree, 3);
                Grid.SetRowSpan(TreeFileSplitter, 3);
                Grid.SetColumnSpan(FileListGrid, 1);
                Grid.SetRow(PreviewPanel, 2);
                Grid.SetColumn(PreviewPanel, 2);
                Grid.SetColumnSpan(PreviewPanel, 3);
                // 内部分隔条 Row 1
                InnerPreviewSplitter.Visibility = Visibility.Visible;
                InnerPreviewSplitterRow.Height = new GridLength(4);
                Grid.SetColumnSpan(InnerPreviewSplitter, 3);
                Grid.SetColumn(InnerPreviewSplitter, 2);
                break;

            case 4: // 文件列表右侧
                PreviewColSplitter.Visibility = Visibility.Visible;
                PreviewColSplitterDef.Width = new GridLength(4);
                Grid.SetRow(PreviewPanel, 0);
                Grid.SetColumn(PreviewPanel, 4);
                Grid.SetColumnSpan(PreviewPanel, 1);
                // 限制 InnerContentGrid 只占 Col 0-2（不延伸到预览列）
                Grid.SetColumnSpan(InnerContentGrid, 3);
                break;
        }
    }

    /// <summary>
    /// 根据 AppSettings.InfoPanelOrientation 切换信息面板布局。
    /// Horizontal = 内容区右侧；Vertical = 内容区下方
    /// </summary>
    private void ApplyInfoPanelOrientation(string orientation)
    {
        if (orientation == "Vertical")
        {
            // 信息面板在内容区下方
            PreviewContentGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            PreviewContentGrid.ColumnDefinitions[1].Width = new GridLength(0);
            PreviewContentGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Auto);
            Grid.SetRow(PreviewInfoPanel, 1);
            Grid.SetColumn(PreviewInfoPanel, 0);
            PreviewInfoPanel.Margin = new Thickness(0, 12, 0, 0);
        }
        else
        {
            // 信息面板在内容区右侧（默认）
            PreviewContentGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            PreviewContentGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Auto);
            PreviewContentGrid.RowDefinitions[1].Height = new GridLength(0);
            Grid.SetRow(PreviewInfoPanel, 0);
            Grid.SetColumn(PreviewInfoPanel, 1);
            PreviewInfoPanel.Margin = new Thickness(12, 0, 0, 0);
        }
    }

    private void ShowPreviewPanel()
    {
        var pos = AppSettings.Instance.PreviewPosition;

        // 如果位置变了，保存旧位置大小并应用新布局
        if (pos != _lastAppliedPosition)
        {
            SaveCurrentPreviewSize(_lastAppliedPosition);
            _lastAppliedPosition = pos;
            ApplyPreviewPosition(pos);
        }
        else
        {
            ApplyPreviewPosition(pos);
        }

        if (pos == 4)
        {
            // 右侧模式：靠列宽度控制
            var colWidth = _lastPreviewSizes[4] > 0 ? _lastPreviewSizes[4] : 350;
            PreviewColumnDef.Width = new GridLength(colWidth, GridUnitType.Pixel);
            PreviewColSplitterDef.Width = new GridLength(4);
            PreviewColSplitter.Visibility = Visibility.Visible;
            PreviewRow.Height = new GridLength(0);
            PreviewSplitterRow.Height = new GridLength(0);
            PreviewSplitter.Visibility = Visibility.Collapsed;
            InnerPreviewRow.Height = new GridLength(0);
        }
        else if (pos == 2 || pos == 3)
        {
            // 目录树下方 / 文件列表下方：内层 Grid 的预览行控制高度
            var h = _lastPreviewSizes[pos] > 0 ? _lastPreviewSizes[pos] : 200;
            InnerPreviewRow.Height = new GridLength(h, GridUnitType.Pixel);
            PreviewRow.Height = new GridLength(0);
            PreviewSplitterRow.Height = new GridLength(0);
            PreviewSplitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            // 底部模式：外层 Grid 的预览行控制高度
                var h = _lastPreviewSizes[1] > 0
                ? new GridLength(_lastPreviewSizes[1], GridUnitType.Pixel)
                : new GridLength(1, GridUnitType.Star);
            PreviewSplitterRow.Height = new GridLength(4);
            PreviewRow.Height = h;
            PreviewSplitter.Visibility = Visibility.Visible;
        }
        if (_previewPanelEnabled)
            PreviewPanel.Visibility = Visibility.Visible;
        ApplyInfoPanelOrientation(AppSettings.Instance.InfoPanelOrientation);
    }

    /// <summary>
    /// 保存指定位置的当前预览大小到独立记忆字典。
    /// </summary>
    private void SaveCurrentPreviewSize(int position)
    {
        if (position == 4)
        {
            if (PreviewColumnDef.Width.GridUnitType == GridUnitType.Pixel)
                _lastPreviewSizes[4] = PreviewColumnDef.Width.Value;
        }
        else if (position == 2 || position == 3)
        {
            if (InnerPreviewRow.Height.GridUnitType == GridUnitType.Pixel)
                _lastPreviewSizes[position] = InnerPreviewRow.Height.Value;
            else if (InnerPreviewRow.Height.Value > 0)
                _lastPreviewSizes[position] = 300; // Star 模式切 Pixel 时给个默认值
        }
        else // position 1
        {
            if (PreviewRow.Height.GridUnitType == GridUnitType.Pixel)
                _lastPreviewSizes[1] = PreviewRow.Height.Value;
        }
    }

    /// <summary>
    /// 仅清除预览内容，不隐藏面板（用于文件间切换，避免闪烁）。
    /// </summary>
    private void ClearPreviewContent()
    {
        if (PreviewPanel.Visibility == Visibility.Visible)
            SaveCurrentPreviewSize(AppSettings.Instance.PreviewPosition);

        PreviewImage.Source = null;
        PreviewFileIcon.Source = null;
        PreviewTextBox.Text = "";
        PreviewWebBrowser.Navigate("about:blank");
        PreviewWebBrowser.Visibility = Visibility.Collapsed;
    }

    private void HidePreview()
    {
        // 只在预览面板可见时保存大小，避免重复 HidePreview 覆盖已保存的值
        if (PreviewPanel.Visibility == Visibility.Visible)
            SaveCurrentPreviewSize(AppSettings.Instance.PreviewPosition);

        PreviewImage.Source = null;
        PreviewFileIcon.Source = null;
        PreviewTextBox.Text = "";
        // 清除 WebBrowser 内容并隐藏
        PreviewWebBrowser.Navigate("about:blank");
        PreviewWebBrowser.Visibility = Visibility.Collapsed;
        PreviewRow.Height = new GridLength(0);
        PreviewSplitterRow.Height = new GridLength(0);
        PreviewSplitter.Visibility = Visibility.Collapsed;
        PreviewPanel.Visibility = Visibility.Collapsed;
        InnerPreviewRow.Height = new GridLength(0);
        InnerPreviewSplitterRow.Height = new GridLength(0);
        InnerPreviewSplitter.Visibility = Visibility.Collapsed;
        // 重置右侧模式（位置4）的列宽
        PreviewColumnDef.Width = new GridLength(0);
        PreviewColSplitterDef.Width = new GridLength(0);
        PreviewColSplitter.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 构建通用文件信息文本
    /// </summary>
    private string BuildInfoText(ArchiveItem item)
    {
        var ratio = item.Size > 0
            ? $"{(double)item.CompressedSize / item.Size * 100:F1}%"
            : "--";
        var info = $"文件名: {item.Name}\n" +
                   $"大小: {FormatSize(item.Size)}\n" +
                   $"压缩后: {FormatSize(item.CompressedSize)}\n" +
                   $"压缩率: {ratio}\n" +
                   $"修改日期: {item.LastModified:yyyy-MM-dd HH:mm}";
        if (item.IsEncrypted)
            info += "\n🔒 已加密";
        return info;
    }

    /// <summary>
    /// 清理预览临时文件
    /// </summary>
    private void ClearPreviewTemp()
    {
        try
        {
            if (!string.IsNullOrEmpty(_previewTempDir) && Directory.Exists(_previewTempDir))
            {
                Directory.Delete(_previewTempDir, recursive: true);
                _previewTempDir = null;
            }
        }
        catch (Exception cleanupEx)
        {
            App.LogDebug("ClearPreviewTemp: failed: {0}", cleanupEx.Message);
        }
    }

    #endregion
}

/// <summary>
/// 文件夹树节点
/// </summary>
public class FolderNode : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public List<FolderNode> Children { get; set; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

// 本地扩展 ArchiveItem（因为 Core 的没有这些属性）
public class ArchiveItem : Core.Abstractions.ArchiveItem
{
    public string DisplayName { get; set; } = string.Empty;  // 显示用的名称
    public string NameForSort { get; set; } = string.Empty;  // 排序用的名称

    /// <summary>
    /// 文件系统图标，由 SystemIconHelper 按扩展名加载。
    /// 在 LoadArchiveAsync 中设置后不再变更。
    /// </summary>
    public ImageSource? IconSource { get; set; }

    public string SizeDisplay => IsDirectory ? "--" : FormatSize(Size);
    public string CompressedSizeDisplay => IsDirectory ? "--" : FormatSize(CompressedSize);

    public string NameDisplay 
    { 
        get 
        {
            if (!string.IsNullOrEmpty(DisplayName))
                return DisplayName;
            return Name;
        } 
    }

    public int SortOrder => IsDirectory ? 0 : 1;

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}