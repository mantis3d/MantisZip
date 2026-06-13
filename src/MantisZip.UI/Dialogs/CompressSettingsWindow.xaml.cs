using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MantisZip.UI.Localization;
using System.Linq;

namespace MantisZip.UI;

/// <summary>
/// 压缩设置窗口
/// </summary>
public partial class CompressSettingsWindow : Window
{
    private readonly List<string> _sourcePaths = new();

    private CompressOutputMode _outputMode = CompressOutputMode.Manual;

    /// <summary>
    /// 是否为独立模式（由 --compress 直接启动，无主窗口）。
    /// 独立模式下压缩完成后自动关闭窗口并退出程序。
    /// </summary>
    public bool StandaloneMode { get; set; }

    public CompressSettingsWindow()
    {
        InitializeComponent();
        LoadDefaultsFromSettings();
    }

    private void OutputMode_Changed(object sender, RoutedEventArgs e)
    {
        if (ManualRadio.IsChecked == true)
            _outputMode = CompressOutputMode.Manual;
        else if (SeparateRadio.IsChecked == true)
            _outputMode = CompressOutputMode.Separate;
        else if (CombinedRadio.IsChecked == true)
            _outputMode = CompressOutputMode.Combined;

        RefreshOutputPathState();
        UpdateCompressButton();
        UpdateCommentDistributionState();
        if (PwdAutoRules != null && PwdAutoRules.IsChecked == true)
            RefreshAutoRules();
    }

    private void RefreshOutputPathState()
    {
        if (OutputPathTextBox == null) return; // InitializeComponent 期间控件尚未创建

        switch (_outputMode)
        {
            case CompressOutputMode.Manual:
                OutputPathTextBox.IsReadOnly = false;
                BrowseOutputButton.Visibility = Visibility.Visible;
                break;
            case CompressOutputMode.Separate:
                OutputPathTextBox.IsReadOnly = true;
                BrowseOutputButton.Visibility = Visibility.Collapsed;
                OutputPathTextBox.Text = L.TF(L.Compress_SeparateSummary, _sourcePaths.Count);
                break;
            case CompressOutputMode.Combined:
                OutputPathTextBox.IsReadOnly = true;
                BrowseOutputButton.Visibility = Visibility.Collapsed;
                RefreshCombinedPath();
                break;
        }
    }

    private void RefreshCombinedPath()
    {
        if (OutputPathTextBox == null) return;
        if (_sourcePaths.Count == 0)
        {
            OutputPathTextBox.Text = "";
            return;
        }

        var commonParent = App.FindCommonParent(_sourcePaths.ToList());
        if (commonParent != null && !App.IsDriveRoot(commonParent))
        {
            var archiveName = Path.GetFileName(commonParent.TrimEnd('\\', '/'));
            var format = (FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "zip";
            var ext = format == "tar.gz" ? ".tar.gz" : "." + format;
            OutputPathTextBox.Text = Path.Combine(commonParent, archiveName + ext);
        }
        else
        {
            // Cross-drive — revert to manual
            AppMessageBox.Show(L.T(L.Compress_CombinedUnavailable), L.T(L.Compress_Title),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ManualRadio.IsChecked = true;
            _outputMode = CompressOutputMode.Manual;
            RefreshOutputPathState();
        }
    }

    private void LoadDefaultsFromSettings()
    {
        var s = AppSettings.Instance;

        // 默认格式
        foreach (System.Windows.Controls.ComboBoxItem item in FormatComboBox.Items)
        {
            if ((string)item.Tag == s.DefaultFormat)
            {
                FormatComboBox.SelectedItem = item;
                break;
            }
        }

        // 默认压缩级别
        foreach (System.Windows.Controls.ComboBoxItem item in LevelCombo.Items)
        {
            if (int.TryParse(item.Tag?.ToString(), out var level) && level == s.DefaultLevel)
            {
                LevelCombo.SelectedItem = item;
                break;
            }
        }
    }

    /// <summary>
    /// 添加源路径（供外部调用，如拖拽）
    /// </summary>
    public void AddSourcePath(string path)
    {
        if (!_sourcePaths.Contains(path))
        {
            _sourcePaths.Add(path);
        }
        UpdateSourceList();
    }

    private void AddFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = L.T(L.Compress_FileFilter),
            Title = L.T(L.Main_SelectFilesTitle),
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                if (!_sourcePaths.Contains(file))
                {
                    _sourcePaths.Add(file);
                }
            }
            UpdateSourceList();
        }
    }

    private void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
        {
            Description = L.T(L.Compress_SelectFolderPrompt)
        };

        if (dialog.ShowDialog() == true)
        {
            if (!_sourcePaths.Contains(dialog.SelectedPath))
            {
                _sourcePaths.Add(dialog.SelectedPath);
            }
            UpdateSourceList();
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SourceListBox.SelectedItem is string selected)
        {
            _sourcePaths.Remove(selected);
            UpdateSourceList();
        }
    }

    private void UpdateSourceList()
    {
        SourceListBox.ItemsSource = null;
        SourceListBox.ItemsSource = _sourcePaths;

        if (_outputMode != CompressOutputMode.Manual)
            RefreshOutputPathState();

        UpdateCompressButton();

        // 源文件变化时重新生成自动规则（设计文档 3.3）
        if (PwdAutoRules != null && PwdAutoRules.IsChecked == true)
            RefreshAutoRules();
    }

    private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var format = (FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "zip";
        var ext = format == "tar.gz" ? ".tar.gz" : "." + format;

        var dialog = new SaveFileDialog
        {
            Filter = L.TF(L.Compress_SaveFilter, format.ToUpper(), ext),
            Title = L.T(L.Compress_Archive_Group),
            FileName = GetDefaultFileName() + ext
        };

        if (dialog.ShowDialog() == true)
        {
            OutputPathTextBox.Text = dialog.FileName;
            UpdateCompressButton();
        }
    }

    private string GetDefaultFileName()
    {
        if (_sourcePaths.Count == 0) return "archive";
        if (_sourcePaths.Count == 1 && File.Exists(_sourcePaths[0]))
            return Path.GetFileNameWithoutExtension(_sourcePaths[0]);
        if (_sourcePaths.Count == 1 && Directory.Exists(_sourcePaths[0]))
            return Path.GetFileName(_sourcePaths[0]);
        return $"archive_{DateTime.Now:yyyyMMddHHmmss}";
    }



    private void SplitSizeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // InitializeComponent 时 SelectedIndex=0 会触发此事件，但 CustomSplitSizeBox 尚未创建
        if (CustomSplitSizeBox == null) return;

        var tag = (SplitSizeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString();
        var isCustom = tag == "-1";
        CustomSplitSizeBox.IsEnabled = isCustom;
        CustomSplitSizeBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        CustomSplitUnit.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        if (isCustom)
            CustomSplitSizeBox.Focus();
    }

    private long GetSplitSize()
    {
        var tag = (SplitSizeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString();
        if (tag == "-1")
        {
            // 自定义分卷大小（以 MB 为单位）
            if (long.TryParse(CustomSplitSizeBox.Text, out var mb) && mb > 0)
                return mb * 1024L * 1024L;
            return 0;
        }
        if (long.TryParse(tag, out var size) && size > 0)
            return size;
        return 0;
    }

    private string? GetComment()
    {
        var comment = CommentTextBox.Text.Trim();
        return string.IsNullOrEmpty(comment) ? null : comment;
    }

    private CommentDistribution GetCommentDistribution()
    {
        if (CommentFirstOnly.IsChecked == true) return CommentDistribution.FirstOnly;
        if (CommentPerLine.IsChecked == true) return CommentDistribution.PerLine;
        return CommentDistribution.AllSame;
    }

    private void UpdateCommentDistributionState()
    {
        if (CommentDistributionPanel == null) return;
        CommentDistributionPanel.IsEnabled = _outputMode == CompressOutputMode.Separate;
    }

    private void TabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl)
        {
            if (PasswordTab.IsSelected)
            {
                RefreshPasswordTabUI();
            }
            else if (CommentTab.IsSelected)
            {
                UpdateCommentFormatState();
                UpdateCommentDistributionState();
            }
        }
    }

    private void UpdateCommentFormatState()
    {
        if (CommentTextBox == null) return;
        var tag = (FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString();
        CommentTextBox.IsEnabled = tag == "zip";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void CompressButton_Click(object sender, RoutedEventArgs e)
    {
        App.Log("CompressButton_Click 开始");
        
        // 验证
        if (_sourcePaths.Count == 0)
        {
            AppMessageBox.Show(L.T(L.Compress_Validation_NoFiles), L.T(L.Compress_Title), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_outputMode == CompressOutputMode.Manual && string.IsNullOrEmpty(OutputPathTextBox.Text))
        {
            AppMessageBox.Show(L.T(L.Compress_Validation_NoOutput), L.T(L.Compress_Title), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 验证密码
        if (EncryptCheckBox.IsChecked == true)
        {
            if (_isUsingLibrary)
            {
                if (_selectedLibraryEntry == null)
                {
                    AppMessageBox.Show(L.T(L.Compress_Pwd_NoEntry), L.T(L.Compress_Title), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                // 如果在明文模式（👁 显示密码），先将 TextBox 内容同步到 PasswordBox，
                // 否则 PasswordBox.Password 可能还是旧的/空的值，导致误判"密码不匹配"
                if (_isPwdRevealed)
                {
                    if (PwdTextBox != null) PasswordBox.Password = PwdTextBox.Text;
                    if (ConfirmPwdTextBox != null) ConfirmPasswordBox.Password = ConfirmPwdTextBox.Text;
                }

                if (string.IsNullOrEmpty(PasswordBox.Password))
                {
                    AppMessageBox.Show(L.T(L.Pwd_Validation_Required), L.T(L.Compress_Title), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (PasswordBox.Password != ConfirmPasswordBox.Password)
                {
                    AppMessageBox.Show(L.T(L.Compress_Validation_PwdMismatch), L.T(L.Compress_Title), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
        }

        App.Log(L.T(L.Shell_Compress));

        var format = (FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "zip";
        var level = int.TryParse((LevelCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString(), out var l) ? l : 5;

        switch (_outputMode)
        {
            case CompressOutputMode.Separate:
                await RunSeparateCompressAsync(format, level);
                break;
            case CompressOutputMode.Combined:
                await RunCombinedCompressAsync(format, level);
                break;
            case CompressOutputMode.Manual:
            default:
                await RunManualCompressAsync(format, level);
                break;
        }
    }

    private async Task RunManualCompressAsync(string format, int level)
    {
        var outputPath = OutputPathTextBox.Text;
        App.Log("Manual compress — outputPath: {0}, format: {1}, level: {2}", outputPath, format, level);

        this.Hide();
        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();
        await Task.Delay(100);

        var request = new CompressRequest
        {
            SourcePaths = new List<string>(_sourcePaths),
            Mode = CompressOutputMode.Manual,
            Format = format,
            CompressionLevel = level,
            Password = GetActivePassword(),
            SplitSize = GetSplitSize(),
            Comment = GetComment(),
            CommentDistribution = GetCommentDistribution(),
            Encrypt = EncryptCheckBox.IsChecked == true,
            OutputPath = outputPath,
            PreserveDirectoryRoot = AppSettings.Instance.PreserveDirectoryRoot,
        };
        var outputPaths = CompressService.GetOutputPaths(request);
        progressWindow.InitBatchMode(outputPaths);
        progressWindow.SetCurrentBatchItem(0);

        try
        {
            bool applyToAll = false;
            Core.Abstractions.CompressConflictAction? chosenAction = null;

            var progress = ProgressWindow.CreateBackgroundProgress(progressWindow);

            var result = await CompressService.CompressAsync(
                request,
                conflictResolver: info =>
                {
                    if (applyToAll && chosenAction.HasValue)
                        return new CompressConflictResolution(chosenAction.Value, null);

                    var dlg = new CompressConflictDialog(info.OutputPath, info.CanAdd, info.SuggestedName);
                    dlg.Owner = progressWindow;
                    dlg.ShowDialog();
                    if (dlg.ApplyToAll) { applyToAll = true; chosenAction = (Core.Abstractions.CompressConflictAction)dlg.ResultAction; }
                    return new CompressConflictResolution((Core.Abstractions.CompressConflictAction)dlg.ResultAction, dlg.CustomName);
                },
                progress,
                progressWindow.CancellationToken);

            progressWindow.FinalizeBatch();
            progressWindow.SetComplete(L.T(L.App_CompressComplete));
            await progressWindow.AutoCloseOrWaitAsync(500, () =>
            {
                if (progressWindow.IsVisible)
                    try { progressWindow.Close(); } catch { }
                SavePasswordAfterCompress();
                this.Close();
            });
        }
        catch (OperationCanceledException)
        {
            this.Show();
        }
        catch (Exception ex)
        {
            this.Show();
            AppMessageBox.Show(L.TF(L.App_CompressFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RunSeparateCompressAsync(string format, int level)
    {
        App.Log("Separate compress — format: {0}, level: {1}", format, level);

        this.Hide();
        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();
        await Task.Delay(100);

        var request = new CompressRequest
        {
            SourcePaths = new List<string>(_sourcePaths),
            Mode = CompressOutputMode.Separate,
            Format = format,
            CompressionLevel = level,
            Password = GetActivePassword(),
            SplitSize = GetSplitSize(),
            Comment = GetComment(),
            CommentDistribution = GetCommentDistribution(),
            Encrypt = EncryptCheckBox.IsChecked == true,
            KeepOriginalExtension = AppSettings.Instance.KeepOriginalExtension,
            PreserveDirectoryRoot = AppSettings.Instance.PreserveDirectoryRoot,
        };
        var outputPaths = CompressService.GetOutputPaths(request);
        progressWindow.InitBatchMode(outputPaths);

        try
        {
            bool applyToAll = false;
            Core.Abstractions.CompressConflictAction? chosenAction = null;

            var progress = ProgressWindow.CreateBackgroundProgress(progressWindow);

            var result = await CompressService.CompressAsync(
                request,
                conflictResolver: info =>
                {
                    if (applyToAll && chosenAction.HasValue)
                        return new CompressConflictResolution(chosenAction.Value, null);

                    return progressWindow.Dispatcher.Invoke(() =>
                    {
                        var dlg = new CompressConflictDialog(info.OutputPath, info.CanAdd, info.SuggestedName);
                        dlg.Owner = progressWindow;
                        dlg.ShowDialog();
                        if (dlg.ApplyToAll) { applyToAll = true; chosenAction = (Core.Abstractions.CompressConflictAction)dlg.ResultAction; }
                        return new CompressConflictResolution((Core.Abstractions.CompressConflictAction)dlg.ResultAction, dlg.CustomName);
                    });
                },
                progress,
                progressWindow.CancellationToken,
                onItemStatus: (index, status) =>
                {
                    if (status == BatchItemStatus.InProgress)
                        progressWindow.SetCurrentBatchItem(index);
                    progressWindow.UpdateBatchItemStatus(index, status);
                });

            progressWindow.FinalizeBatch();
            var summary = L.TF(L.App_CompressSeparateComplete, result.Succeeded, result.Failed);
            progressWindow.SetComplete(summary);
            await progressWindow.AutoCloseOrWaitAsync(500, () =>
            {
                if (progressWindow.IsVisible)
                    try { progressWindow.Close(); } catch { }
                SavePasswordAfterCompress();
                this.Close();
            });
        }
        catch (OperationCanceledException)
        {
            this.Show();
        }
    }

    private async Task RunCombinedCompressAsync(string format, int level)
    {
        App.Log("Combined compress — format: {0}, level: {1}", format, level);

        // Recompute combined path
        var commonParent = App.FindCommonParent(_sourcePaths.ToList());
        string? outputPath;

        if (commonParent != null && !App.IsDriveRoot(commonParent))
        {
            var archiveName = Path.GetFileName(commonParent.TrimEnd('\\', '/'));
            var ext = format == "tar.gz" ? ".tar.gz" : "." + format;
            outputPath = Path.Combine(commonParent, archiveName + ext);
        }
        else
        {
            // Cannot combine across drives
            AppMessageBox.Show(L.T(L.Compress_CombinedUnavailable), L.T(L.Compress_Title),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            this.Show();
            ManualRadio.IsChecked = true;
            _outputMode = CompressOutputMode.Manual;
            RefreshOutputPathState();
            return;
        }

        this.Hide();
        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();
        await Task.Delay(100);

        var request = new CompressRequest
        {
            SourcePaths = new List<string>(_sourcePaths),
            Mode = CompressOutputMode.Combined,
            Format = format,
            CompressionLevel = level,
            Password = GetActivePassword(),
            SplitSize = GetSplitSize(),
            Comment = GetComment(),
            CommentDistribution = GetCommentDistribution(),
            Encrypt = EncryptCheckBox.IsChecked == true,
            OutputPath = outputPath,
            PreserveDirectoryRoot = AppSettings.Instance.PreserveDirectoryRoot,
        };
        var outputPaths = CompressService.GetOutputPaths(request);
        progressWindow.InitBatchMode(outputPaths);
        progressWindow.SetCurrentBatchItem(0);

        try
        {
            bool applyToAll = false;
            Core.Abstractions.CompressConflictAction? chosenAction = null;

            var progress = ProgressWindow.CreateBackgroundProgress(progressWindow);

            var result = await CompressService.CompressAsync(
                request,
                conflictResolver: info =>
                {
                    if (applyToAll && chosenAction.HasValue)
                        return new CompressConflictResolution(chosenAction.Value, null);

                    var dlg = new CompressConflictDialog(info.OutputPath, info.CanAdd, info.SuggestedName);
                    dlg.Owner = progressWindow;
                    dlg.ShowDialog();
                    if (dlg.ApplyToAll) { applyToAll = true; chosenAction = (Core.Abstractions.CompressConflictAction)dlg.ResultAction; }
                    return new CompressConflictResolution((Core.Abstractions.CompressConflictAction)dlg.ResultAction, dlg.CustomName);
                },
                progress,
                progressWindow.CancellationToken);

            progressWindow.FinalizeBatch();
            progressWindow.SetComplete(L.T(L.App_CompressComplete));
            await progressWindow.AutoCloseOrWaitAsync(500, () =>
            {
                if (progressWindow.IsVisible)
                    try { progressWindow.Close(); } catch { }
                SavePasswordAfterCompress();
                this.Close();
            });
        }
        catch (OperationCanceledException)
        {
            this.Show();
        }
        catch (Exception ex)
        {
            this.Show();
            AppMessageBox.Show(L.TF(L.App_CompressFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FormatComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_outputMode == CompressOutputMode.Combined)
            RefreshCombinedPath();
        UpdateCompressButton();
        UpdateCommentFormatState();
        UpdatePasswordFormatState();
        // Null guard: PwdAutoRules may not be created yet during InitializeComponent
        // (FormatComboBox is in General tab, PwdAutoRules is in Password tab)
        if (PwdAutoRules != null && PwdAutoRules.IsChecked == true)
            RefreshAutoRules();
    }

    private void OutputPathTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateCompressButton();
        if (PwdAutoRules != null && PwdAutoRules.IsChecked == true)
            RefreshAutoRules();
    }

    private void UpdateCompressButton()
    {
        if (CompressButton == null) return; // InitializeComponent 期间控件尚未创建

        var hasSource = _sourcePaths.Count > 0;
        if (_outputMode == CompressOutputMode.Manual)
        {
            var hasOutput = !string.IsNullOrEmpty(OutputPathTextBox.Text);
            CompressButton.IsEnabled = hasSource && hasOutput;
        }
        else
        {
            // Separate/Combined: source files are sufficient
            CompressButton.IsEnabled = hasSource;
        }
    }
}