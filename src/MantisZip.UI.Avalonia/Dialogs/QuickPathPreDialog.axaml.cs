using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 系统对话框前置窗。弹系统对话框之前先用 QuickPathControl 选目录。
/// 两种模式：
///   - 选目录模式（IsPickFolderMode=true）：确定后直接返回路径，不弹系统对话框。
///   - 选文件模式（IsPickFolderMode=false）：确定后弹出系统 OpenFileDialog/SaveFileDialog，
///     定位到所选目录，再返回用户选中的文件路径。
/// </summary>
public partial class QuickPathPreDialog : Window
{
    // ── Backing fields ────────────────────────────────────────────────────

    private bool _isPickFolderMode = true;
    private bool _isFileOpenMode;

    // ── Localized string properties (for XAML bindings) ───────────────────

    public string WinTitle => LocalizationManager.T(
        _isPickFolderMode ? "QuickPathPre_Title" : "QuickPathPre_FileTitle");

    public string OkText => LocalizationManager.T("MsgBox_Ok");
    public string CancelText => LocalizationManager.T("MsgBox_Cancel");

    // ── Result properties ─────────────────────────────────────────────────

    /// <summary>
    /// 结果路径。选目录模式=目录路径；选文件模式=用户从系统对话框选中的完整文件路径。
    /// </summary>
    public string? SelectedPath { get; private set; }

    // ── Mode properties ───────────────────────────────────────────────────

    /// <summary>
    /// 选目录模式（默认 true）。true=确定后直接返回目录路径；false=确定后弹系统对话框选文件。
    /// </summary>
    public bool IsPickFolderMode
    {
        get => _isPickFolderMode;
        set
        {
            _isPickFolderMode = value;
            UpdateUIForMode();
        }
    }

    /// <summary>
    /// 文件打开模式（仅 IsPickFolderMode=false 时生效）。true=OpenFileDialog，false=SaveFileDialog。
    /// </summary>
    public bool IsFileOpenMode
    {
        get => _isFileOpenMode;
        set => _isFileOpenMode = value;
    }

    // ── Property proxies (delegated to QuickPathControl) ──────────────────

    public string FileTypeFilter
    {
        get => PathControl.FileTypeFilter;
        set => PathControl.FileTypeFilter = value;
    }

    public string FileOpenFilter
    {
        get => PathControl.FileTypeFilter;
        set => PathControl.FileTypeFilter = value;
    }

    public string DefaultFileName
    {
        get => PathControl.DefaultFileName;
        set => PathControl.DefaultFileName = value;
    }

    public string InitialPath
    {
        get => PathControl.PathText;
        set => PathControl.PathText = value;
    }

    // ── Constructors ──────────────────────────────────────────────────────

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="QuickPathPreDialog(bool, bool)"/>。
    /// </summary>
    [Obsolete("Design-time only")]
    public QuickPathPreDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>
    /// 创建 QuickPathPreDialog。
    /// </summary>
    /// <param name="isPickFolderMode">true=选目录直接返回，false=选目录再弹系统对话框选文件。</param>
    /// <param name="isFileOpenMode">true=OpenFileDialog，false=SaveFileDialog（仅 isPickFolderMode=false 时生效）。</param>
    public QuickPathPreDialog(bool isPickFolderMode, bool isFileOpenMode)
    {
        _isPickFolderMode = isPickFolderMode;
        _isFileOpenMode = isFileOpenMode;
        InitializeComponent();
        DataContext = this;
        UpdateUIForMode();
    }

    // ── Event handlers ───────────────────────────────────────────────────

    private async void Ok_Click(object? sender, RoutedEventArgs e)
    {
        var path = PathControl.PathText;
        if (string.IsNullOrWhiteSpace(path))
        {
            await AppMessageBox.Show(
                LocalizationManager.T("QuickPathPre_PathWarning"),
                LocalizationManager.T("QuickPathPre_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_isPickFolderMode)
        {
            // 选目录模式：直接返回路径
            SelectedPath = path;
            Close(true);
        }
        else
        {
            // 选文件模式：用所选目录作为 SuggestedStartLocation，弹系统对话框
            string? fileResult = null;
            var topLevel = TopLevel.GetTopLevel(this);

            if (topLevel?.StorageProvider is { } storage)
            {
                var folder = await storage.TryGetFolderFromPathAsync(path);

                if (_isFileOpenMode)
                {
                    var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = LocalizationManager.T("QuickPathPre_FileTitle"),
                        AllowMultiple = false,
                        SuggestedStartLocation = folder
                    });

                    if (files.Count >= 1)
                        fileResult = files[0].Path?.LocalPath;
                }
                else
                {
                    var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = LocalizationManager.T("QuickPathPre_FileTitle"),
                        SuggestedFileName = DefaultFileName,
                        SuggestedStartLocation = folder
                    });

                    if (file != null)
                        fileResult = file.Path?.LocalPath;
                }
            }

            if (fileResult != null)
            {
                SelectedPath = fileResult;
                var dir = Path.GetDirectoryName(fileResult);
                if (dir != null)
                    PathControl.AddToHistory(dir);
                Close(true);
            }
            // else: 用户在系统对话框取消，QuickPathPreDialog 保持打开
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    // ── Mode Switch ───────────────────────────────────────────────────────

    private void UpdateUIForMode()
    {
        PathControl.IsFolderMode = true;
        PathControl.IsFileOpenMode = false;

        if (_isPickFolderMode)
        {
            Title = LocalizationManager.T("QuickPathPre_Title");
            PromptText.Text = LocalizationManager.T("QuickPathPre_Prompt");
        }
        else
        {
            Title = LocalizationManager.T("QuickPathPre_FileTitle");
            PromptText.Text = LocalizationManager.T("QuickPathPre_FilePrompt");
        }
    }
}
