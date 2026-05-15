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
using System.Text;
using Markdig;
using Ude;
using ICSharpCode.SharpZipLib.Zip;

namespace MantisZip.UI;

/// <summary>
/// MainWindow.xaml 的交互逻辑
/// </summary>
public partial class MainWindow : Window
{
    private string? _currentArchivePath;
    private ArchiveFormat _currentFormat;
    private string? _currentPassword;  // 当前压缩包的密码（打开时自动匹配）
    private bool _hasEncryptedArchive; // 当前压缩包是否有加密条目
    private List<ArchiveItem> _allItems = new();  // 存储所有文件项
    private string _currentFolder = "";  // 当前目录
    private string? _previewTempDir;        // 预览临时目录
    private readonly Dictionary<int, double> _lastPreviewSizes = new()
    {
        { 1, 341 }, { 2, 416 }, { 3, 479 }, { 4, 678 }
    }; // 每个位置独立记忆大小（高度:位置1/2/3, 宽度:位置4）
    private int _lastAppliedPosition = 1;    // 上次应用的布局位置，用于检测变更
    private bool _isProgrammaticFilter;      // 编程触发的 FilterFiles，应跳过 SelectionChanged 预览
    private bool _previewPanelEnabled = true; // 工具栏预览开关状态
    private Point _dragStartPoint;           // 文件列表拖拽起点
    private string? _dragTempDir;            // 拖拽提取临时目录
    private bool _isOwnDrag;                 // 当前拖拽是否来自本窗口
    private CancellationTokenSource? _previewCts; // 预览取消令牌

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

// Drag & drop moved to MainWindow.DragDrop.cs
// Menu moved to MainWindow.Menu.cs

// Menu event handlers moved to MainWindow.Menu.cs

// Context menu moved to MainWindow.Menu.cs

    #region 核心功能

    private bool IsArchiveFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".zip" or ".7z" or ".rar" or ".tar" or ".tgz" or ".gz" or ".iso";
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
            ".iso" => ArchiveFormat.Iso,
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

            // 检测加密条目 → 自动尝试匹配已保存的密码
            _currentPassword = null;
            _hasEncryptedArchive = items.Any(i => i.IsEncrypted);
            if (_hasEncryptedArchive)
            {
                var match = App.TryMatchPassword(archivePath, engine, null, false);
                if (match != null)
                {
                    _currentPassword = match.Value.Password;
                    App.LogDebug("LoadArchiveAsync: matched password desc={0}", match.Value.Description);
                }
                else
                {
                    // 所有保存密码都失败 → 弹密码输入框让用户输入
                    App.LogDebug("LoadArchiveAsync: no saved password matched, showing dialog");
                    var pwdDialog = new PasswordDialog(Path.GetFileName(archivePath));
                    pwdDialog.Owner = this;
                    if (pwdDialog.ShowDialog() == true)
                    {
                        var userPwd = pwdDialog.ResultPassword;
                        if (!string.IsNullOrEmpty(userPwd) && App.QuickVerifyPassword(archivePath, userPwd, engine))
                        {
                            _currentPassword = userPwd;
                            if (pwdDialog.RememberPassword)
                            {
                                var patterns = pwdDialog.Patterns.Count > 0
                                    ? pwdDialog.Patterns
                                    : new List<string> { Path.GetFileName(archivePath) };
                                var desc = pwdDialog.Description ?? "";
                                PasswordManager.Instance.AddPassword(userPwd, desc, patterns);
                            }
                        }
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
            UpdatePasswordStatus();

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
            PasswordStatusText.Text = "";
            SetStatus("加载失败");
        }
    }

    private async Task ExtractAsync(string archivePath, string destinationPath)
    {
        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();

        try
        {
            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            if (engine == null) return;

            var ct = progressWindow.CancellationToken;
            var baseProgress = new Progress<ArchiveProgress>(p =>
            {
                progressWindow.Dispatcher.BeginInvoke(() =>
                    progressWindow.SetProgress(p));
            });
            var progress = progressWindow.CreatePauseAwareProgress(baseProgress);

            bool showPwd = _hasEncryptedArchive && AppSettings.Instance.ShowPasswordMatchNotification;

            // 先试已保存密码
            var match = App.TryMatchPassword(archivePath, engine, progressWindow, showPwd);
            if (match != null)
            {
                var (pwd, desc) = match.Value;
                if (showPwd) progressWindow.ShowPasswordMatched(pwd, desc);

                var opts = App.CreateExtractOptions();
                await engine.ExtractAsync(archivePath, destinationPath, pwd, progress, ct, opts);

                progressWindow.Close();
                SetStatus($"解压完成: {Path.GetFileName(archivePath)}");
                if (AppSettings.Instance.OpenFolderAfterExtract) OpenInExplorer(destinationPath);
                return;
            }

            // 所有已保存密码都失败，且压缩包有加密条目 → 弹密码输入框
            if (!_hasEncryptedArchive)
            {
                // 非加密压缩包：直接解压，不需要密码
                var opts = App.CreateExtractOptions();
                await engine.ExtractAsync(archivePath, destinationPath, null, progress, ct, opts);
                progressWindow.Close();
                SetStatus($"解压完成: {Path.GetFileName(archivePath)}");
                if (AppSettings.Instance.OpenFolderAfterExtract) OpenInExplorer(destinationPath);
                return;
            }

            var pwdResult = App.PromptForPassword(archivePath, progressWindow, this);
            if (pwdResult == null)
            {
                progressWindow.Close();
                SetStatus("取消解压");
                return;
            }

            var (userPwd, remember, pwdDesc, pwdPatterns) = pwdResult.Value;
            if (string.IsNullOrEmpty(userPwd)) { progressWindow.Close(); SetStatus("取消解压"); return; }
            bool showPwdManual = _hasEncryptedArchive && AppSettings.Instance.ShowPasswordMatchNotification;

            if (!await App.ExtractWithPasswordAsync(archivePath, destinationPath, engine,
                    userPwd, "手动输入", progressWindow, progress, ct, showPwdManual, remember, pwdDesc, pwdPatterns))
            {
                progressWindow.Close();
                MessageBox.Show("密码错误", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("解压失败");
                return;
            }

            progressWindow.Close();
            SetStatus($"解压完成: {Path.GetFileName(archivePath)}");
            if (AppSettings.Instance.OpenFolderAfterExtract) OpenInExplorer(destinationPath);
        }
        catch (OperationCanceledException)
        {
            progressWindow.Close();
            SetStatus("已取消");
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            if (IsPasswordErrorLocal(ex))
            {
                App.LogDebug("ExtractAsync: 密码错误且用户未提供密码: {0}", ex.Message);
            }
            else
            {
                MessageBox.Show($"解压失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            SetStatus("解压失败");
        }
    }

    /// <summary>
    /// 判断异常是否表示需要密码。与 <see cref="App.IsPasswordError"/> 保持一致。
    /// </summary>
    private static bool IsPasswordErrorLocal(Exception ex)
    {
        var msg = ex.Message.ToLower();
        return msg.Contains("password") || msg.Contains("密码") ||
               msg.Contains("encrypted") || msg.Contains("decrypt") ||
               msg.Contains("encryption") || ex is InvalidOperationException ||
               (ex is ZipException && (msg.Contains("password") || msg.Contains("decrypt")));
    }

    private async Task CompressAsync(string[] sourcePaths, string outputPath)
    {
        try
        {
            SetStatus("正在压缩...");
            ShowProgress(true);

            var options = App.CreateCompressOptions();
            options.CompressionLevel = AppSettings.Instance.DefaultLevel;
            options.Format = ArchiveFormat.Zip;

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

            // 如果压缩包有加密但没密码 → 先让用户输入密码
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
                            var patterns = pwdDialog.Patterns.Count > 0
                                ? pwdDialog.Patterns
                                : new List<string> { Path.GetFileName(archivePath) };
                            var desc = pwdDialog.Description ?? "";
                            PasswordManager.Instance.AddPassword(userPwd, desc, patterns);
                        }
                    }
                    else
                    {
                        ShowProgress(false);
                        MessageBox.Show("密码错误", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        SetStatus("测试失败");
                        return;
                    }
                }
                else
                {
                    ShowProgress(false);
                    SetStatus("已取消");
                    return;
                }
            }

            var result = await engine.TestArchiveAsync(archivePath, password);

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

    // UI helpers moved to MainWindow.UI.cs
    // FolderNode and ArchiveItem moved to MainWindow.UI.cs

    // Preview moved to MainWindow.Preview.cs
}

// FolderNode and ArchiveItem moved to MainWindow.UI.cs