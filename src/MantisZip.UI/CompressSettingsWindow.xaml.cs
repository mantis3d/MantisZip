using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// 压缩设置窗口
/// </summary>
public partial class CompressSettingsWindow : Window
{
    private readonly List<string> _sourcePaths = new();

    private enum OutputMode { Manual, Separate, Combined }
    private OutputMode _outputMode = OutputMode.Manual;

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
            _outputMode = OutputMode.Manual;
        else if (SeparateRadio.IsChecked == true)
            _outputMode = OutputMode.Separate;
        else if (CombinedRadio.IsChecked == true)
            _outputMode = OutputMode.Combined;

        RefreshOutputPathState();
        UpdateCompressButton();
        UpdateCommentDistributionState();
    }

    private void RefreshOutputPathState()
    {
        if (OutputPathTextBox == null) return; // InitializeComponent 期间控件尚未创建

        switch (_outputMode)
        {
            case OutputMode.Manual:
                OutputPathTextBox.IsReadOnly = false;
                BrowseOutputButton.Visibility = Visibility.Visible;
                break;
            case OutputMode.Separate:
                OutputPathTextBox.IsReadOnly = true;
                BrowseOutputButton.Visibility = Visibility.Collapsed;
                OutputPathTextBox.Text = L.TF(L.Compress_SeparateSummary, _sourcePaths.Count);
                break;
            case OutputMode.Combined:
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
            _outputMode = OutputMode.Manual;
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

        if (_outputMode != OutputMode.Manual)
            RefreshOutputPathState();

        UpdateCompressButton();
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

    private void EncryptCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        PasswordGrid.IsEnabled = EncryptCheckBox.IsChecked == true;
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
        CommentDistributionPanel.IsEnabled = _outputMode == OutputMode.Separate;
    }

    private void TabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl && CommentTab.IsSelected)
        {
            UpdateCommentFormatState();
            UpdateCommentDistributionState();
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

        if (_outputMode == OutputMode.Manual && string.IsNullOrEmpty(OutputPathTextBox.Text))
        {
            AppMessageBox.Show(L.T(L.Compress_Validation_NoOutput), L.T(L.Compress_Title), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 验证密码
        if (EncryptCheckBox.IsChecked == true)
        {
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

        App.Log(L.T(L.Shell_Compress));

        var format = (FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "zip";
        var level = int.TryParse((LevelCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString(), out var l) ? l : 5;

        switch (_outputMode)
        {
            case OutputMode.Separate:
                await RunSeparateCompressAsync(format, level);
                break;
            case OutputMode.Combined:
                await RunCombinedCompressAsync(format, level);
                break;
            case OutputMode.Manual:
            default:
                await RunManualCompressAsync(format, level);
                break;
        }
    }

    private async Task RunManualCompressAsync(string format, int level)
    {
        var outputPath = OutputPathTextBox.Text;
        App.Log("Manual compress — outputPath: {0}, format: {1}, level: {2}", outputPath, format, level);

        // 冲突处理 — 在隐藏窗口之前进行
        var engine = ArchiveEngineFactory.GetEngineByExtension(outputPath);
        if (engine == null)
        {
            outputPath = Path.ChangeExtension(outputPath, ".zip");
            engine = new ZipEngine();
        }

        if (File.Exists(outputPath))
        {
            bool canAdd = engine is not null and not TarGzEngine;
            var dlg = new CompressConflictDialog(outputPath, canAdd, Path.GetFileName(App.GetUniquePath(outputPath)));
            if (dlg.ShowDialog() == true)
            {
                switch (dlg.ResultAction)
                {
                    case CompressConflictAction.Cancel:
                        return;
                    case CompressConflictAction.Rename:
                        outputPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".",
                            dlg.CustomName ?? Path.GetFileName(App.GetUniquePath(outputPath)));
                        engine = ArchiveEngineFactory.GetEngineByExtension(outputPath) ?? new ZipEngine();
                        break;
                    case CompressConflictAction.Add:
                        {
                            this.Hide();
                            var addProgress = new ProgressWindow();
                            addProgress.InitCancellation();
                            addProgress.Show();
                            await Task.Delay(100);
                            try
                            {
                                var addOptions = App.CreateCompressOptions();
                                addOptions.Encrypt = EncryptCheckBox.IsChecked == true;
                                addOptions.Password = PasswordBox.Password;
                                addOptions.Comment = GetComment();
                                var addCtx = ProgressWindow.CreateBackgroundProgress(addProgress);
                                await engine.AddToArchiveAsync(outputPath, _sourcePaths.ToArray(), addOptions, addCtx, addProgress.CancellationToken);
                                addProgress.SetComplete(L.T(L.App_AddToArchiveComplete));
                                await Task.Delay(500);
                                addProgress.Close();
                                this.Close();
                            }
                            catch (OperationCanceledException) { this.Show(); addProgress.Close(); }
                            catch (Exception ex)
                            {
                                this.Show(); addProgress.Close();
                                AppMessageBox.Show(L.TF(L.App_CompressFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                            return;
                        }
                    case CompressConflictAction.Overwrite:
                    default:
                        break;
                }
            }
            else
            {
                return; // 用户取消对话框
            }
        }

        this.Hide();
        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();
        await Task.Delay(100);

        var options = App.CreateCompressOptions();
        options.CompressionLevel = level;
        options.Encrypt = EncryptCheckBox.IsChecked == true;
        options.Password = PasswordBox.Password;
        options.SplitSize = GetSplitSize();
        options.Comment = GetComment();
        options.CommentDistribution = GetCommentDistribution();

        try
        {
            var progress = ProgressWindow.CreateBackgroundProgress(progressWindow);

            // re-acquire engine if outputPath changed (rename)
            engine = ArchiveEngineFactory.GetEngineByExtension(outputPath) ?? new ZipEngine();

            App.Log("引擎: {0}", engine.GetType().Name);

            await engine.CompressAsync(_sourcePaths.ToArray(), outputPath, options, progress, progressWindow.CancellationToken);

            App.Log(L.T(L.App_CompressComplete));
            progressWindow.SetComplete(L.T(L.App_CompressComplete));
            await Task.Delay(500);
            progressWindow.Close();
            this.Close();
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

        var options = App.CreateCompressOptions();
        options.CompressionLevel = level;
        options.Encrypt = EncryptCheckBox.IsChecked == true;
        options.Password = PasswordBox.Password;
        options.SplitSize = GetSplitSize();

        // 注释分配 — 独立模式下每人压缩包可分配不同注释
        var baseComment = GetComment();
        var distribution = GetCommentDistribution();
        string[]? perLineComments = distribution == CommentDistribution.PerLine && baseComment != null
            ? baseComment.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray()
            : null;

        var ext = format == "tar.gz" ? ".tar.gz" : "." + format;
        int success = 0, fail = 0;

        try
        {
            var ct = progressWindow.CancellationToken;

            for (int i = 0; i < _sourcePaths.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var sourcePath = _sourcePaths[i];
                // 按分配策略设置每个压缩包的注释
                options.Comment = distribution switch
                {
                    CommentDistribution.AllSame => baseComment,
                    CommentDistribution.FirstOnly => i == 0 ? baseComment : null,
                    CommentDistribution.PerLine => i < (perLineComments?.Length ?? 0) ? perLineComments![i] : null,
                    _ => baseComment
                };
                progressWindow.SetProgress(new ArchiveProgress
                {
                    PercentComplete = (int)((double)i / _sourcePaths.Count * 100),
                    FilePercentComplete = null,
                    CurrentFile = Path.GetFileName(sourcePath)
                });

                try
                {
                    // Determine parent dir and base name
                    string parentDir, fileNameWithoutExt;
                    if (File.Exists(sourcePath))
                    {
                        parentDir = Path.GetDirectoryName(sourcePath) ?? ".";
                        fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        parentDir = Path.GetDirectoryName(sourcePath.TrimEnd('\\', '/')) ?? ".";
                        fileNameWithoutExt = Path.GetFileName(sourcePath.TrimEnd('\\', '/'));
                    }
                    else
                    {
                        fail++;
                        continue;
                    }

                    var outputPath = Path.Combine(parentDir, fileNameWithoutExt + ext);
                    var engine = ArchiveEngineFactory.GetEngineByExtension(outputPath);
                    if (engine == null)
                    {
                        outputPath = Path.ChangeExtension(outputPath, ".zip");
                        engine = new ZipEngine();
                    }

                    // 冲突处理 — per item
                    if (File.Exists(outputPath))
                    {
                        bool canAdd = engine is not null and not TarGzEngine;
                        var conflictResult = await progressWindow.Dispatcher.InvokeAsync(() =>
                        {
                            var dlg = new CompressConflictDialog(outputPath, canAdd,
                                Path.GetFileName(App.GetUniquePath(outputPath)));
                            return dlg.ShowDialog() == true ? dlg : null;
                        });

                        if (conflictResult == null)
                        {
                            fail++;
                            continue; // 用户取消
                        }

                        switch (conflictResult.ResultAction)
                        {
                            case CompressConflictAction.Cancel:
                                fail++;
                                continue;
                            case CompressConflictAction.Rename:
                                outputPath = Path.Combine(parentDir,
                                    conflictResult.CustomName ?? Path.GetFileName(App.GetUniquePath(outputPath)));
                                engine = ArchiveEngineFactory.GetEngineByExtension(outputPath) ?? new ZipEngine();
                                break;
                            case CompressConflictAction.Add:
                                {
                                    try
                                    {
                                        var addProgress = ProgressWindow.CreateBackgroundProgress(progressWindow);
                                        await engine.AddToArchiveAsync(outputPath, new[] { sourcePath }, options, addProgress, ct);
                                        success++;
                                    }
                                    catch (Exception addEx)
                                    {
                                        App.Log("Separate add-to-archive failed for {0}: {1}", sourcePath, addEx.Message);
                                        fail++;
                                    }
                                    continue; // skip to next item
                                }
                            case CompressConflictAction.Overwrite:
                            default:
                                break;
                        }
                    }

                    var progress = ProgressWindow.CreateBackgroundProgress(progressWindow);
                    await engine.CompressAsync(new[] { sourcePath }, outputPath, options, progress, ct);
                    success++;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    App.Log("Separate compress failed for {0}: {1}", sourcePath, ex.Message);
                    fail++;
                }
            }

            progressWindow.SetComplete(L.TF(L.App_CompressSeparateComplete, success, fail));
            await Task.Delay(500);
            progressWindow.Close();
            this.Close();
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
            this.Show(); // restore window
            ManualRadio.IsChecked = true;
            _outputMode = OutputMode.Manual;
            RefreshOutputPathState();
            return;
        }

        // 冲突处理 — 在隐藏窗口之前进行
        var engine = ArchiveEngineFactory.GetEngineByExtension(outputPath);
        if (engine == null)
        {
            outputPath = Path.ChangeExtension(outputPath, ".zip");
            engine = new ZipEngine();
        }

        if (File.Exists(outputPath))
        {
            bool canAdd = engine is not null and not TarGzEngine;
            var dlg = new CompressConflictDialog(outputPath, canAdd, Path.GetFileName(App.GetUniquePath(outputPath)));
            if (dlg.ShowDialog() == true)
            {
                switch (dlg.ResultAction)
                {
                    case CompressConflictAction.Cancel:
                        return;
                    case CompressConflictAction.Rename:
                        outputPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".",
                            dlg.CustomName ?? Path.GetFileName(App.GetUniquePath(outputPath)));
                        engine = ArchiveEngineFactory.GetEngineByExtension(outputPath) ?? new ZipEngine();
                        break;
                    case CompressConflictAction.Add:
                        {
                            this.Hide();
                            var addProgress = new ProgressWindow();
                            addProgress.InitCancellation();
                            addProgress.Show();
                            await Task.Delay(100);
                            try
                            {
                                var addOptions = App.CreateCompressOptions();
                                addOptions.Encrypt = EncryptCheckBox.IsChecked == true;
                                addOptions.Password = PasswordBox.Password;
                                addOptions.Comment = GetComment();
                                var addCtx = ProgressWindow.CreateBackgroundProgress(addProgress);
                                await engine.AddToArchiveAsync(outputPath, _sourcePaths.ToArray(), addOptions, addCtx, addProgress.CancellationToken);
                                addProgress.SetComplete(L.T(L.App_AddToArchiveComplete));
                                await Task.Delay(500);
                                addProgress.Close();
                                this.Close();
                            }
                            catch (OperationCanceledException) { this.Show(); addProgress.Close(); }
                            catch (Exception ex)
                            {
                                this.Show(); addProgress.Close();
                                AppMessageBox.Show(L.TF(L.App_CompressFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                            return;
                        }
                    case CompressConflictAction.Overwrite:
                    default:
                        break;
                }
            }
            else
            {
                return; // 用户取消对话框
            }
        }

        this.Hide();
        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();
        await Task.Delay(100);

        var options = App.CreateCompressOptions();
        options.CompressionLevel = level;
        options.Encrypt = EncryptCheckBox.IsChecked == true;
        options.Password = PasswordBox.Password;
        options.SplitSize = GetSplitSize();
        options.Comment = GetComment();
        options.CommentDistribution = GetCommentDistribution();

        try
        {
            var progress = ProgressWindow.CreateBackgroundProgress(progressWindow);

            // re-acquire engine if outputPath changed (rename)
            engine = ArchiveEngineFactory.GetEngineByExtension(outputPath) ?? new ZipEngine();

            await engine.CompressAsync(_sourcePaths.ToArray(), outputPath, options, progress, progressWindow.CancellationToken);

            App.Log(L.T(L.App_CompressComplete));
            progressWindow.SetComplete(L.T(L.App_CompressComplete));
            await Task.Delay(500);
            progressWindow.Close();
            this.Close();
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
        if (_outputMode == OutputMode.Combined)
            RefreshCombinedPath();
        UpdateCompressButton();
        UpdateCommentFormatState();
    }

    private void OutputPathTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateCompressButton();
    }

    private void UpdateCompressButton()
    {
        if (CompressButton == null) return; // InitializeComponent 期间控件尚未创建

        var hasSource = _sourcePaths.Count > 0;
        if (_outputMode == OutputMode.Manual)
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