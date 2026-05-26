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
    private bool _hasEncryptedArchive; // 当前压缩包是否有加密条目
    private string? _archiveComment;   // 压缩包注释（仅 ZIP 格式支持）
    private List<ArchiveItem> _allItems = new();  // 存储所有文件项
    private readonly Dictionary<string, (int Count, long Size, long CompressedSize)> _dirStats = new(); // 目录统计缓存
    private string _currentFolder = "";  // 当前目录
    private string? _previewTempDir;        // L.T(L.Settings_Tab_Preview)临时目录
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
    private bool _transparentBgEnabled;
    private ImageAnimationController? _gifController;
    private TextBox? _gifFrameInput;
    private TextBlock? _gifFrameTotal;

    public MainWindow()
    {
        InitializeComponent();

        // 列标题右键菜单（切换列显隐）
        if (Resources["ColumnHeaderContextMenu"] is ContextMenu headerMenu)
            headerMenu.Opened += ColumnHeaderContextMenu_Opened;

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

    private class ColumnState
    {
        public double Width { get; set; }
        public bool Visible { get; set; }
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

                // 恢复各列的宽度和可见性（按位置索引匹配）
                if (obj?.ColumnStates != null && obj.ColumnStates.Count > 0)
                {
                    for (int i = 0; i < FileListGrid.Columns.Count && i < obj.ColumnStates.Count; i++)
                    {
                        var state = obj.ColumnStates[i];
                        var col = FileListGrid.Columns[i];
                        if (state.Width > 0)
                            col.Width = new DataGridLength(state.Width);
                        // 跳过名称列（不允许隐藏）
                        if (i > 0)
                            col.Visibility = state.Visible ? Visibility.Visible : Visibility.Collapsed;
                    }
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

            // 保存各列的宽度和可见性
            var columnStates = new List<ColumnState>();
            foreach (var col in FileListGrid.Columns)
            {
                columnStates.Add(new ColumnState
                {
                    Width = col.Width.Value,   // 当前宽度值（用户拖拽后为像素值）
                    Visible = col.Visibility == Visibility.Visible
                });
            }

            var obj = new WindowSize
            {
                Width = Width,
                Height = Height,
                TreeColumnWidth = treeWidth,
                PreviewRowHeight = previewHeight,
                PreviewColumnWidth = previewColumnWidth,
                PreviewTreeHeight = previewTreeHeight,
                PreviewFilesHeight = previewFilesHeight,
                ColumnStates = columnStates
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

    /// <summary>
    /// 读取L.T(L.Compress_Archive_Group)注释（仅 ZIP 格式支持，通过 SharpZipLib 读取 EOCD 注释字段）。
    /// 其他格式返回 null。
    /// </summary>
    private static string? ReadArchiveComment(string archivePath, ArchiveFormat format)
    {
        if (format != ArchiveFormat.Zip) return null;

        try
        {
            using var zf = new ZipFile(archivePath, ICSharpCode.SharpZipLib.Zip.StringCodec.Default);
            var comment = zf.ZipFileComment;
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

            var items = await engine.ListEntriesAsync(archivePath);

            // Update overlay to show entry count
            ArchiveLoadingText.Text = L.TF(L.Main_Status_ProcessingEntries, items.Count);

            // 检测加密条目 → 自动尝试匹配已保存的密码
            _currentPassword = null;
            _hasEncryptedArchive = items.Any(i => i.IsEncrypted);
            if (_hasEncryptedArchive)
            {
                var match = App.TryMatchPassword(archivePath, engine, null, false, out var limitReached);
                if (match != null)
                {
                    _currentPassword = match.Value.Password;
                    App.LogDebug("LoadArchiveAsync: matched password desc={0}", match.Value.Description);
                }
                else
                {
                    if (limitReached)
                    {
                        AppMessageBox.Show(L.TF(L.PwdMgr_AutoTry_LimitReached, 100),
                            L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
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
                                try { PasswordManager.Instance.AddPassword(userPwd, desc, patterns); }
                                catch (Exception pwdEx) { App.LogDebug("LoadArchiveAsync: failed to save password: {0}", pwdEx.Message); }
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
            var totalCompressed = items.Sum(i => i.CompressedSize);
            ArchiveInfoText.Text = L.TF(L.Main_ArchiveInfo, items.Count, FormatSize(totalSize), FormatSize(totalCompressed));
            ArchiveStatsText.Text = L.TF(L.Main_ArchiveStats, items.Count, FormatSize(totalSize), FormatSize(totalCompressed));

            SetStatus(L.TF(L.Main_Status_Loaded, Path.GetFileName(archivePath)));
            UpdatePasswordStatus();
            UpdateSmartExtractBtnState();

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
            SetStatus(L.T(L.Main_Status_LoadFailed));

            // Reset UI state on error
            ArchiveLoadingOverlay.Visibility = Visibility.Collapsed;
            FileListPanel.Visibility = Visibility.Collapsed;
            DropHint.Visibility = Visibility.Visible;
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
                var opts = App.CreateExtractOptions();
                await engine.ExtractAsync(archivePath, destinationPath, null, progress, ct, opts);
                progressWindow.Close();
                SetStatus(L.TF(L.Main_Status_ExtractDone, Path.GetFileName(archivePath)));
                if (AppSettings.Instance.OpenFolderAfterExtract) OpenInExplorer(destinationPath);
                return;
            }

            var pwdResult = App.PromptForPassword(archivePath, progressWindow, this);
            if (pwdResult == null)
            {
                progressWindow.Close();
                SetStatus(L.T(L.Main_Status_ExtractCancel));
                return;
            }

            var (userPwd, remember, pwdDesc, pwdPatterns) = pwdResult.Value;
            if (string.IsNullOrEmpty(userPwd)) { progressWindow.Close(); SetStatus(L.T(L.Main_Status_ExtractCancel)); return; }
            bool showPwdManual = _hasEncryptedArchive && AppSettings.Instance.ShowPasswordMatchNotification;

            if (!await App.ExtractWithPasswordAsync(archivePath, destinationPath, engine,
                    userPwd, L.T(L.Main_ForceLoadPwd), progressWindow, progress, ct, showPwdManual, remember, pwdDesc, pwdPatterns))
            {
                progressWindow.Close();
                AppMessageBox.Show(L.T(L.Main_Status_WrongPwd), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus(L.T(L.Main_Status_WrongPwd));
                return;
            }

            progressWindow.Close();
            SetStatus(L.TF(L.Main_Status_ExtractDone, Path.GetFileName(archivePath)));
            if (AppSettings.Instance.OpenFolderAfterExtract) OpenInExplorer(destinationPath);
        }
        catch (OperationCanceledException)
        {
            progressWindow.Close();
            SetStatus(L.T(L.Main_Status_AddCancel));
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            if (IsPasswordErrorLocal(ex))
            {
                App.LogDebug("ExtractAsync: {0} {1} {2}", L.T(L.Main_Status_WrongPwd), L.T(L.PwdEdit_PasswordLabel), ex.Message);
            }
            else
            {
                AppMessageBox.Show(L.TF(L.Main_Status_ExtractFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            SetStatus(L.TF(L.Main_Status_ExtractFailed, ""));
        }
    }

    /// <summary>
    /// 判断异常L.T(L.MsgBox_Yes)L.T(L.MsgBox_No)表示需要L.T(L.PwdMgr_Col_Password)。与 <see cref="App.IsPasswordError"/> 保持一致。
    /// </summary>
    private static bool IsPasswordErrorLocal(Exception ex)
    {
        var msg = ex.Message.ToLower();
        return msg.Contains("password") || msg.Contains(L.T(L.PwdMgr_Col_Password)) ||
               msg.Contains("encrypted") || msg.Contains("decrypt") ||
               msg.Contains("encryption") ||
               (ex is ZipException && (msg.Contains("password") || msg.Contains("decrypt")));
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

            var engine = ArchiveEngineFactory.GetEngineByExtension(outputPath) ?? new ZipEngine();
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
        try
        {
            SetStatus(L.T(L.Main_Status_Testing));
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
                            try { PasswordManager.Instance.AddPassword(userPwd, desc, patterns); }
                            catch (Exception pwdEx) { App.LogDebug("ReExtractWithPassword: failed to save password: {0}", pwdEx.Message); }
                        }
                    }
                    else
                    {
                        ShowProgress(false);
                        AppMessageBox.Show(L.T(L.Main_Status_WrongPwd), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                        SetStatus(L.T(L.Main_Status_TestFailed));
                        return;
                    }
                }
                else
                {
                    ShowProgress(false);
                    SetStatus(L.T(L.Main_Status_AddCancel));
                    return;
                }
            }

            var result = await engine.TestArchiveAsync(archivePath, password);

            AppMessageBox.Show(
                result ? L.T(L.Main_Status_TestResultOK) : L.T(L.Main_Status_TestResultBad),
                L.T(L.Main_Status_TestTitle),
                MessageBoxButton.OK,
                result ? MessageBoxImage.Information : MessageBoxImage.Warning);

            SetStatus(result ? L.T(L.Main_Status_TestPassed) : L.T(L.Main_Status_TestFailed));
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(L.TF(L.Main_Status_TestFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus(L.T(L.Main_Status_TestFailed));
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