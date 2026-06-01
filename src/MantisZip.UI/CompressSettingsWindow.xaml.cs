using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
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
    private readonly List<Core.PasswordEntry> _allPasswordEntries = new();

    private Core.PasswordEntry? _selectedLibraryEntry;

    private enum OutputMode { Manual, Separate, Combined }
    private OutputMode _outputMode = OutputMode.Manual;

    private bool _isUsingLibrary = true; // true=密码库, false=新密码
    private bool _isPwdRevealed;

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
        if (PwdAutoRules != null && PwdAutoRules.IsChecked == true)
            RefreshAutoRules();
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

    private void EncryptCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var enabled = EncryptCheckBox.IsChecked == true;
        PasswordContentGrid.IsEnabled = enabled;
        if (!enabled)
        {
            _selectedLibraryEntry = null;
            PwdSelectedStatus.Text = L.T(L.Compress_Pwd_NoEntry);
        }
    }

    private void PasswordSource_Changed(object sender, RoutedEventArgs e)
    {
        _isUsingLibrary = LibraryRadio.IsChecked == true;
        UpdatePasswordSourceUI();
    }

    /// <summary>
    /// 同步密码来源 RadioButton 的启用/禁用和透明度状态
    /// </summary>
    private void UpdatePasswordSourceUI()
    {
        // Null guards: controls in Password tab may not be created yet during InitializeComponent
        // 只控制内容面板的启用/透明度，RadioButton 本身始终可用
        if (PwdLibraryContent != null)
        {
            PwdLibraryContent.IsEnabled = _isUsingLibrary;
            PwdLibraryContent.Opacity = _isUsingLibrary ? 1.0 : 0.3;
        }
        if (NewPwdContent != null)
        {
            NewPwdContent.IsEnabled = !_isUsingLibrary;
            NewPwdContent.Opacity = _isUsingLibrary ? 0.3 : 1.0;
        }

        if (_isUsingLibrary)
        {
            if (PwdSaveCheck != null)
                PwdSaveCheck.Content = L.T(L.Compress_Pwd_UpdateRules);
            if (PwdDescBox != null)
            {
                PwdDescBox.IsEnabled = false;
                PwdDescBox.IsReadOnly = true;
                if (_selectedLibraryEntry != null)
                    PwdDescBox.Text = _selectedLibraryEntry.Description;
            }
            // 选择密码库条目时不覆盖规则框内容，规则始终由自动规则或用户手动维护
            if (PwdAutoRules != null && PwdAutoRules.IsChecked == true && PwdRulesBox != null)
                RefreshAutoRules();
        }
        else
        {
            if (PwdSaveCheck != null)
                PwdSaveCheck.Content = L.T(L.Compress_Pwd_SaveToLibrary);
            if (PwdDescBox != null)
            {
                PwdDescBox.IsEnabled = true;
                PwdDescBox.IsReadOnly = false;
                PwdDescBox.Text = "";
            }
            if (PwdAutoRules != null && PwdAutoRules.IsChecked == true)
                RefreshAutoRules();
        }
    }

    /// <summary>
    /// 加载密码列表到 ListBox（按 LastUsed 降序）
    /// </summary>
    private void LoadPasswordLibrary()
    {
        _allPasswordEntries.Clear();
        _allPasswordEntries.AddRange(PasswordManager.Instance.GetAllPasswords()
            .OrderByDescending(e => e.LastUsed ?? DateTime.MinValue));
        App.TraceLog("LoadPasswordLibrary: loaded {0} entries", _allPasswordEntries.Count);
        ApplyPasswordFilter();
    }

    /// <summary>
    /// 根据搜索词过滤密码列表
    /// </summary>
    private void ApplyPasswordFilter()
    {
        var query = PwdSearchBox.Text?.Trim() ?? "";
        var placeholder = L.T(L.Compress_Pwd_Search);

        // 搜索框用 {l:L} 占位文字作为 Text，这会在过滤时误过滤掉所有条目。
        // 把占位文字等同为空查询，直到用户实际输入搜索词。
        if (string.Equals(query, placeholder, StringComparison.OrdinalIgnoreCase))
            query = "";

        var filtered = _allPasswordEntries
            .Where(e => string.IsNullOrEmpty(query)
                || e.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.Patterns.Any(p => p.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Null guard: PwdLibraryList may not be created yet during InitializeComponent
        // (PwdSearchBox.TextChanged fires before PwdLibraryList is created in XAML order)
        if (PwdLibraryList == null) return;

        App.TraceLog("ApplyPasswordFilter: query='{0}', total={1}, filtered={2}", query, _allPasswordEntries.Count, filtered.Count);

        if (filtered.Count == 0 && !string.IsNullOrEmpty(query))
        {
            PwdLibraryList.ItemsSource = null;
            PwdLibraryList.Items.Add(L.T(L.Compress_Pwd_EmptySearch));
        }
        else
        {
            PwdLibraryList.ItemsSource = filtered;
        }
    }

    /// <summary>
    /// 搜索框获得焦点时清除占位文字
    /// </summary>
    private void PwdSearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (PwdSearchBox.Text == L.T(L.Compress_Pwd_Search))
            PwdSearchBox.Text = "";
    }

    private void PwdSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyPasswordFilter();
    }

    private void PwdLibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PwdLibraryList.SelectedItem is Core.PasswordEntry entry)
        {
            _selectedLibraryEntry = entry;
            PwdSelectedStatus.Text = L.TF(L.Compress_Pwd_Selected, entry.Description);

            // 同步到共享区
            PwdDescBox.Text = entry.Description;
            PwdDescBox.IsReadOnly = _isUsingLibrary;
            PwdDescBox.IsEnabled = !_isUsingLibrary;

            // 选中密码库条目时清空新密码输入框（设计文档 5）
            PasswordBox.Password = "";
            if (PwdTextBox != null) PwdTextBox.Text = "";
            ConfirmPasswordBox.Password = "";
            if (ConfirmPwdTextBox != null) ConfirmPwdTextBox.Text = "";
        }
        else
        {
            PwdSelectedStatus.Text = L.T(L.Compress_Pwd_NoEntry);
        }
    }

    /// <summary>
    /// 切换密码明文/掩码 — 在 PasswordBox 和 TextBox 之间切换
    /// </summary>
    private void PwdRevealBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPwdRevealed = !_isPwdRevealed;

        if (_isPwdRevealed)
        {
            // 切换到明文 TextBox（主密码 + 确认密码）
            PwdTextBox.Text = PasswordBox.Password;
            if (ConfirmPwdTextBox != null) ConfirmPwdTextBox.Text = ConfirmPasswordBox.Password;

            PasswordBox.Visibility = Visibility.Collapsed;
            PwdTextBox.Visibility = Visibility.Visible;
            ConfirmPasswordBox.Visibility = Visibility.Collapsed;
            if (ConfirmPwdTextBox != null) ConfirmPwdTextBox.Visibility = Visibility.Visible;

            PwdTextBox.Focus();
            PwdTextBox.SelectionStart = PwdTextBox.Text.Length;
        }
        else
        {
            // 切换回掩码 PasswordBox（主密码 + 确认密码）
            PasswordBox.Password = PwdTextBox.Text;
            ConfirmPasswordBox.Password = ConfirmPwdTextBox?.Text ?? "";

            PwdTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            if (ConfirmPwdTextBox != null) ConfirmPwdTextBox.Visibility = Visibility.Collapsed;
            ConfirmPasswordBox.Visibility = Visibility.Visible;

            PasswordBox.Focus();
        }
    }

    /// <summary>
    /// 密码框内容变化时：更新强度 + 清除密码库选中
    /// </summary>
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        OnPasswordContentChanged(PasswordBox.Password);
    }

    /// <summary>
    /// 明文密码框内容变化时：同步处理
    /// </summary>
    private void PwdTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        OnPasswordContentChanged(PwdTextBox.Text);
    }

    private void OnPasswordContentChanged(string? content)
    {
        UpdatePasswordStrength();

        // 用户手动输入密码时，取消密码库的选中
        if (!string.IsNullOrEmpty(content))
        {
            _selectedLibraryEntry = null;
            PwdLibraryList.SelectedItem = null;
            PwdSelectedStatus.Text = L.T(L.Compress_Pwd_NoEntry);
        }

        // 如果密码库有选中但用户改了密码框，自动切换到新密码模式
        if (_isUsingLibrary && _selectedLibraryEntry == null)
        {
            NewPwdRadio.IsChecked = true;
        }
    }

    /// <summary>
    /// 更新密码强度指示
    /// </summary>
    private void UpdatePasswordStrength()
    {
        var pwd = PasswordBox.Password;
        if (string.IsNullOrEmpty(pwd))
        {
            PwdStrengthText.Text = "";
            PwdStrengthText.ClearValue(TextBlock.ForegroundProperty);
            return;
        }

        var hasUpper = pwd.Any(char.IsUpper);
        var hasLower = pwd.Any(char.IsLower);
        var hasDigit = pwd.Any(char.IsDigit);
        var hasSpecial = pwd.Any(c => !char.IsLetterOrDigit(c));
        var variety = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);

        if (pwd.Length < 6 || (pwd.Length < 10 && variety <= 2))
        {
            PwdStrengthText.Text = "● " + L.T(L.Compress_Pwd_Strength_Weak);
            PwdStrengthText.Foreground = new SolidColorBrush(Colors.Red);
        }
        else if (pwd.Length >= 10 && variety >= 3)
        {
            PwdStrengthText.Text = "● " + L.T(L.Compress_Pwd_Strength_Strong);
            PwdStrengthText.Foreground = new SolidColorBrush(Colors.Green);
        }
        else
        {
            PwdStrengthText.Text = "● " + L.T(L.Compress_Pwd_Strength_Medium);
            PwdStrengthText.Foreground = new SolidColorBrush(Colors.Orange);
        }
    }

    /// <summary>
    /// 自动规则 CheckBox 切换
    /// </summary>
    private void PwdAutoRules_Changed(object sender, RoutedEventArgs e)
    {
        // Null guard: PwdRulesBox may not be created yet during InitializeComponent
        if (PwdRulesBox != null)
        {
            var auto = PwdAutoRules.IsChecked == true;
            PwdRulesBox.IsReadOnly = auto;
            PwdRulesBox.IsEnabled = !auto;
        }
        if (PwdAutoRules.IsChecked == true)
        {
            // 无论库模式还是新密码模式，自动规则都基于输出模式生成，不覆盖为选中条目的规则
            RefreshAutoRules();
        }
    }

    /// <summary>
    /// 获取当前激活的密码
    /// </summary>
    private string? GetActivePassword()
    {
        if (EncryptCheckBox.IsChecked != true)
            return null;

        if (_isUsingLibrary)
            return _selectedLibraryEntry?.Password;

        // 明文模式下确保 PasswordBox 与 TextBox 同步（用户可能在 TextBox 中输入）
        if (_isPwdRevealed)
        {
            if (PwdTextBox != null) PasswordBox.Password = PwdTextBox.Text;
            if (ConfirmPwdTextBox != null) ConfirmPasswordBox.Password = ConfirmPwdTextBox.Text;
        }

        return PasswordBox.Password;
    }

    /// <summary>
    /// 刷新自动规则（根据输出模式生成压缩包名 glob）
    /// </summary>
    private void RefreshAutoRules()
    {
        if (!PwdAutoRules.IsChecked == true) return;

        var format = (FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "zip";
        var ext = format == "tar.gz" ? ".tar.gz" : "." + format;

        switch (_outputMode)
        {
            case OutputMode.Manual:
                var manualPath = OutputPathTextBox.Text?.Trim();
                if (!string.IsNullOrEmpty(manualPath))
                {
                    var name = Path.GetFileNameWithoutExtension(manualPath);
                    PwdRulesBox.Text = $"{name}*{ext}";
                }
                break;

            case OutputMode.Separate:
                var rules = new List<string>();
                foreach (var src in _sourcePaths)
                {
                    string baseName;
                    if (File.Exists(src))
                        baseName = Path.GetFileNameWithoutExtension(src);
                    else if (Directory.Exists(src))
                        baseName = Path.GetFileName(src.TrimEnd('\\', '/'));
                    else
                        continue;
                    rules.Add($"{baseName}*{ext}");
                }
                PwdRulesBox.Text = string.Join("\r\n", rules);
                break;

            case OutputMode.Combined:
                var commonParent = App.FindCommonParent(_sourcePaths.ToList());
                if (commonParent != null && !App.IsDriveRoot(commonParent))
                {
                    var archiveName = Path.GetFileName(commonParent.TrimEnd('\\', '/'));
                    PwdRulesBox.Text = $"{archiveName}*{ext}";
                }
                break;
        }
    }

    /// <summary>
    /// 保存密码到密码库（压缩成功后调用）
    /// </summary>
    private void SavePasswordAfterCompress()
    {
        if (PwdSaveCheck.IsChecked != true) return;
        if (EncryptCheckBox.IsChecked != true) return;

        try
        {
            var rules = PwdRulesBox.Text
                ?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .ToList() ?? new List<string>();

            if (rules.Count == 0)
            {
                // 自动生成一条规则
                var format = (FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "zip";
                var ext = format == "tar.gz" ? ".tar.gz" : "." + format;
                rules.Add($"*{ext}");
            }

            if (_isUsingLibrary && _selectedLibraryEntry != null)
            {
                // 更新匹配规则：去重追加
                var updated = false;
                foreach (var rule in rules)
                {
                    if (!_selectedLibraryEntry.Patterns.Contains(rule))
                    {
                        _selectedLibraryEntry.Patterns.Add(rule);
                        updated = true;
                    }
                }
                if (updated)
                {
                    PasswordManager.Instance.UpdatePassword(
                        _selectedLibraryEntry.Id,
                        _selectedLibraryEntry.Password,
                        _selectedLibraryEntry.Description,
                        _selectedLibraryEntry.Patterns);
                    PasswordManager.Instance.MarkUsed(_selectedLibraryEntry.Id);
                    App.LogDebug("Password rules updated for entry: {0}", _selectedLibraryEntry.Description);
                }
            }
            else if (!_isUsingLibrary)
            {
                // 新增密码条目
                var password = PasswordBox.Password;
                var desc = PwdDescBox.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(desc))
                    desc = $"Compressed on {DateTime.Now:yyyy-MM-dd HH:mm}";

                PasswordManager.Instance.AddPassword(password, desc, rules);
                App.LogDebug("Password saved to library: {0}", desc);
            }
        }
        catch (Exception ex)
        {
            App.LogDebug("SavePasswordAfterCompress failed: {0}", ex.Message);
        }
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

    /// <summary>
    /// 切换到加密选项卡时统一刷新所有 UI 状态
    /// </summary>
    private void RefreshPasswordTabUI()
    {
        UpdatePasswordFormatState();
        UpdatePasswordSourceUI();

        // 重新应用自动规则状态，确保 PwdRulesBox 禁用态正确显示
        if (PwdRulesBox != null)
        {
            var auto = PwdAutoRules.IsChecked == true;
            PwdRulesBox.IsReadOnly = auto;
            PwdRulesBox.IsEnabled = !auto;
        }

        LoadPasswordLibrary();
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

    private void UpdatePasswordFormatState()
    {
        // Null guard: PasswordTab TabItem may not be created yet during InitializeComponent
        // (FormatComboBox_SelectionChanged fires before TabItem (Password) is created)
        if (PasswordTab == null) return;

        var tag = (FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString();
        var canEncrypt = tag == "zip" || tag == "7z";
        PasswordTab.IsEnabled = canEncrypt;
        if (!canEncrypt && EncryptCheckBox.IsChecked == true)
        {
            EncryptCheckBox.IsChecked = false;
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
        }
        engine ??= new ZipEngine();

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
                        engine = ArchiveEngineFactory.GetEngineByExtension(outputPath, new ZipEngine());
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
                                addOptions.Password = GetActivePassword();
                                addOptions.Comment = GetComment();
                                var addCtx = ProgressWindow.CreateBackgroundProgress(addProgress);
                                await engine!.AddToArchiveAsync(outputPath, _sourcePaths.ToArray(), addOptions, addCtx, addProgress.CancellationToken);
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
        options.Password = GetActivePassword();
        options.SplitSize = GetSplitSize();
        options.Comment = GetComment();
        options.CommentDistribution = GetCommentDistribution();

        try
        {
            var progress = ProgressWindow.CreateBackgroundProgress(progressWindow);

            // re-acquire engine if outputPath changed (rename)
            engine = ArchiveEngineFactory.GetEngineByExtension(outputPath, new ZipEngine());

            App.Log("引擎: {0}", engine.GetType().Name);

            await engine.CompressAsync(_sourcePaths.ToArray(), outputPath, options, progress, progressWindow.CancellationToken);

            App.Log(L.T(L.App_CompressComplete));
            progressWindow.SetComplete(L.T(L.App_CompressComplete));
            await Task.Delay(500);
            progressWindow.Close();
            SavePasswordAfterCompress();
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
        options.Password = GetActivePassword();
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
                    }
                    engine ??= new ZipEngine();

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
                                engine = ArchiveEngineFactory.GetEngineByExtension(outputPath, new ZipEngine());
                                break;
                            case CompressConflictAction.Add:
                                {
                                    try
                                    {
                                        var addProgress = ProgressWindow.CreateBackgroundProgress(progressWindow);
                                        await engine!.AddToArchiveAsync(outputPath, new[] { sourcePath }, options, addProgress, ct);
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
                    await engine!.CompressAsync(new[] { sourcePath }, outputPath, options, progress, ct);
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
            SavePasswordAfterCompress();
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
        }
        engine ??= new ZipEngine();

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
                        engine = ArchiveEngineFactory.GetEngineByExtension(outputPath, new ZipEngine());
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
                                addOptions.Password = GetActivePassword();
                                addOptions.Comment = GetComment();
                                var addCtx = ProgressWindow.CreateBackgroundProgress(addProgress);
                                await engine!.AddToArchiveAsync(outputPath, _sourcePaths.ToArray(), addOptions, addCtx, addProgress.CancellationToken);
                                addProgress.SetComplete(L.T(L.App_AddToArchiveComplete));
                                await Task.Delay(500);
                                addProgress.Close();
                                SavePasswordAfterCompress();
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
        options.Password = GetActivePassword();
        options.SplitSize = GetSplitSize();
        options.Comment = GetComment();
        options.CommentDistribution = GetCommentDistribution();

        try
        {
            var progress = ProgressWindow.CreateBackgroundProgress(progressWindow);

            // re-acquire engine if outputPath changed (rename)
            engine = ArchiveEngineFactory.GetEngineByExtension(outputPath, new ZipEngine());

            await engine.CompressAsync(_sourcePaths.ToArray(), outputPath, options, progress, progressWindow.CancellationToken);

            App.Log(L.T(L.App_CompressComplete));
            progressWindow.SetComplete(L.T(L.App_CompressComplete));
            await Task.Delay(500);
            progressWindow.Close();
            SavePasswordAfterCompress();
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