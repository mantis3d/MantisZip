using System.IO;
using System.Windows;
using MantisZip.Core.Utils;
using Microsoft.Win32;

namespace MantisZip.UI;

/// <summary>
/// 系统对话框前置窗。弹系统对话框之前先用 QuickPathControl 选⭐🕐⚡目录。
/// 两种模式：
///   - 选目录模式（IsPickFolderMode=true）：确定后直接返回路径，不弹系统对话框。
///   - 选文件模式（IsPickFolderMode=false）：确定后弹出系统 OpenFileDialog/SaveFileDialog，
///     定位到所选目录，再返回用户选中的文件路径。
/// </summary>
public partial class QuickPathPreDialog : Window
{
    // ── Dependency Properties ─────────────────────────────────────────────────

    /// <summary>选目录模式（true=选目录直接返回，false=选目录再弹系统对话框选文件）</summary>
    public static readonly DependencyProperty IsPickFolderModeProperty =
        DependencyProperty.Register(nameof(IsPickFolderMode), typeof(bool), typeof(QuickPathPreDialog),
            new PropertyMetadata(true, OnModeChanged));

    /// <summary>文件打开模式（仅 IsPickFolderMode=false 时生效，true=OpenFileDialog，false=SaveFileDialog）</summary>
    public static readonly DependencyProperty IsFileOpenModeProperty =
        DependencyProperty.Register(nameof(IsFileOpenMode), typeof(bool), typeof(QuickPathPreDialog),
            new PropertyMetadata(false));

    // ── CLR Properties ────────────────────────────────────────────────────────

    /// <summary>选目录模式（默认 true）。true=确定后直接返回目录路径；false=确定后弹系统对话框选文件。</summary>
    public bool IsPickFolderMode
    {
        get => (bool)GetValue(IsPickFolderModeProperty);
        set => SetValue(IsPickFolderModeProperty, value);
    }

    /// <summary>文件打开模式（仅 IsPickFolderMode=false 生效）。true=OpenFileDialog，false=SaveFileDialog。</summary>
    public bool IsFileOpenMode
    {
        get => (bool)GetValue(IsFileOpenModeProperty);
        set => SetValue(IsFileOpenModeProperty, value);
    }

    /// <summary>结果路径。选目录模式=目录路径；选文件模式=用户从系统对话框选中的完整文件路径。</summary>
    public string? SelectedPath { get; private set; }

    /// <summary>文件保存模式的过滤条件（仅 SaveFileDialog）。</summary>
    public string FileTypeFilter
    {
        get => PathControl.FileTypeFilter;
        set => PathControl.FileTypeFilter = value;
    }

    /// <summary>文件打开模式的过滤条件（仅 OpenFileDialog）。</summary>
    public string FileOpenFilter
    {
        get => PathControl.FileOpenFilter;
        set => PathControl.FileOpenFilter = value;
    }

    /// <summary>默认文件名（仅 SaveFileDialog）。</summary>
    public string DefaultFileName
    {
        get => PathControl.DefaultFileName;
        set => PathControl.DefaultFileName = value;
    }

    /// <summary>初始路径。</summary>
    public string InitialPath
    {
        get => PathControl.PathText;
        set => PathControl.PathText = value;
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public QuickPathPreDialog()
    {
        InitializeComponent();
        UpdateUIForMode();
    }

    // ── Event Handlers ────────────────────────────────────────────────────────

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var path = PathControl.PathText;
        if (string.IsNullOrWhiteSpace(path))
        {
            AppMessageBox.Show("请选择一个路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (IsPickFolderMode)
        {
            // 选目录模式：直接返回路径
            SelectedPath = path;
            DialogResult = true;
        }
        else
        {
            // 选文件模式：用所选目录作为 InitialDirectory，弹系统对话框
            string? fileResult = null;

            if (IsFileOpenMode)
            {
                var dlg = new OpenFileDialog
                {
                    InitialDirectory = path,
                    CheckFileExists = true,
                    Filter = FileOpenFilter
                };
                if (dlg.ShowDialog(Owner ?? this) == true)
                    fileResult = dlg.FileName;
            }
            else
            {
                var dlg = new SaveFileDialog
                {
                    InitialDirectory = path,
                    Filter = FileTypeFilter,
                    FileName = DefaultFileName
                };
                if (dlg.ShowDialog(Owner ?? this) == true)
                    fileResult = dlg.FileName;
            }

            if (fileResult != null)
            {
                SelectedPath = fileResult;
                var dir = Path.GetDirectoryName(fileResult);
                if (dir != null)
                    PathHistoryManager.Record(dir);
                DialogResult = true;
            }
            // else: 用户在系统对话框取消，QuickPathPreDialog 保持打开
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    // ── Mode Switch ───────────────────────────────────────────────────────────

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is QuickPathPreDialog dlg)
            dlg.UpdateUIForMode();
    }

    private void UpdateUIForMode()
    {
        PathControl.IsFolderMode = IsPickFolderMode;
        PathControl.IsFileOpenMode = false;

        if (IsPickFolderMode)
        {
            Title = "选择路径";
            PromptText.Text = "选择目标路径:";
        }
        else
        {
            Title = "选择文件";
            PromptText.Text = "先选择目录，再选择文件:";
        }
    }
}
