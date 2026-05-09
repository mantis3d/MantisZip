using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace MantisZip.UI;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();

        DefaultLevelSlider.ValueChanged += (_, _) =>
            DefaultLevelText.Text = ((int)DefaultLevelSlider.Value).ToString();

        MaxTextSizeSlider.ValueChanged += (_, _) =>
            MaxTextSizeText.Text = $"{(int)MaxTextSizeSlider.Value} MB";

        // 任何上下文菜单项变更都要启用"应用"按钮
        void OnChanged(object? s, RoutedEventArgs e) => InstallShellBtn.IsEnabled = true;
        EnableCompressCheck.Checked += OnChanged;
        EnableCompressCheck.Unchecked += OnChanged;
        EnableQuickCheck.Checked += OnChanged;
        EnableQuickCheck.Unchecked += OnChanged;
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
        DefaultLevelSlider.Value = s.DefaultLevel;
        CloseAfterCompressCheck.IsChecked = s.CloseAfterCompress;
        KeepExtCheck.IsChecked = s.KeepOriginalExtension;

        // 解压
        foreach (ComboBoxItem item in ExtractDestCombo.Items)
            if ((string)item.Tag == s.ExtractDestination) { ExtractDestCombo.SelectedItem = item; break; }
        foreach (ComboBoxItem item in ConflictCombo.Items)
            if ((string)item.Tag == s.FileConflictAction) { ConflictCombo.SelectedItem = item; break; }
        OpenFolderCheck.IsChecked = s.OpenFolderAfterExtract;

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

        // 高级
        SevenZipPathBox.Text = s.SevenZipPath;
        AboutVersionText.Text = AppConstants.Version;
    }

    private void SaveSettings()
    {
        var s = AppSettings.Instance;

        s.DefaultFormat = ((ComboBoxItem)DefaultFormatCombo.SelectedItem)?.Tag as string ?? "zip";
        s.DefaultLevel = (int)DefaultLevelSlider.Value;
        s.CloseAfterCompress = CloseAfterCompressCheck.IsChecked == true;
        s.KeepOriginalExtension = KeepExtCheck.IsChecked == true;

        s.ExtractDestination = ((ComboBoxItem)ExtractDestCombo.SelectedItem)?.Tag as string ?? "ask";
        s.FileConflictAction = ((ComboBoxItem)ConflictCombo.SelectedItem)?.Tag as string ?? "ask";
        s.OpenFolderAfterExtract = OpenFolderCheck.IsChecked == true;

        s.EnableCompressMenu = EnableCompressCheck.IsChecked == true;
        s.EnableQuickCompress = EnableQuickCheck.IsChecked == true;
        s.EnableOpenMenu = EnableOpenCheck.IsChecked == true;
        s.EnableExtractMenu = EnableExtractCheck.IsChecked == true;
        s.EnableCascadingMenu = CascadeCheck.IsChecked == true;
        s.ShowMenuIcons = ShowIconsCheck.IsChecked == true;

        s.EnableImagePreview = EnableImagePreviewCheck.IsChecked == true;
        s.EnableTextPreview = EnableTextPreviewCheck.IsChecked == true;
        s.MaxTextPreviewBytes = (long)MaxTextSizeSlider.Value * 1024 * 1024;

        s.SevenZipPath = SevenZipPathBox.Text;

        s.Save();
    }

    private void UpdateShellStatus()
    {
        var installed = ShellIntegration.IsInstalled;
        ShellStatusText.Text = installed
            ? "✅ Shell 右键菜单已安装"
            : "❌ Shell 右键菜单未安装";
    }

    private void InstallShellBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 先卸载旧的，再按当前设置安装
            ShellIntegration.Uninstall();
            ShellIntegration.Install();
            UpdateShellStatus();
            InstallShellBtn.IsEnabled = false;
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
        if (InstallShellBtn.IsEnabled)
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
