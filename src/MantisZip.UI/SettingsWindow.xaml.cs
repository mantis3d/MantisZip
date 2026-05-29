using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MantisZip.Core.Engines;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

public partial class SettingsWindow : Window
{
    /// <summary>字号变更时回调主窗口，实时同步预览字号。</summary>
    public Action<int>? OnTextFontSizeChanged { get; set; }

    /// <summary>7z.dll 路径（UI 中临时存储，保存时才写入 AppSettings）。</summary>
    private string _sevenZipPath = "";

    public SettingsWindow()
    {
        InitializeComponent();

        MaxTextSizeSlider.ValueChanged += (_, _) =>
            MaxTextSizeText.Text = $"{(int)MaxTextSizeSlider.Value} MB";

        MaxPreviewSizeSlider.ValueChanged += (_, _) =>
            MaxPreviewSizeInput.Text = ((int)MaxPreviewSizeSlider.Value).ToString();

        TextFontSizeSlider.ValueChanged += (_, _) =>
        {
            var size = (int)TextFontSizeSlider.Value;
            TextFontSizeText.Text = size.ToString();
            TextFontSizeSample.FontSize = size;
            OnTextFontSizeChanged?.Invoke(size);
        };

        FontPreviewSizeSlider.ValueChanged += (_, _) =>
        {
            var size = (int)FontPreviewSizeSlider.Value;
            FontPreviewSizeText.Text = size.ToString();
        };

        // 先填充字体列表，再加载设置并从列表中选中已保存的字体
        PopulateFontFamilies();
        LoadSettings();

        // 任何上下文菜单项变更都要启用L.T(L.Settings_Menu_Btn_Apply)按钮
        void OnChanged(object? s, RoutedEventArgs e) => ApplyShellBtn.IsEnabled = true;
        EnableCompressCheck.Checked += OnChanged;
        EnableCompressCheck.Unchecked += OnChanged;
        EnableCompressSeparateCheck.Checked += OnChanged;
        EnableCompressSeparateCheck.Unchecked += OnChanged;
        EnableCompressCombinedCheck.Checked += OnChanged;
        EnableCompressCombinedCheck.Unchecked += OnChanged;
        EnableOpenCheck.Checked += OnChanged;
        EnableOpenCheck.Unchecked += OnChanged;
        EnableExtractHereCheck.Checked += OnChanged;
        EnableExtractHereCheck.Unchecked += OnChanged;
        EnableExtractToNamedCheck.Checked += OnChanged;
        EnableExtractToNamedCheck.Unchecked += OnChanged;
        EnableExtractToCheck.Checked += OnChanged;
        EnableExtractToCheck.Unchecked += OnChanged;
        CascadeCheck.Checked += OnChanged;
        CascadeCheck.Unchecked += OnChanged;
        ShowIconsCheck.Checked += OnChanged;
        ShowIconsCheck.Unchecked += OnChanged;
    }

    private void LoadSettings()
    {
        var s = AppSettings.Instance;

        // 压缩
        foreach (ComboBoxItem item in DefaultFormatCombo.Items)
            if ((string)item.Tag == s.DefaultFormat) { DefaultFormatCombo.SelectedItem = item; break; }
        DefaultLevelCombo.SelectedItem = DefaultLevelCombo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => int.TryParse(i.Tag?.ToString(), out var l) && l == s.DefaultLevel) ?? DefaultLevelCombo.Items[2];
        CloseAfterCompressCheck.IsChecked = s.CloseAfterCompress;
        KeepExtCheck.IsChecked = s.KeepOriginalExtension;
        PreserveRootCheck.IsChecked = s.PreserveDirectoryRoot;

        // 解压
        foreach (ComboBoxItem item in ExtractDestCombo.Items)
            if ((string)item.Tag == s.ExtractDestination) { ExtractDestCombo.SelectedItem = item; break; }
        foreach (ComboBoxItem item in ConflictCombo.Items)
            if ((string)item.Tag == s.FileConflictAction) { ConflictCombo.SelectedItem = item; break; }
        OpenFolderCheck.IsChecked = s.OpenFolderAfterExtract;
        EnableDragExtractCheck.IsChecked = s.EnableDragExtract;

        // 上下文菜单
        EnableCompressCheck.IsChecked = s.EnableCompressMenu;
        EnableCompressSeparateCheck.IsChecked = s.EnableCompressSeparate;
        EnableCompressCombinedCheck.IsChecked = s.EnableCompressCombined;
        EnableOpenCheck.IsChecked = s.EnableOpenMenu;
        EnableExtractHereCheck.IsChecked = s.EnableExtractHereMenu;
        EnableSmartCheck.IsChecked = s.EnableSmartExtractMenu;
        EnableExtractToNamedCheck.IsChecked = s.EnableExtractToNamedMenu;
        EnableExtractToCheck.IsChecked = s.EnableExtractToMenu;
        CascadeCheck.IsChecked = s.EnableCascadingMenu;
        ShowIconsCheck.IsChecked = s.ShowMenuIcons;
        UpdateShellStatus();

        // 预览
        ShowPreviewPanelCheck.IsChecked = s.ShowPreviewPanel;
        EnableImagePreviewCheck.IsChecked = s.EnableImagePreview;
        EnableTextPreviewCheck.IsChecked = s.EnableTextPreview;
        MaxTextSizeSlider.Value = s.MaxTextPreviewBytes / (1024 * 1024);
        MaxTableRowsBox.Text = s.MaxTablePreviewRows.ToString();
        MaxTableColsBox.Text = s.MaxTablePreviewCols.ToString();
        var mbVal = (int)(s.MaxPreviewFileSize / (1024 * 1024));
        MaxPreviewSizeSlider.Value = mbVal;
        MaxPreviewSizeInput.Text = mbVal.ToString();
        TextFontSizeSlider.Value = s.TextPreviewFontSize;
        UseColorEmojiCheck.IsChecked = s.UseColorEmoji;
        // 预览字体（空字符串 = 系统默认，也需选中第一项）
        {
            bool found = false;
            foreach (ComboBoxItem fi in TextFontFamilyCombo.Items)
                if ((string)fi.Tag == s.TextPreviewFontFamily) { TextFontFamilyCombo.SelectedItem = fi; found = true; break; }
            if (!found) TextFontFamilyCombo.SelectedIndex = 0;
        }
        // 预览样本文本
        FontPreviewSampleBox.Text = s.FontPreviewSampleText;
        FontPreviewSizeSlider.Value = s.FontPreviewFontSize;

        // 密码管理
        ShowPasswordNotifCheck.IsChecked = s.ShowPasswordMatchNotification;
        RevealPasswordCheck.IsChecked = s.PasswordRevealByDefault;

        // 预览位置
        foreach (ComboBoxItem item in PreviewPositionCombo.Items)
            if ((string)item.Tag == s.PreviewPosition.ToString()) { PreviewPositionCombo.SelectedItem = item; break; }

        // 信息面板方向
        foreach (ComboBoxItem item in InfoPanelOrientationCombo.Items)
            if ((string)item.Tag == s.InfoPanelOrientation) { InfoPanelOrientationCombo.SelectedItem = item; break; }

        // 文件关联
        UpdateAssocStatus();

        // 高级 — 7z.dll
        _sevenZipPath = s.SevenZipPath;
        UpdateSevenZipStatus();

        // 高级 — 调试日志
        EnableDebugLogCheck.IsChecked = s.EnableDebugLogging;
        LogPathText.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            L.T(L.App_MantisZipTitle), "debug.log");
        // 日志隐私脱敏
        foreach (ComboBoxItem item in LogPrivacyModeCombo.Items)
            if ((string)item.Tag == s.LogPrivacyMode) { LogPrivacyModeCombo.SelectedItem = item; break; }

        // 外观
        foreach (ComboBoxItem item in ThemeCombo.Items)
            if ((string)item.Tag == s.Theme) { ThemeCombo.SelectedItem = item; break; }

        // 语言
        LanguageCombo.Items.Clear();
        foreach (var lang in LanguageManager.Instance.AvailableLanguages)
        {
            LanguageCombo.Items.Add(new ComboBoxItem
            {
                Content = lang.DisplayName,
                Tag = lang.Code
            });
        }
        foreach (ComboBoxItem item in LanguageCombo.Items)
            if ((string)item.Tag == s.Language) { LanguageCombo.SelectedItem = item; break; }
        UpdateLanguageTranslatorText();

        AboutVersionText.Text = AppConstants.Version;

        App.ApplyTextRenderingMode(SettingsTabs);
    }

    private void SaveSettings()
    {
        var s = AppSettings.Instance;

        App.LogDebug("SettingsWindow.SaveSettings: saving settings");
        s.DefaultFormat = ((ComboBoxItem)DefaultFormatCombo.SelectedItem)?.Tag as string ?? "zip";
        s.DefaultLevel = int.TryParse((DefaultLevelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var l) ? l : 5;
        s.CloseAfterCompress = CloseAfterCompressCheck.IsChecked == true;
        s.KeepOriginalExtension = KeepExtCheck.IsChecked == true;
        s.PreserveDirectoryRoot = PreserveRootCheck.IsChecked == true;

        s.ExtractDestination = ((ComboBoxItem)ExtractDestCombo.SelectedItem)?.Tag as string ?? "ask";
        s.FileConflictAction = ((ComboBoxItem)ConflictCombo.SelectedItem)?.Tag as string ?? "ask";
        s.OpenFolderAfterExtract = OpenFolderCheck.IsChecked == true;
        s.EnableDragExtract = EnableDragExtractCheck.IsChecked == true;

        s.EnableCompressMenu = EnableCompressCheck.IsChecked == true;
        s.EnableCompressSeparate = EnableCompressSeparateCheck.IsChecked == true;
        s.EnableCompressCombined = EnableCompressCombinedCheck.IsChecked == true;
        s.EnableOpenMenu = EnableOpenCheck.IsChecked == true;
        s.EnableExtractHereMenu = EnableExtractHereCheck.IsChecked == true;
        s.EnableSmartExtractMenu = EnableSmartCheck.IsChecked == true;
        s.EnableExtractToNamedMenu = EnableExtractToNamedCheck.IsChecked == true;
        s.EnableExtractToMenu = EnableExtractToCheck.IsChecked == true;
        s.EnableCascadingMenu = CascadeCheck.IsChecked == true;
        s.ShowMenuIcons = ShowIconsCheck.IsChecked == true;

        s.EnableImagePreview = EnableImagePreviewCheck.IsChecked == true;
        s.EnableTextPreview = EnableTextPreviewCheck.IsChecked == true;
        s.MaxTextPreviewBytes = (long)MaxTextSizeSlider.Value * 1024 * 1024;
        s.MaxTablePreviewRows = int.TryParse(MaxTableRowsBox.Text, out var rows) ? rows : 100;
        s.MaxTablePreviewCols = int.TryParse(MaxTableColsBox.Text, out var cols) ? cols : 100;
        s.MaxPreviewFileSize = (long)MaxPreviewSizeSlider.Value * 1024 * 1024;
        s.TextPreviewFontSize = (int)TextFontSizeSlider.Value;
        s.TextPreviewFontFamily = (TextFontFamilyCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        s.TextEncodingPreference = ""; // reserved
        s.FontPreviewSampleText = FontPreviewSampleBox.Text;
        s.FontPreviewFontSize = (int)FontPreviewSizeSlider.Value;
        s.ShowPreviewPanel = ShowPreviewPanelCheck.IsChecked == true;
        s.Theme = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Light";
        s.UseColorEmoji = UseColorEmojiCheck.IsChecked == true;
        s.PreviewPosition = int.TryParse((PreviewPositionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var pos) ? pos : 1;
        s.InfoPanelOrientation = (InfoPanelOrientationCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Horizontal";

        s.ShowPasswordMatchNotification = ShowPasswordNotifCheck.IsChecked == true;
        s.PasswordRevealByDefault = RevealPasswordCheck.IsChecked == true;

        // 7z.dll 路径（仅在变更时才持久化并重置引擎）
        if (_sevenZipPath != s.SevenZipPath)
        {
            s.SevenZipPath = _sevenZipPath;
            // 清除引擎初始化标记，下次使用 7z 操作时重新加载新路径
            SevenZipEngine.ResetLibraryPath();
        }

        s.EnableDebugLogging = EnableDebugLogCheck.IsChecked == true;
        s.LogPrivacyMode = (LogPrivacyModeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "off";

        if (!s.Save())
        {
            App.LogDebug("SettingsWindow: failed to save settings");
            AppMessageBox.Show(L.TF(L.Settings_SaveFailed, "settings.json"),
                L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            App.LogDebug("SettingsWindow: settings saved successfully (theme={0}, lang={1}, debugLog={2}, privMode={3})",
                s.Theme, s.Language, s.EnableDebugLogging, s.LogPrivacyMode);
        }
        App.ApplyTextRenderingMode(SettingsTabs);
    }

    /// <summary>
    /// 更新状态文字及三个按钮的启用状态。
    /// </summary>
    private void UpdateShellStatus()
    {
        var installed = ShellIntegration.IsInstalled;
        ShellStatusText.Text = installed
            ? L.T(L.Settings_Menu_Installed)
            : L.T(L.Settings_Menu_NotInstalled);
        InstallBtn.IsEnabled = !installed;
        UninstallBtn.IsEnabled = installed;
        // 应用按钮的状态由 OnChanged 事件单独管理
    }

    private void InstallBtn_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("SettingsWindow: InstallBtn_Click");
        try
        {
            // 先持久化当前 UI 设置，再安装
            SaveSettings();
            ShellIntegration.Uninstall();
            ShellIntegration.Install();
            UpdateShellStatus();
            App.LogDebug("SettingsWindow: shell context menu installed");
            AppMessageBox.Show(L.T(L.Settings_Menu_InstalledMsg), L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.LogDebug("SettingsWindow: shell install failed: {0}", ex.Message);
            AppMessageBox.Show(L.TF(L.Settings_Menu_InstallFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #region L.T(L.Settings_Tab_FileAssoc)

    private void UpdateAssocStatus()
    {
        var installed = ShellIntegration.AreAssociationsInstalled;
        AssocStatusText.Text = installed
            ? L.T(L.Settings_Assoc_Installed)
            : L.T(L.Settings_Assoc_NotInstalled);
        InstallAssocBtn.IsEnabled = !installed;
        UninstallAssocBtn.IsEnabled = installed;
    }

    private void InstallAssoc_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("SettingsWindow: InstallAssoc_Click");
        try
        {
            ShellIntegration.UninstallAssociations();
            ShellIntegration.InstallAssociations();
            UpdateAssocStatus();
            App.LogDebug("SettingsWindow: file associations installed");
            AppMessageBox.Show(L.T(L.Settings_Assoc_InstalledMsg), L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.LogDebug("SettingsWindow: assoc install failed: {0}", ex.Message);
            AppMessageBox.Show(L.TF(L.Settings_Menu_InstallFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UninstallAssoc_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("SettingsWindow: UninstallAssoc_Click");
        try
        {
            ShellIntegration.UninstallAssociations();
            UpdateAssocStatus();
            App.LogDebug("SettingsWindow: file associations uninstalled");
            AppMessageBox.Show(L.T(L.Settings_Assoc_UninstalledMsg), L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.LogDebug("SettingsWindow: assoc uninstall failed: {0}", ex.Message);
            AppMessageBox.Show(L.TF(L.Settings_Menu_UninstallFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region 字体列表

    /// <summary>
    /// 填充字体下拉列表。
    /// </summary>
    private void PopulateFontFamilies()
    {
        TextFontFamilyCombo.Items.Clear();
        // 第一项：系统默认
        TextFontFamilyCombo.Items.Add(new ComboBoxItem
        {
            Content = L.T(L.Settings_Preview_FontDefault),
            Tag = ""
        });
        foreach (var family in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
        {
            TextFontFamilyCombo.Items.Add(new ComboBoxItem
            {
                Content = family.Source,
                Tag = family.Source
            });
        }
    }

    #endregion

    #region L.T(L.Settings_Tab_Preview)L.T(L.Main_Col_Size)输入

    private void IntInput_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        // 只允许输入数字
        foreach (var c in e.Text)
        {
            if (!char.IsDigit(c)) { e.Handled = true; return; }
        }
    }

    private void MaxPreviewSizeInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (int.TryParse(MaxPreviewSizeInput.Text, out var val))
        {
            if (val < 1) { MaxPreviewSizeInput.Text = "1"; val = 1; }
            if (val > 100) { MaxPreviewSizeInput.Text = "100"; val = 100; }
            if (val != (int)MaxPreviewSizeSlider.Value)
                MaxPreviewSizeSlider.Value = val;
        }
    }

    private void MaxTableRows_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (int.TryParse(MaxTableRowsBox.Text, out var val))
        {
            if (val < 3) { MaxTableRowsBox.Text = "3"; }
            if (val > 1000) { MaxTableRowsBox.Text = "1000"; }
        }
    }

    private void MaxTableCols_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (int.TryParse(MaxTableColsBox.Text, out var val))
        {
            if (val < 3) { MaxTableColsBox.Text = "3"; }
            if (val > 1000) { MaxTableColsBox.Text = "1000"; }
        }
    }

    #endregion

    private void UninstallBtn_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("SettingsWindow: UninstallBtn_Click");
        try
        {
            ShellIntegration.Uninstall();
            UpdateShellStatus();
            App.LogDebug("SettingsWindow: shell context menu uninstalled");
            AppMessageBox.Show(L.T(L.App_ShellUninstalled), L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.LogDebug("SettingsWindow: shell uninstall failed: {0}", ex.Message);
            AppMessageBox.Show(L.TF(L.Settings_Menu_UninstallFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyShellBtn_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("SettingsWindow: ApplyShellBtn_Click");
        try
        {
            // 先持久化 UI 当前的设置到 AppSettings.Instance，再安装
            SaveSettings();
            ShellIntegration.Uninstall();
            ShellIntegration.Install();
            UpdateShellStatus();
            ApplyShellBtn.IsEnabled = false;
            App.LogDebug("SettingsWindow: shell context menu updated and applied");
            AppMessageBox.Show(L.T(L.Settings_Menu_UpdatedMsg), L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.LogDebug("SettingsWindow: shell apply failed: {0}", ex.Message);
            AppMessageBox.Show(L.TF(L.Settings_Menu_InstallFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("SettingsWindow: SaveBtn_Click");
        SaveSettings();
        // 上下文菜单有改动则同步安装
        if (ApplyShellBtn.IsEnabled)
        {
            App.LogDebug("SettingsWindow: applying shell menu changes before close");
            ShellIntegration.Uninstall();
            ShellIntegration.Install();
        }
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    #region 调试日志

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPathText.Text);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Process.Start("explorer.exe", $"/select,\"{LogPathText.Text}\"");
        }
        catch (Exception logEx)
        {
            App.TraceLog("OpenLogFolder_Click: {0}", logEx.Message);
        }
    }

    #endregion

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is ComboBoxItem item && item.Tag is string theme
            && theme != AppSettings.Instance.Theme)
        {
            var oldTheme = AppSettings.Instance.Theme;
            App.LogDebug("SettingsWindow: ThemeCombo changed: '{0}' -> '{1}'", oldTheme, theme);

            // 临时保存，用户可以取消
            AppSettings.Instance.Theme = theme;
            AppSettings.Instance.Save();

            var result = AppMessageBox.Show(
                L.T(L.Settings_Appearance_ThemeRestart_Msg),
                L.T(L.Settings_Appearance_ThemeRestart_Title),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(Environment.ProcessPath!);
                Application.Current.Shutdown();
            }
            else
            {
                // 用户取消 → 恢复旧主题并还原 ComboBox
                AppSettings.Instance.Theme = oldTheme;
                AppSettings.Instance.Save();
                ThemeCombo.SelectionChanged -= ThemeCombo_SelectionChanged;
                foreach (ComboBoxItem ci in ThemeCombo.Items)
                    if ((string)ci.Tag == oldTheme) { ThemeCombo.SelectedItem = ci; break; }
                ThemeCombo.SelectionChanged += ThemeCombo_SelectionChanged;
            }
        }
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count == 0) return; // 初始填充，非用户操作
        var code = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (code != null && code != AppSettings.Instance.Language)
        {
            App.LogDebug("SettingsWindow: Language changed: '{0}' -> '{1}'", AppSettings.Instance.Language, code);
            LanguageManager.Instance.SwitchTo(code);
            AppMessageBox.Show(L.T(L.Settings_Language_Restart), L.T(L.App_MantisZipTitle),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        // 无论是否切换语言（可能是相同语言换回），都更新翻译者文本
        if (code != null)
            UpdateLanguageTranslatorText(code);
    }

    private void UpdateLanguageTranslatorText(string? langCode = null)
    {
        langCode ??= (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrEmpty(langCode))
        {
            LanguageTranslatorText.Text = "";
            return;
        }
        var translator = LanguageManager.Instance.GetLanguageTranslator(langCode);
        LanguageTranslatorText.Text = !string.IsNullOrEmpty(translator)
            ? translator
            : L.T(L.Settings_Language_NoTranslator);
    }

    private void LogPrivacyHelp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LogPrivacyHelpDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void CleanTemp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), L.T(L.App_MantisZipTitle));
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
                AppMessageBox.Show(L.T(L.Settings_CleanPreviewDone), L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                AppMessageBox.Show(L.T(L.Settings_CleanPreviewNone), L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(L.TF(L.Settings_CleanPreviewFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #region 7z.dll 配置

    /// <summary>
    /// 更新 7z.dll 状态显示。
    /// 根据当前配置的路径和文件是否存在更新 UI。
    /// </summary>
    private void UpdateSevenZipStatus()
    {
        // 确定当前有效路径：用户指定优先，否则用自动探测的
        var effectivePath = !string.IsNullOrEmpty(_sevenZipPath)
            ? _sevenZipPath
            : SevenZipEngine.SevenZipDllPath;

        var exists = File.Exists(effectivePath);

        SevenZipCurrentPathText.Text = effectivePath;

        if (exists)
        {
            SevenZipStatusIcon.Text = "✅";
            SevenZipStatusText.Text = L.T(L.Settings_Advanced_SevenZipFound);
            SevenZipStatusText.Foreground = System.Windows.Media.Brushes.Green;
        }
        else
        {
            SevenZipStatusIcon.Text = "❌";
            SevenZipStatusText.Text = L.T(L.Settings_Advanced_SevenZipNotFound);
            SevenZipStatusText.Foreground = System.Windows.Media.Brushes.Red;
        }

        // 重置按钮仅在用户指定了路径时启用
        SevenZipResetBtn.IsEnabled = !string.IsNullOrEmpty(_sevenZipPath);
    }

    private void SevenZipBrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = L.T(L.Settings_Advanced_SevenZipSelectDll),
            Filter = "7z.dll|7z.dll|动态链接库 (*.dll)|*.dll|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.Is64BitProcess ? "x64" : "x86"),
        };

        if (dialog.ShowDialog() == true)
        {
            _sevenZipPath = dialog.FileName;
            UpdateSevenZipStatus();
        }
    }

    private void SevenZipResetBtn_Click(object sender, RoutedEventArgs e)
    {
        _sevenZipPath = "";
        UpdateSevenZipStatus();
    }

    #endregion
}
