using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace MantisZip.UI;

/// <summary>
/// 压缩设置窗口
/// </summary>
public partial class CompressSettingsWindow : Window
{
    private readonly List<string> _sourcePaths = new();

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
            Filter = "所有文件|*.*",
            Title = "选择要压缩的文件",
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
            Description = "选择要压缩的目录"
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
        CompressButton.IsEnabled = _sourcePaths.Count > 0 && !string.IsNullOrEmpty(OutputPathTextBox.Text);
    }

    private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var format = (FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "zip";
        var ext = format == "tar.gz" ? ".tar.gz" : "." + format;

        var dialog = new SaveFileDialog
        {
            Filter = $"{format.ToUpper()} 压缩文件|*{ext}|所有文件|*.*",
            Title = "保存压缩包",
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
            MessageBox.Show("请添加要压缩的文件或目录", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(OutputPathTextBox.Text))
        {
            MessageBox.Show("请选择输出路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 验证密码
        if (EncryptCheckBox.IsChecked == true)
        {
            if (string.IsNullOrEmpty(PasswordBox.Password))
            {
                MessageBox.Show("请输入密码", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (PasswordBox.Password != ConfirmPasswordBox.Password)
            {
                MessageBox.Show("两次输入的密码不一致", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        App.Log("验证通过，开始压缩");

        // 开始压缩
        var outputPath = OutputPathTextBox.Text;
        var format = (FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "zip";
        var level = int.TryParse((LevelCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString(), out var l) ? l : 5;

        App.Log("outputPath: {0}, format: {1}, level: {2}", outputPath, format, level);

try
        {
            // 隐藏设置窗口，显示进度窗口
            this.Hide();
            
            var progressWindow = new ProgressWindow();
            progressWindow.InitCancellation();
            progressWindow.Show();

            // 让 UI 渲染后再开始压缩
            await Task.Delay(100);

            App.Log("准备调用引擎");

            var options = new ArchiveOptions
            {
                CompressionLevel = level,
                Encrypt = EncryptCheckBox.IsChecked == true,
                Password = PasswordBox.Password,
                SplitSize = GetSplitSize()
            };
            App.Log("options: level={0}, encrypt={1}", options.CompressionLevel, options.Encrypt);

            App.Log("创建 Progress 对象...");
            var progress = new Progress<ArchiveProgress>(p =>
            {
                App.Log("【UI】Progress 回调 BEGIN: {0}%", p.PercentComplete);
                try
                {
                    progressWindow.SetProgress(p);
                    App.Log("【UI】SetProgress 调用成功");
                }
                catch (Exception ex)
                {
                    App.Log("【UI】SetProgress 异常: {0}", ex.Message);
                }
                App.Log("【UI】Progress 回调 END");
            });
            App.Log("Progress 对象已创建");

            var engine = ArchiveEngineFactory.GetEngineByExtension(outputPath);
            if (engine == null)
            {
                outputPath = Path.ChangeExtension(outputPath, ".zip");
                engine = new ZipEngine();
            }

            App.Log("引擎: {0}", engine.GetType().Name);

            await engine.CompressAsync(_sourcePaths.ToArray(), outputPath, options, progress, progressWindow.CancellationToken);

            App.Log("压缩完成");

            progressWindow.SetComplete("压缩完成");
            await Task.Delay(500);
            
            // 关闭进度窗口
            progressWindow.Close();
            this.Close();
        }
catch (OperationCanceledException)
        {
            // 用户取消后恢复设置窗口
            this.Show();
        }
        catch (Exception ex)
        {
            this.Show(); // 显示错误前先恢复窗口
            MessageBox.Show($"压缩失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FormatComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateCompressButton();
    }

    private void OutputPathTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateCompressButton();
    }

    private void UpdateCompressButton()
    {
        var hasSource = _sourcePaths.Count > 0;
        var hasOutput = !string.IsNullOrEmpty(OutputPathTextBox.Text);
        CompressButton.IsEnabled = hasSource && hasOutput;
    }
}