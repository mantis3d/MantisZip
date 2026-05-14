using System.IO;
using System.Windows;
using MantisZip.Core.Abstractions;

namespace MantisZip.UI;

/// <summary>
/// 文件冲突对话框：覆盖 / 覆盖较旧 / 重命名 / 跳过，支持"应用到全部"。
/// 显示磁盘已有文件与压缩包内文件的对比信息。
/// Topmost 以确保不被进度窗口挡住。
/// </summary>
public partial class ConflictDialog : Window
{
    public FileConflictAction ResultAction { get; private set; }
    public bool ApplyToAll => ApplyAllCheck.IsChecked == true;

    public ConflictDialog(FileConflictInfo info)
    {
        InitializeComponent();

        FileNameText.Text = $"“{Path.GetFileName(info.FilePath)}”";

        // 已有文件信息
        ExistingSizeText.Text = info.ExistingSize.HasValue ? FormatSize(info.ExistingSize.Value) : "--";
        ExistingDateText.Text = info.ExistingModified?.ToString("yyyy-MM-dd HH:mm") ?? "--";

        // 压缩包内条目信息
        EntrySizeText.Text = info.EntrySize.HasValue ? FormatSize(info.EntrySize.Value) : "--";
        EntryDateText.Text = info.EntryModified?.ToString("yyyy-MM-dd HH:mm") ?? "--";

        // 对比结果
        var parts = new List<string>();
        if (info.ExistingSize.HasValue && info.EntrySize.HasValue)
        {
            if (info.EntrySize.Value > info.ExistingSize.Value)
                parts.Add("大小: 压缩包内更大");
            else if (info.EntrySize.Value < info.ExistingSize.Value)
                parts.Add("大小: 磁盘更大");
            else
                parts.Add("大小: 相同");
        }
        else if (info.EntrySize.HasValue && !info.ExistingSize.HasValue)
        {
            // 磁盘文件可能被删除或读不到
        }
        if (info.ExistingModified.HasValue && info.EntryModified.HasValue)
        {
            if (info.EntryModified.Value > info.ExistingModified.Value)
                parts.Add("日期: 压缩包内更新");
            else if (info.EntryModified.Value < info.ExistingModified.Value)
                parts.Add("日期: 磁盘更新");
            else
                parts.Add("日期: 相同");
        }
        CompareResultText.Text = string.Join("  |  ", parts);
    }

    private void Overwrite_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = FileConflictAction.Overwrite;
        DialogResult = true;
    }

    private void OverwriteIfOlder_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = FileConflictAction.OverwriteIfOlder;
        DialogResult = true;
    }

    private void OverwriteIfSmaller_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = FileConflictAction.OverwriteIfSmaller;
        DialogResult = true;
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = FileConflictAction.Rename;
        DialogResult = true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = FileConflictAction.Skip;
        DialogResult = true;
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
