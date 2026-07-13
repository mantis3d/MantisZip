using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 压缩包另存为格式转换对话框。
/// 使用 QuickPathControl（文件保存模式）+ DynamicFormatOptionsPanel。
/// </summary>
public partial class ArchiveSaveAsDialog : Window
{
    /// <summary>Target save path (directory + filename combined), or null if cancelled.</summary>
    public string? SavePath { get; private set; }

    /// <summary>Selected output format ("zip", "7z", "tar.gz").</summary>
    public string SelectedFormat { get; private set; } = "zip";

    /// <summary>Password if encryption enabled, otherwise null.</summary>
    public string? Password { get; private set; }

    // ── Localized bindings ──

    /// <summary>Window title.</summary>
    public string WinTitle => LocalizationManager.T("SaveAs_Title");

    /// <summary>Design-time only constructor.</summary>
    [Obsolete("Design-time only")]
    public ArchiveSaveAsDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public ArchiveSaveAsDialog(string currentArchivePath)
    {
        InitializeComponent();
        DataContext = this;

        // Pre-fill with current archive's name and directory
        var dir = Path.GetDirectoryName(currentArchivePath) ?? "";
        var name = Path.GetFileNameWithoutExtension(currentArchivePath) ?? "archive";
        var ext = Path.GetExtension(currentArchivePath)?.ToLowerInvariant();

        PathControl.PathText = dir;
        PathControl.DefaultFileName = name;
        PathControl.FileName = name + GetDefaultExtension(ext);

        // Select matching format in combobox
        var format = GetFormatFromExtension(ext);
        switch (format)
        {
            case "7z": FormatComboBox.SelectedIndex = 1; break;
            case "tar.gz": FormatComboBox.SelectedIndex = 2; break;
            default: FormatComboBox.SelectedIndex = 0; break;
        }

        SelectedFormat = format;
        FormatOptionsPanel.SelectedFormat = format;

        UpdateConversionHint(currentArchivePath);
    }

    private static string GetFormatFromExtension(string? ext)
    {
        return ext switch
        {
            ".7z" => "7z",
            ".tar" or ".gz" or ".tgz" => "tar.gz",
            _ => "zip"
        };
    }

    private static string GetDefaultExtension(string? ext)
    {
        return ext switch
        {
            ".7z" => ".7z",
            ".tar" or ".gz" or ".tgz" => ".tar.gz",
            _ => ".zip"
        };
    }

    private void UpdateConversionHint(string currentPath)
    {
        var currentExt = Path.GetExtension(currentPath)?.ToLowerInvariant();
        var currentFormat = GetFormatFromExtension(currentExt);

        // Show hint when converting between incompatible formats
        if (SelectedFormat != currentFormat)
            ConversionHintBlock.Text = LocalizationManager.T("SaveAs_Hint_Convert");
        else
            ConversionHintBlock.Text = LocalizationManager.T("SaveAs_Hint_KeepFormat");
    }

    private void FormatComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FormatComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            SelectedFormat = tag;
            FormatOptionsPanel.SelectedFormat = tag;

            // Update file extension
            var ext = tag switch
            {
                "7z" => ".7z",
                "tar.gz" => ".tar.gz",
                _ => ".zip"
            };

            // Update filename extension
            var baseName = Path.GetFileNameWithoutExtension(PathControl.FileName) ?? "archive";
            PathControl.FileName = baseName + ext;

            // Update save filter
            PathControl.FileTypeFilter = tag switch
            {
                "7z" => "7z 文件|*.7z",
                "tar.gz" => "TAR.GZ 文件|*.tar.gz",
                _ => "ZIP 文件|*.zip"
            };

            UpdateConversionHint("");
        }
    }

    private void EncryptCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        PasswordGrid.IsEnabled = EncryptCheckBox.IsChecked == true;
    }

    private async void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PathControl.PathText))
        {
            await AppMessageBox.Show(
                LocalizationManager.T("SaveAs_Warning_SelectPath"),
                LocalizationManager.T("Settings_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                this);
            return;
        }

        var dir = PathControl.PathText;
        var fileName = PathControl.FileName;
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "archive" + GetDefaultExtension(null);

        // Ensure extension matches selected format
        var neededExt = SelectedFormat switch
        {
            "7z" => ".7z",
            "tar.gz" => ".tar.gz",
            _ => ".zip"
        };
        if (!fileName.EndsWith(neededExt, StringComparison.OrdinalIgnoreCase))
            fileName += neededExt;

        SavePath = Path.Combine(dir, fileName);

        // Validate password match
        if (EncryptCheckBox.IsChecked == true)
        {
            var pwd = PasswordBox.Text;
            var confirm = ConfirmPasswordBox.Text;
            if (string.IsNullOrEmpty(pwd))
            {
                await AppMessageBox.Show(
                    LocalizationManager.T("SaveAs_Warning_EnterPassword"),
                    LocalizationManager.T("Settings_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning,
                    this);
                return;
            }
            if (pwd != confirm)
            {
                await AppMessageBox.Show(
                    LocalizationManager.T("SaveAs_Warning_PasswordMismatch"),
                    LocalizationManager.T("Settings_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning,
                    this);
                return;
            }
            Password = pwd;
        }

        // Record path to history
        PathControl.AddToHistory(dir);

        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
