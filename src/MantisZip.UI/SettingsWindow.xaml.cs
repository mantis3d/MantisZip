using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

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
            MaxPreviewSizeText.Text = $"{(int)MaxPreviewSizeSlider.Value} MB";

        TextFontSizeSlider.ValueChanged += (_, _) =>
        {
            var size = (int)TextFontSizeSlider.Value;
            TextFontSizeText.Text = size.ToString();
            TextFontSizeSample.FontSize = size;
            OnTextFontSizeChanged?.Invoke(size);
        };

        // 先绑定事件再加载设置，确保所有 UI 跟随加载值刷新
        LoadSettings();

        // 任何上下文菜单项变更都要启用"应用"按钮
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
        MaxPreviewSizeSlider.Value = s.MaxPreviewFileSize / (1024 * 1024);
        TextFontSizeSlider.Value = s.TextPreviewFontSize;

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
        AboutVersionText.Text = AppConstants.Version;
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
        s.PreviewPosition = int.TryParse((PreviewPositionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var pos) ? pos : 1;
        s.InfoPanelOrientation = (InfoPanelOrientationCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Horizontal";

        s.ShowPasswordMatchNotification = ShowPasswordNotifCheck.IsChecked == true;
        s.PasswordRevealByDefault = RevealPasswordCheck.IsChecked == true;

        s.EnableDebugLogging = EnableDebugLogCheck.IsChecked == true;

        s.SevenZipPath = SevenZipPathBox.Text;

        s.Save();
    }

    /// <summary>
    /// 更新状态文字及三个按钮的启用状态。
    /// </summary>
    private void UpdateShellStatus()
    {
        var installed = ShellIntegration.IsInstalled;
        ShellStatusText.Text = installed
            ? "✅ Shell 右键菜单已安装"
            : "❌ Shell 右键菜单未安装";
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
            MessageBox.Show("Shell 右键菜单已安装", "MantisZip", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"安装失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #region 文件关联

    private void UpdateAssocStatus()
    {
        var installed = ShellIntegration.AreAssociationsInstalled;
        AssocStatusText.Text = installed
            ? "✅ 文件关联已安装"
            : "❌ 文件关联未安装";
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
            MessageBox.Show("文件关联已安装", "MantisZip", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"安装失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UninstallAssoc_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ShellIntegration.UninstallAssociations();
            UpdateAssocStatus();
            MessageBox.Show("文件关联已卸载", "MantisZip", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"卸载失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    private void UninstallBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ShellIntegration.Uninstall();
            UpdateShellStatus();
            MessageBox.Show("Shell 右键菜单已卸载", "MantisZip", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"卸载失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("上下文菜单已更新", "MantisZip", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"安装失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
            Filter = "可执行文件|7z.exe|所有文件|*.*",
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
                "MantisZip", "startup.log");
            var dir = Path.GetDirectoryName(startupLog);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Process.Start("explorer.exe", $"/select,\"{startupLog}\"");
        }
        catch { }
    }

    #endregion

    private void CleanTemp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "MantisZip");
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
                MessageBox.Show("预览临时文件已清理", "MantisZip", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("没有需要清理的临时文件", "MantisZip", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"清理失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
