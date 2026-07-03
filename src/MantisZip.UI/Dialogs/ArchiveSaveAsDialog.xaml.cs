using System;
using System.IO;
using System.Windows;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;

namespace MantisZip.UI;

/// <summary>
/// 压缩包另存为格式转换对话框。
/// 使用 QuickPathControl（文件保存模式）+ DynamicFormatOptionsPanel。
/// </summary>
public partial class ArchiveSaveAsDialog : Window
{
    /// <summary>Target save path (directory + filename combined), or null if cancelled.</summary>
    public string? SavePath { get; private set; }

    /// <summary>Selected output format ("zip", "7z", "tar.gz").</summary>
    public string? SelectedFormat { get; private set; }

    /// <summary>Password if encryption enabled, otherwise null.</summary>
    public string? Password { get; private set; }

    private bool _showPlaintext;

    public ArchiveSaveAsDialog(string currentArchivePath)
    {
        InitializeComponent();
        App.ApplyTextRenderingMode(this);

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
            ConversionHint.Text = "转换格式后，部分元数据可能丢失。RAR 等只读格式不支持压缩。";
        else
            ConversionHint.Text = "选择保存位置和文件名，保持原格式。";
    }

    private void FormatComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (FormatComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string tag)
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

    private void EncryptCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        PasswordGrid.IsEnabled = EncryptCheckBox.IsChecked == true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PathControl.PathText))
        {
            AppMessageBox.Show("请选择保存路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            var pwd = PasswordBox.Password;
            var confirm = ConfirmPasswordBox.Password;
            if (string.IsNullOrEmpty(pwd))
            {
                AppMessageBox.Show("请输入密码", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (pwd != confirm)
            {
                AppMessageBox.Show("两次输入的密码不一致", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Password = pwd;
        }

        // Record path to history
        PathHistoryManager.Record(dir);

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}