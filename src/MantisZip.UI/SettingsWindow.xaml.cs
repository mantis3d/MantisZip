using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

public partial class SettingsWindow : Window
{
    /// <summary>字号变更时回调主窗口，实时同步预览字号。</summary>
    public Action<int>? OnTextFontSizeChanged { get; set; }

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

        // 先绑定事件再加载设置，确保所有 UI 跟随加载值刷新
        LoadSettings();

        // 任何上下文菜单项变更都要启用L.T(L.Settings_Menu_Btn_Apply)按钮
        void OnChanged(object? s, RoutedEventArgs e) => ApplyShellBtn.IsEnabled = true;
        EnableCompressCheck.Checked += OnChanged;
        EnableCompressCheck.Unchecked += OnChanged;
        EnableQuickCheck.Checked += OnChanged;
        EnableQuickCheck.Unchecked += OnChanged;
        EnableOpenCheck.Checked += OnChanged;
        EnableOpenCheck.Unchecked += OnChanged;
        EnableExtractCheck.Checked += OnChanged;
        EnableExtractCheck.Unchecked += OnChanged;
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

        // 解压
        foreach (ComboBoxItem item in ExtractDestCombo.Items)
            if ((string)item.Tag == s.ExtractDestination) { ExtractDestCombo.SelectedItem = item; break; }
        foreach (ComboBoxItem item in ConflictCombo.Items)
            if ((string)item.Tag == s.FileConflictAction) { ConflictCombo.SelectedItem = item; break; }
        OpenFolderCheck.IsChecked = s.OpenFolderAfterExtract;
        EnableDragExtractCheck.IsChecked = s.EnableDragExtract;

        // 上下文菜单
        EnableCompressCheck.IsChecked = s.EnableCompressMenu;
        EnableQuickCheck.IsChecked = s.EnableQuickCompress;
        EnableOpenCheck.IsChecked = s.EnableOpenMenu;
        EnableExtractCheck.IsChecked = s.EnableExtractMenu;
        CascadeCheck.IsChecked = s.EnableCascadingMenu;
        ShowIconsCheck.IsChecked = s.ShowMenuIcons;
        UpdateShellStatus();

        // 预览
        EnableImagePreviewCheck.IsChecked = s.EnableImagePreview;
        EnableTextPreviewCheck.IsChecked = s.EnableTextPreview;
        MaxTextSizeSlider.Value = s.MaxTextPreviewBytes / (1024 * 1024);
        var mbVal = (int)(s.MaxPreviewFileSize / (1024 * 1024));
        MaxPreviewSizeSlider.Value = mbVal;
        MaxPreviewSizeInput.Text = mbVal.ToString();
        TextFontSizeSlider.Value = s.TextPreviewFontSize;
        UseColorEmojiCheck.IsChecked = s.UseColorEmoji;

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

        // 高级
        EnableDebugLogCheck.IsChecked = s.EnableDebugLogging;
        LogPathText.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        SevenZipPathBox.Text = s.SevenZipPath;

        // 日志隐私脱敏
        foreach (ComboBoxItem item in LogPrivacyModeCombo.Items)
            if ((string)item.Tag == s.LogPrivacyMode) { LogPrivacyModeCombo.SelectedItem = item; break; }

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

        AboutVersionText.Text = AppConstants.Version;

        App.ApplyTextRenderingMode(SettingsTabs);
    }

    private void SaveSettings()
    {
        var s = AppSettings.Instance;

        s.DefaultFormat = ((ComboBoxItem)DefaultFormatCombo.SelectedItem)?.Tag as string ?? "zip";
        s.DefaultLevel = int.TryParse((DefaultLevelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var l) ? l : 5;
        s.CloseAfterCompress = CloseAfterCompressCheck.IsChecked == true;
        s.KeepOriginalExtension = KeepExtCheck.IsChecked == true;

        s.ExtractDestination = ((ComboBoxItem)ExtractDestCombo.SelectedItem)?.Tag as string ?? "ask";
        s.FileConflictAction = ((ComboBoxItem)ConflictCombo.SelectedItem)?.Tag as string ?? "ask";
        s.OpenFolderAfterExtract = OpenFolderCheck.IsChecked == true;
        s.EnableDragExtract = EnableDragExtractCheck.IsChecked == true;

        s.EnableCompressMenu = EnableCompressCheck.IsChecked == true;
        s.EnableQuickCompress = EnableQuickCheck.IsChecked == true;
        s.EnableOpenMenu = EnableOpenCheck.IsChecked == true;
        s.EnableExtractMenu = EnableExtractCheck.IsChecked == true;
        s.EnableCascadingMenu = CascadeCheck.IsChecked == true;
        s.ShowMenuIcons = ShowIconsCheck.IsChecked == true;

        s.EnableImagePreview = EnableImagePreviewCheck.IsChecked == true;
        s.EnableTextPreview = EnableTextPreviewCheck.IsChecked == true;
        s.MaxTextPreviewBytes = (long)MaxTextSizeSlider.Value * 1024 * 1024;
        s.MaxPreviewFileSize = (long)MaxPreviewSizeSlider.Value * 1024 * 1024;
        s.TextPreviewFontSize = (int)TextFontSizeSlider.Value;
        s.UseColorEmoji = UseColorEmojiCheck.IsChecked == true;
        s.PreviewPosition = int.TryParse((PreviewPositionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var pos) ? pos : 1;
        s.InfoPanelOrientation = (InfoPanelOrientationCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Horizontal";

        s.ShowPasswordMatchNotification = ShowPasswordNotifCheck.IsChecked == true;
        s.PasswordRevealByDefault = RevealPasswordCheck.IsChecked == true;

        s.EnableDebugLogging = EnableDebugLogCheck.IsChecked == true;
        s.LogPrivacyMode = (LogPrivacyModeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "off";

        s.SevenZipPath = SevenZipPathBox.Text;

        s.Save();
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
        try
        {
            ShellIntegration.Uninstall();
            ShellIntegration.Install();
            UpdateShellStatus();
            AppMessageBox.Show(L.T(L.Settings_Menu_InstalledMsg), L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
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
        try
        {
            ShellIntegration.UninstallAssociations();
            ShellIntegration.InstallAssociations();
            UpdateAssocStatus();
            AppMessageBox.Show(L.T(L.Settings_Assoc_InstalledMsg), L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(L.TF(L.Settings_Menu_InstallFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UninstallAssoc_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ShellIntegration.UninstallAssociations();
            UpdateAssocStatus();
            AppMessageBox.Show(L.T(L.Settings_Assoc_UninstalledMsg), L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(L.TF(L.Settings_Menu_UninstallFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region L.T(L.Settings_Tab_Preview)L.T(L.Main_Col_Size)输入

    private void MaxPreviewSizeInput_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
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

    #endregion

    private void UninstallBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ShellIntegration.Uninstall();
            UpdateShellStatus();
            AppMessageBox.Show(L.T(L.App_ShellUninstalled), L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(L.TF(L.Settings_Menu_UninstallFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyShellBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 先卸载旧的，再按当前设置安装
            ShellIntegration.Uninstall();
            ShellIntegration.Install();
            UpdateShellStatus();
            ApplyShellBtn.IsEnabled = false;
            AppMessageBox.Show(L.T(L.Settings_Menu_UpdatedMsg), L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(L.TF(L.Settings_Menu_InstallFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        // 上下文菜单有改动则同步安装
        if (ApplyShellBtn.IsEnabled)
        {
            ShellIntegration.Uninstall();
            ShellIntegration.Install();
        }
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BrowseSevenZip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = L.T(L.Settings_SevenZipFilter),
            FileName = "7z.exe"
        };
        if (dialog.ShowDialog() == true)
        {
            SevenZipPathBox.Text = dialog.FileName;
        }
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
        catch { }
    }

    private void OpenStartupLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var startupLog = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                L.T(L.App_MantisZipTitle), "startup.log");
            var dir = Path.GetDirectoryName(startupLog);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Process.Start("explorer.exe", $"/select,\"{startupLog}\"");
        }
        catch { }
    }

    #endregion

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count == 0) return; // 初始填充，非用户操作
        if (LanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string code
            && code != AppSettings.Instance.Language)
        {
            LanguageManager.Instance.SwitchTo(code);
            AppMessageBox.Show(L.T(L.Settings_Language_Restart), L.T(L.App_MantisZipTitle),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
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
}
