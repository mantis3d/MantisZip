using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Collections.Generic;
using System.Linq;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using Microsoft.Win32;

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

    public MainWindow()
    {
        InitializeComponent();
    }

    #region 拖拽事件

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0 && IsArchiveFile(files[0]))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }
        }
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0 && IsArchiveFile(files[0]))
            {
                await LoadArchiveAsync(files[0]);
            }
        }
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

        var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
        {
            Description = "选择解压目录"
        };

        if (dialog.ShowDialog() == true)
        {
            await ExtractAsync(_currentArchivePath, dialog.SelectedPath);
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
            "MantisZip - 全功能解压缩软件\n\n版本: 1.0.0\n基于 .NET 8 + WPF\n\n支持格式: ZIP, 7z, TAR, GZ, RAR (只读)",
            "关于 MantisZip",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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

    private async Task LoadArchiveAsync(string archivePath)
    {
        try
        {
            SetStatus("正在加载压缩包...");
            _currentArchivePath = archivePath;

            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            if (engine == null)
            {
                MessageBox.Show("不支持的压缩格式", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("就绪");
                return;
            }

            _currentFormat = engine.CanHandle(ArchiveFormat.Zip) ? ArchiveFormat.Zip : ArchiveFormat.SevenZip;

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
                IsEncrypted = i.IsEncrypted
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

            SetStatus($"已加载: {Path.GetFileName(archivePath)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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

        // 延迟选中根目录（等待 TreeView 完成加载）
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (FolderTree.Items.Count > 0)
            {
                var container = FolderTree.ItemContainerGenerator.ContainerFromIndex(0) as TreeViewItem;
                if (container != null)
                {
                    container.IsExpanded = true;
                    container.IsSelected = true;
                }
            }
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
    }

    /// <summary>
    /// 双击进入子目录
    /// </summary>
    private void FileListGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListGrid.SelectedItem is ArchiveItem item)
        {
            if (item.IsDirectory)
            {
                // 进入子目录
                FilterFiles(item.FullPath);
                
                // 更新目录树选中状态
                SelectFolderInTree(item.FullPath);
            }
        }
    }

    /// <summary>
    /// 在目录树中选中指定路径
    /// </summary>
    private void SelectFolderInTree(string path)
    {
        // 简化实现：刷新目录树
        BuildFolderTree();
    }

    #endregion
}

/// <summary>
/// 文件夹树节点
/// </summary>
public class FolderNode
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public List<FolderNode> Children { get; set; } = new();
}

// 本地扩展 ArchiveItem（因为 Core 的没有这些属性）
public class ArchiveItem : Core.Abstractions.ArchiveItem
{
    public string DisplayName { get; set; } = string.Empty;  // 显示用的名称
    public string NameForSort { get; set; } = string.Empty;  // 排序用的名称

    public new string SizeDisplay => IsDirectory ? "--" : FormatSize(Size);
    public new string CompressedSizeDisplay => IsDirectory ? "--" : FormatSize(CompressedSize);

    public new string NameDisplay 
    { 
        get 
        {
            if (!string.IsNullOrEmpty(DisplayName))
            {
                return (IsDirectory ? "📁 " : "📄 ") + DisplayName;
            }
            return (IsDirectory ? "📁 " : "📄 ") + Name;
        } 
    }

    public new int SortOrder => IsDirectory ? 0 : 1;

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