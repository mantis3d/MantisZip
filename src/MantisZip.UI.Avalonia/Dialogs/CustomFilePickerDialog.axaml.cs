using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>文件选择器模式。</summary>
public enum PickerMode
{
    /// <summary>选择文件夹，确定返回目录路径。</summary>
    PickFolder,
    /// <summary>保存文件，支持文件名输入，确定返回完整保存路径。</summary>
    SaveFile,
    /// <summary>打开文件，文件筛选器，确定返回文件路径（单文件）。</summary>
    OpenFile,
    /// <summary>解压模式：PickFolder + 底部解压预览区（ResultTreeView 实时冲突检测）。</summary>
    ExtractFolder,
    /// <summary>多选模式：文件+目录混合选择，勾选累积，跨目录保留。</summary>
    PickItems
}

/// <summary>文件浏览列表项。</summary>
public class FileBrowserItem : ObservableObject
{
    public string FullPath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public Bitmap? Icon { get; set; }
    public string SizeText { get; set; } = string.Empty;
    public string ModifiedText { get; set; } = string.Empty;
    public string SubText { get; set; } = string.Empty;
    public bool ShowSubText { get; set; }

    /// <summary>是否可勾选（PickItems 模式文件+目录可勾选；其他模式 false 隐藏勾选框）。</summary>
    public bool CanCheck { get; set; }

    private bool _isSelected;

    /// <summary>勾选状态（PickItems 模式累积）。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>
/// 文件类型下拉项。<see cref="Label"/> 为显示文本，<see cref="Patterns"/> 为扩展名模式列表
///（如 <c>"*.json"</c> / <c>".json"</c> / <c>"json"</c> 均可识别）。
/// 空 <see cref="Patterns"/> 表示不过滤（显示所有文件），仅 OpenFile 模式有意义。
/// </summary>
public sealed record FileTypeOption(string Label, string[] Patterns);

/// <summary>
/// 自建文件/目录选择器的模态壳层：窗口标题栏 + 内嵌 <see cref="CustomFilePickerControl"/> +
/// 底部「确定 / 取消」+ 规则 15 唯一豁免的「系统浏览」原生逃生通道（<see cref="SystemBrowse_Click"/>）。
/// 静态入口：<see cref="ShowFolderAsync"/> / <see cref="ShowSaveFileAsync"/> /
/// <see cref="ShowOpenFileAsync"/> / <see cref="ShowExtractFolderAsync"/> / <see cref="ShowOpenItemsAsync"/>。
/// 确定返回 <see cref="SelectedPath"/>，取消返回 null。
/// </summary>
/// <remarks>
/// 选择器本体（导航 / 选择 / 参数区 / 预览 / 过滤 / 收藏 / 搜索）全部在
/// <see cref="CustomFilePickerControl"/> 内；本类只做「窗口生命周期 + 结果搬运」。
/// </remarks>
public partial class CustomFilePickerDialog : Window
{
    private readonly PickerMode _mode;

    /// <summary>确定后返回的路径。</summary>
    public string? SelectedPath { get; private set; }

    /// <summary>保存模式下用户输入的文件名。</summary>
    public string SelectedFileName { get; private set; } = string.Empty;

    /// <summary>PickItems 模式累积选中的路径列表（按路径排序，FullPath 去重）。</summary>
    public IReadOnlyList<string> SelectedPaths { get; private set; } = Array.Empty<string>();

    // ── 选择器状态透传（保持既有公开面不变，规则 20 / 参数区结构测试依赖这些成员） ──

    /// <summary>本次调用的通用参数项（透传自 <see cref="CustomFilePickerControl.Options"/>）。</summary>
    public IReadOnlyList<PickerOptionItem> Options => Picker.Options;

    /// <summary>参数区容器（透传；参数区结构测试需要访问，x:Name 生成成员语义为 internal）。</summary>
    internal Grid OptionsRow => Picker.OptionsArea;

    /// <summary>本次解压是否保留完整路径（透传）。</summary>
    public bool SelectedPreserveFullPath => Picker.SelectedPreserveFullPath;

    /// <summary>「保留完整路径」开关是否可用（透传）。</summary>
    public bool IsPreserveFullPathToggleAvailable => Picker.IsPreserveFullPathToggleAvailable;

    // ── 窗口级文案（规则 13） ──────────────────────────────────────────────

    public string OkText => LocalizationManager.T("Common_OK");
    public string CancelText => LocalizationManager.T("Common_Cancel");

    // ── Static entry points ────────────────────────────────────────────────

    /// <summary>选择文件夹。返回所选目录路径，取消返回 null。</summary>
    public static Task<string?> ShowFolderAsync(Window owner, string? initialPath = null)
        => ShowInternal(owner, PickerMode.PickFolder, null, null, initialPath, null);

    /// <summary>保存文件。返回完整保存路径，取消返回 null。</summary>
    /// <param name="fileTypes">自定义文件类型下拉项（标签 + 扩展名模式）。为 null 时用默认压缩格式（zip/7z/tar.gz）。</param>
    /// <param name="suggestedFileName">文件名框预填的建议文件名。为 null 时沿用默认文件名（untitled + 默认扩展名）。</param>
    public static Task<string?> ShowSaveFileAsync(Window owner, string? initialPath = null, string? defaultExtension = null,
        IReadOnlyList<FileTypeOption>? fileTypes = null, string? suggestedFileName = null)
        => ShowInternal(owner, PickerMode.SaveFile, null, defaultExtension, initialPath, null, fileTypes: fileTypes, suggestedFileName: suggestedFileName);

    /// <summary>打开文件（单文件）。返回文件路径，取消返回 null。</summary>
    /// <param name="fileExtensions">文件筛选器（扩展名列表，如 "*.zip" / ".zip" 或 "zip"）。null 或空 = 显示所有文件。</param>
    /// <param name="fileTypes">自定义文件类型下拉项（标签 + 扩展名模式）。为 null 时用默认的「压缩文件 / 所有文件」两项，
    /// 其中「压缩文件」沿用 <paramref name="fileExtensions"/> 作为筛选条件。</param>
    public static Task<string?> ShowOpenFileAsync(Window owner, string? initialPath = null, string[]? fileExtensions = null,
        IReadOnlyList<FileTypeOption>? fileTypes = null)
        => ShowInternal(owner, PickerMode.OpenFile, null, null, initialPath, fileExtensions, fileTypes: fileTypes);

    /// <summary>
    /// 解压模式：选择目标目录，底部实时显示解压冲突预览。
    /// 返回 <see cref="ExtractPickResult"/>（目标目录 + 本次「保留完整路径」勾选值），取消返回 null。
    /// </summary>
    /// <remarks>
    /// 不走 <see cref="ShowInternal"/>（后者只返回 <c>string?</c>，无法承载勾选值）。
    /// 确定时无条件写入当前目录，理论上可能为空串，
    /// 故用 <c>!string.IsNullOrEmpty</c> 守卫（空串视为取消）。
    /// </remarks>
    public static async Task<ExtractPickResult?> ShowExtractFolderAsync(
        Window owner, IReadOnlyList<ArchiveItem> entries, string? initialPath = null,
        string currentFolder = "", bool preserveFullPath = true)
    {
        var dialog = new CustomFilePickerDialog(PickerMode.ExtractFolder, entries, null, initialPath, null,
            currentFolder, preserveFullPath)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        await dialog.ShowDialog(owner);
        return !string.IsNullOrEmpty(dialog.SelectedPath)
            ? new ExtractPickResult(dialog.SelectedPath, dialog.SelectedPreserveFullPath)
            : null;
    }

    /// <summary>打开文件/文件夹（多选，PickItems 模式）。返回选中路径列表，取消返回 null。</summary>
    public static async Task<IReadOnlyList<string>?> ShowOpenItemsAsync(Window owner, string? initialPath = null)
    {
        var dialog = new CustomFilePickerDialog(PickerMode.PickItems, null, null, initialPath, null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        // Close(true) 由 Ok 分支设置，Close(false) 由取消设置 → 结果即是否确认
        var confirmed = await dialog.ShowDialog<bool>(owner);
        return confirmed ? dialog.SelectedPaths : null;
    }

    private static async Task<string?> ShowInternal(
        Window owner, PickerMode mode, IReadOnlyList<ArchiveItem>? entries, string? defaultExtension, string? initialPath, string[]? fileExtensions,
        string currentFolder = "", bool preserveFullPath = true,
        IReadOnlyList<FileTypeOption>? fileTypes = null, string? suggestedFileName = null)
    {
        var dialog = new CustomFilePickerDialog(mode, entries, defaultExtension, initialPath, fileExtensions, currentFolder, preserveFullPath, fileTypes, suggestedFileName)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        await dialog.ShowDialog(owner);
        return dialog.SelectedPath;
    }

    // ── Constructors ───────────────────────────────────────────────────────

    /// <summary>设计时无参构造函数。</summary>
    public CustomFilePickerDialog()
        : this(PickerMode.PickFolder, null, null, null, null)
    {
    }

    public CustomFilePickerDialog(PickerMode mode, IReadOnlyList<ArchiveItem>? entries = null, string? defaultExtension = null, string? initialPath = null, string[]? fileExtensions = null,
        string currentFolder = "", bool preserveFullPath = true,
        IReadOnlyList<FileTypeOption>? fileTypes = null, string? suggestedFileName = null)
    {
        InitializeComponent();
        _mode = mode;
        // 窗口自身只有两个按钮文案绑定（确定 / 取消），选择器内部绑定走控件自身 DataContext
        DataContext = this;

        // 标题
        Title = mode switch
        {
            PickerMode.SaveFile => LocalizationManager.T("Picker_SaveFileTitle"),
            PickerMode.OpenFile => LocalizationManager.T("Picker_OpenFileTitle"),
            PickerMode.PickItems => LocalizationManager.T("Picker_PickItemsTitle"),
            _ => LocalizationManager.T("Picker_PickFolderTitle")
        };

        // 窗口高度：解压/多选模式更高
        Height = mode switch
        {
            PickerMode.ExtractFolder => 620,
            PickerMode.PickItems => 500,
            _ => 420
        };

        // 选择器事件接线：确认上抛 → 本窗口关窗；系统浏览上抛 → 本窗口原生调用
        Picker.ConfirmRequested += Picker_ConfirmRequested;
        Picker.SystemBrowseRequested += SystemBrowse_Click;

        // 选择器初始化（等价于原对话框构造函数内的全部初始化副作用）
        Picker.Mode = mode;
        // 模态对话框保持既有交互：PickItems 显示复选框、跨目录累积（= 高级模式）。
        // 简单模式只给设置窗内嵌场景用（点选即结果、切目录清空）。
        Picker.IsSimpleMode = false;
        Picker.Initialize(
            initialPath: initialPath,
            entries: entries,
            defaultExtension: defaultExtension,
            fileExtensions: fileExtensions,
            fileTypes: fileTypes,
            currentFolder: currentFolder,
            preserveFullPath: preserveFullPath,
            suggestedFileName: suggestedFileName);
    }

    /// <summary>选择器已在 <c>ConfirmRequested</c> 中确认单个文件 → 写回结果并关窗。</summary>
    private void Picker_ConfirmRequested(object? sender, string filePath)
    {
        SelectedPath = filePath;
        SelectedFileName = Path.GetFileName(filePath);
        Close(true);
    }

    // ── Confirm / Cancel ───────────────────────────────────────────────────

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        // 选择器负责解析与校验（返回 false = 已弹出提示，窗口保持打开）
        if (!Picker.TryConfirm()) return;

        if (_mode == PickerMode.PickItems)
        {
            SelectedPaths = Picker.SelectedPaths;
        }

        SelectedPath = Picker.ConfirmedPath;
        SelectedFileName = Picker.ConfirmedFileName;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        SelectedPath = null;
        Close(false);
    }

    // ── System dialog fallback（规则 15 唯一豁免点） ────────────────────────

    private async void SystemBrowse_Click(object? sender, EventArgs e)
    {
        try
        {
            var storage = StorageProvider;
            var currentDir = Picker.CurrentPath ?? string.Empty;
            switch (_mode)
            {
                case PickerMode.PickFolder:
                case PickerMode.ExtractFolder:
                {
                    var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        Title = LocalizationManager.T("Picker_PickFolderTitle"),
                        AllowMultiple = false,
                        SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(currentDir)
                    });
                    if (folders.Count >= 1 && folders[0].Path?.LocalPath is { } path)
                    {
                        SelectedPath = path;
                        Close(true);
                    }
                    break;
                }
                case PickerMode.SaveFile:
                {
                    var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = LocalizationManager.T("Picker_SaveFileTitle"),
                        SuggestedFileName = Picker.GetSaveFileName(),
                        SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(currentDir)
                    });
                    if (file?.Path?.LocalPath is { } savePath)
                    {
                        SelectedPath = savePath;
                        SelectedFileName = Path.GetFileName(savePath);
                        Close(true);
                    }
                    break;
                }
                case PickerMode.OpenFile:
                {
                    var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = LocalizationManager.T("Picker_OpenFileTitle"),
                        AllowMultiple = false,
                        SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(currentDir)
                    });
                    if (files.Count >= 1 && files[0].Path?.LocalPath is { } openPath)
                    {
                        SelectedPath = openPath;
                        SelectedFileName = Path.GetFileName(openPath);
                        Close(true);
                    }
                    break;
                }
                case PickerMode.PickItems:
                    // PickItems 模式系统浏览按钮已隐藏；此处仅防御
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomFilePickerDialog] SystemBrowse failed: {ex.Message}");
        }
    }

    /// <summary>关闭时由选择器保存界面状态（左侧面板标签 + 右栏宽度，仅在值确有变化时落盘）。</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        Picker.PersistUiState();
    }
}
