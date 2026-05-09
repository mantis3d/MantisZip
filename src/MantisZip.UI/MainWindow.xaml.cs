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
    private GridLength? _lastPreviewHeight;  // 上次的预览行高度（保存 GridLength 以保留 Star/Pixel 类型）
    private bool _isProgrammaticFilter;      // 编程触发的 FilterFiles，应跳过 SelectionChanged 预览
    private Point _dragStartPoint;           // 文件列表拖拽起点
    private string? _dragTempDir;            // 拖拽提取临时目录

    public MainWindow()
    {
        InitializeComponent();
        LoadWindowSettings();
        UpdateShellMenuItems();
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

                // 恢复预览行高度（仅在有存档加载时使用）
                if (obj?.PreviewRowHeight > 0)
                {
                    _lastPreviewHeight = new GridLength(obj.PreviewRowHeight);
                }
            }
        }
        catch
        {
            // 如果读取失败，使用默认大小
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

            // 保存预览行高度（如果当前可见且是像素值）
            double previewHeight = 0;
            if (PreviewPanel.Visibility == Visibility.Visible && PreviewRow.Height.GridUnitType == GridUnitType.Pixel)
            {
                previewHeight = PreviewRow.Height.Value;
            }
            else if (_lastPreviewHeight?.Value > 0)
            {
                previewHeight = _lastPreviewHeight.Value.Value;
            }

            var obj = new WindowSize
            {
                Width = Width,
                Height = Height,
                TreeColumnWidth = treeWidth,
                PreviewRowHeight = previewHeight
            };
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
        }
        catch
        {
            // 如果保存失败，忽略
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

        // 没有打开压缩包 → 原有行为
        var path = files[0];
        if (IsArchiveFile(path))
        {
            _ = LoadArchiveAsync(path);
        }
        else
        {
            var window = new CompressSettingsWindow();
            foreach (var f in files)
            {
                window.AddSourcePath(f);
            }
            window.Show();
            window.Activate();
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
                progress);

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
        SetStatus($"正在提取 {filesToDrag.Count} 个文件...");

        try
        {
            // 逐个提取到临时目录
            var extractedPaths = new List<string>();
            foreach (var item in filesToDrag)
            {
                var outputPath = Path.Combine(_dragTempDir, item.Name);
                await ExtractEntryForDragAsync(item, outputPath);
                extractedPaths.Add(outputPath);
            }

            // 启动拖拽（阻塞直到拖拽操作结束）
            var data = new DataObject(DataFormats.FileDrop, extractedPaths.ToArray());
            DragDrop.DoDragDrop(FileListGrid, data, DragDropEffects.Copy);
        }
        catch (NotSupportedException ex)
        {
            MessageBox.Show(ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"提取文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CleanupDragTempDir();
            SetStatus("就绪");
        }
    }

    /// <summary>
    /// 提取压缩包内单个条目到临时文件，供拖拽使用。
    /// 支持 ZIP、7z 快速单条提取，TarGz 通过顺序扫描提取。
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

        using var tarIn = new ICSharpCode.SharpZipLib.Tar.TarInputStream(tarStream);
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
        catch { }
        _dragTempDir = null;
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
        window.ShowDialog();
        // 设置可能已变更，刷新 Shell 菜单状态
        UpdateShellMenuItems();
    }

    private void ShowSubFolders_Click(object sender, RoutedEventArgs e)
    {
        // 刷新当前目录
        FilterFiles(_currentFolder);
    }

    private void PasswordManager_Click(object sender, RoutedEventArgs e)
    {
        var window = new PasswordManagerWindow();
        window.ShowDialog();
    }

    private void InstallShell_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ShellIntegration.Install();
            MessageBox.Show(
                "Shell 右键菜单已安装。\n\n" +
                "• 右键任意文件/文件夹 → 用 MantisZip 压缩\n" +
                "• 右键压缩包 (.zip/.7z/.rar 等) → 用 MantisZip 解压",
                "MantisZip", MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateShellMenuItems();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"安装失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UninstallShell_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ShellIntegration.Uninstall();
            MessageBox.Show("Shell 右键菜单已卸载", "MantisZip",
                MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateShellMenuItems();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"卸载失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateShellMenuItems()
    {
        var installed = ShellIntegration.IsInstalled;
        ShellInstallMenuItem.IsEnabled = !installed;
        ShellUninstallMenuItem.IsEnabled = installed;
    }

    private void TestArchive_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentArchivePath))
        {
            _ = TestArchiveAsync(_currentArchivePath);
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
            HidePreview();

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
                            if (dialog.RememberPassword)
                            {
                                var patterns = new List<string> { Path.GetFileName(archivePath) };
                                PasswordManager.Instance.AddPassword(password, "", patterns);
                            }
                        }
                        catch
                        {
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
        catch { /* 忽略打开失败 */ }
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
        // 编程切换目录时（FilterFiles），不触发预览
        if (_isProgrammaticFilter)
        {
            HidePreview();
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

        if (lastClicked != null && !lastClicked.IsDirectory && !string.IsNullOrEmpty(_currentArchivePath))
        {
            await ShowPreviewAsync(lastClicked);
        }
        else
        {
            HidePreview();
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
        ".cs", ".csproj", ".md", ".yaml", ".yml", ".toml",
        ".sh", ".bat", ".cmd", ".ps1", ".py", ".js", ".ts", ".tsx",
        ".html", ".htm", ".css", ".scss", ".less",
        ".sql", ".gitignore", ".editorconfig", ".sln", ".props", ".targets",
        ".ruleset", ".rc", ".resx", ".nuspec", ".gradle", ".dockerfile",
        ".env", ".yml", ".yaml", ".json5", ".h", ".c", ".cpp", ".hpp",
        ".swift", ".kt", ".java", ".rb", ".go", ".rs", ".php", ".vue"
    };

    private async Task ShowPreviewAsync(ArchiveItem item)
    {
        try
        {
            // 清理上次预览
            ClearPreviewTemp();
            HidePreview();

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
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(filePath);
                bmp.DecodePixelWidth = 1920; // 限制解码尺寸，避免大图全分辨率解码
                bmp.EndInit();
                bmp.Freeze(); // 跨线程安全
                return bmp;
            });

            PreviewImage.Source = bitmap;
            PreviewImage.Visibility = Visibility.Visible;
            PreviewTextBox.Visibility = Visibility.Collapsed;
            PreviewUnsupported.Visibility = Visibility.Collapsed;
            PreviewHeader.Text = $"🔍 预览: {Path.GetFileName(filePath)}";

            // 构建图片信息
            var ratio = item.Size > 0
                ? $"{(double)item.CompressedSize / item.Size * 100:F1}%"
                : "--";
            PreviewInfoText.Text =
                $"文件名: {item.Name}\n" +
                $"大小: {FormatSize(item.Size)}\n" +
                $"压缩后: {FormatSize(item.CompressedSize)}\n" +
                $"压缩率: {ratio}\n" +
                $"修改日期: {item.LastModified:yyyy-MM-dd HH:mm}";
            PreviewInfoPanel.Visibility = Visibility.Visible;

            ShowPreviewPanel();
        }
        catch
        {
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
            catch
            {
                content = File.ReadAllText(filePath, System.Text.Encoding.GetEncoding("GBK"));
            }

            PreviewTextBox.Text = content;
            PreviewTextBox.Visibility = Visibility.Visible;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewUnsupported.Visibility = Visibility.Collapsed;
            PreviewInfoPanel.Visibility = Visibility.Collapsed;
            PreviewHeader.Text = $"📄 预览: {Path.GetFileName(filePath)} ({content.Length} 字符)";
            ShowPreviewPanel();
        }
        catch
        {
            ShowUnsupportedPreview(null, "无法读取此文件");
        }
    }

    private void ShowUnsupportedPreview(ArchiveItem? item, string? message = null)
    {
        PreviewUnsupported.Text = message ?? "🔍 无法预览此文件";
        PreviewUnsupported.Visibility = Visibility.Visible;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewTextBox.Visibility = Visibility.Collapsed;
        PreviewInfoPanel.Visibility = Visibility.Collapsed;
        PreviewHeader.Text = item != null ? $"📄 {item.Name}" : "预览";
        ShowPreviewPanel();
    }

    private void ShowPreviewPanel()
    {
        PreviewSplitterRow.Height = new GridLength(4);
        PreviewRow.Height = _lastPreviewHeight ?? new GridLength(1, GridUnitType.Star);
        PreviewSplitter.Visibility = Visibility.Visible;
        PreviewPanel.Visibility = Visibility.Visible;
    }

    private void HidePreview()
    {
        // 保存当前预览行高度（必须在清 0 之前），支持 Pixel 和 Star 两种类型
        if (PreviewRow.Height.Value > 0)
        {
            _lastPreviewHeight = PreviewRow.Height;
        }

        PreviewImage.Source = null;
        PreviewTextBox.Text = "";
        PreviewRow.Height = new GridLength(0);
        PreviewSplitterRow.Height = new GridLength(0);
        PreviewSplitter.Visibility = Visibility.Collapsed;
        PreviewPanel.Visibility = Visibility.Collapsed;
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
        catch
        {
            // 临时文件清理失败不影响主功能
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