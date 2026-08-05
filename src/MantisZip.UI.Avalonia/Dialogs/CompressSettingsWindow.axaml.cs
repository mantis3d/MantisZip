using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 压缩设置窗口。通过 ShowDialog 返回 bool 结果，
/// 调用方在结果为 true 后读取 ViewModel 的属性构造 CompressRequest。
/// </summary>
public partial class CompressSettingsWindow : Window
{
    private bool _loaded;
    /// <summary>
    /// ViewModel，公开属性供调用方关闭后读取。
    /// </summary>
    public CompressSettingsViewModel ViewModel { get; }

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="CompressSettingsWindow(IReadOnlyList{string})"/>。
    /// </summary>
    public CompressSettingsWindow()
    {
        InitializeComponent();
        ViewModel = new CompressSettingsViewModel(Array.Empty<string>());
        DataContext = ViewModel;
        SubscribeViewModel();
    }

    public CompressSettingsWindow(IReadOnlyList<string> sourcePaths)
    {
        InitializeComponent();

        ViewModel = new CompressSettingsViewModel(sourcePaths);

        // 设置文件保存浏览回调
        ViewModel.BrowseOutput = async () =>
        {
            // 格式联动：根据当前 DefaultFormat 计算默认扩展名（弹窗内 SaveFile 模式应用）
            var defaultExt = ViewModel.DefaultFormat switch
            {
                "tar.gz" => ".tar.gz",
                "7z" => ".7z",
                _ => ".zip"
            };
            return await CustomFilePickerDialog.ShowSaveFileAsync(this, initialPath: ViewModel.OutputPath, defaultExtension: defaultExt);
        };

        // 设置文件/文件夹选择回调
        ViewModel.PickFiles = async () =>
        {
            // 将第一个源路径的目录作为「场景相关路径」传给 picker，作为默认路径优先级链的 context 来源
            var contextPath = ResolveContextPath(sourcePaths);
            return await CustomFilePickerDialog.ShowOpenItemsAsync(this, initialPath: contextPath);
        };

        // 设置关闭回调
        ViewModel.CloseAction = async (result) =>
        {
            if (result)
            {
                // 关闭前将 DynamicFormatOptionsPanel 当前值快照到 ViewModel，
                // 供压缩流程读取本次对话框设置的高级选项（仅本次压缩生效，不写回 AppSettings）
                SnapshotFormatOptionsToViewModel();
            }
            Close(result);
            await Task.CompletedTask;
        };

DataContext = ViewModel;
        SubscribeViewModel();

        // 浏览回调：保存文件对话框（带格式联动），选中后将完整路径拆为目录+文件名填入。
        // 返回目录（QuickPathPicker 只收目录）；文件名落在独立 OutputFileName 控件。
        OutputPathPicker.BrowseAction = BrowseOutputPathAsync;

        Loaded += OnLoaded;
    }

    /// <summary>
    /// QuickPathPicker 浏览：打开保存文件对话框（带格式联动），返回目录并拆分写入文件名。
    /// </summary>
    private async Task<string?> BrowseOutputPathAsync(Window? owner, string? current)
    {
        if (ViewModel.OutputMode != CompressOutputMode.Manual) return null;
        var defaultExt = ViewModel.DefaultFormat switch
        {
            "tar.gz" => ".tar.gz",
            "7z" => ".7z",
            _ => ".zip"
        };
        var path = await CustomFilePickerDialog.ShowSaveFileAsync(
            owner ?? this, initialPath: current ?? ViewModel.OutputDirectory, defaultExtension: defaultExt);
        if (string.IsNullOrEmpty(path)) return null;

        // 拆分完整路径 → 目录 + 文件名
        var dir = System.IO.Path.GetDirectoryName(path);
        var name = System.IO.Path.GetFileName(path);
        foreach (var ext in new[] { ".tar.gz", ".7z", ".zip" })
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^ext.Length];
                break;
            }
        }
        if (string.IsNullOrEmpty(dir)) return null;
        ViewModel.OutputDirectory = dir;
        ViewModel.OutputFileName = name;
        return dir;
    }

    /// <summary>
    /// 获取当前文件过滤条件。返回 null 表示不过滤。
    /// </summary>
    public FileFilterCriteria? GetFilter()
    {
        if (FileFilterControl == null) return null;
        if (!FileFilterControl.IsFilterEnabled) return null;
        var filter = FileFilterControl.GetFilter();
        return filter.IsActive ? filter : null;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        // 限制最大高度不超过屏幕可用高度的 90%，防止内容撑高后按钮被推出屏幕
        var screen = Screens.ScreenFromWindow(this);
        if (screen != null)
        {
            MaxHeight = screen.WorkingArea.Height * 0.9;
        }

        AdjustWindowPosition();
        FormatOptionsPanel.LoadDefaults();
        LoadSplitSizeFromSettings();
        InitFileFilter();
    }

    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // 切换 Tab 后窗口高度可能变化，等布局完成后再检查位置
        Dispatcher.UIThread.Post(AdjustWindowPosition, DispatcherPriority.Background);
    }

    /// <summary>
    /// 检查窗口底部是否超出屏幕，超出则自动上移到刚好可见的位置。
    /// </summary>
    private void AdjustWindowPosition()
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen == null) return;

        // Height 是设备独立单位 (DIPs)，需乘 RenderScaling 转为屏幕像素
        var windowBottomPx = Position.Y + (int)(Height * RenderScaling);
        var overflowPx = windowBottomPx - screen.WorkingArea.Bottom;
        if (overflowPx > 0)
        {
            Position = Position.WithY(Position.Y - overflowPx);
        }
    }

    /// <summary>初始化文件过滤控件（预设 + 事件）。</summary>
    private void InitFileFilter()
    {
        var settings = AppSettings.Load();
        FileFilterControl.LoadPresets(settings.FilterPresets);

        FileFilterControl.SavePresetRequested += name =>
        {
            var filter = FileFilterControl.GetFilter();
            if (!filter.IsActive) return;
            settings.AddPreset(new FileFilterPreset(name, filter));
            settings.Save();
            FileFilterControl.LoadPresets(settings.FilterPresets);
        };

        FileFilterControl.DeletePresetRequested += preset =>
        {
            settings.FilterPresets.Remove(preset);
            settings.Save();
            FileFilterControl.LoadPresets(settings.FilterPresets);
        };

        FileFilterControl.RenamePresetRequested += (preset, newName) =>
        {
            var existing = settings.FilterPresets.FirstOrDefault(p => p.Name == newName);
            if (existing != null)
            {
                _ = AppMessageBox.Show(
                    LocalizationManager.T("FileFilter_PresetNameExists"),
                    LocalizationManager.T("FileFilter_RenamePresetTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning,
                    this);
                return;
            }
            preset.Name = newName;
            settings.Save();
            FileFilterControl.LoadPresets(settings.FilterPresets, newName);
        };

        // 过滤条件变更时重建预览树
        FileFilterControl.FilterChanged += OnFileFilterChanged;
    }

    /// <summary>
    /// 根据当前过滤条件重建压缩预览树。
    /// </summary>
    private void BuildPreview()
    {
        var filter = GetFilter();
        ViewModel.BuildCompressPreview(filter);
    }

    private void OnFileFilterChanged()
    {
        BuildPreview();
    }

    /// <summary>
    /// 从 AppSettings 加载分卷大小设置。
    /// </summary>
    private void LoadSplitSizeFromSettings()
    {
        var s = AppSettings.Load();
        if (!string.IsNullOrEmpty(s.SplitSizeTag))
        {
            var option = ViewModel.SplitSizeOptions.FirstOrDefault(o => o.Tag == s.SplitSizeTag);
            if (option != null)
                ViewModel.SelectedSplitSizeOption = option;
        }
        if (!string.IsNullOrEmpty(s.CustomSplitSizeMB))
            ViewModel.CustomSplitSizeText = s.CustomSplitSizeMB;
    }

    /// <summary>
    /// 将 DynamicFormatOptionsPanel 的当前值快照到 ViewModel，供压缩流程读取。
    /// 在关闭前调用；不再写回 AppSettings，避免污染设置窗口的全局默认值。
    /// 公开供 CLI 入口（App.axaml.cs 覆盖 CloseAction 后）显式调用。
    /// </summary>
    public void SnapshotFormatOptionsToViewModel()
    {
        ViewModel.FileNameEncoding = FormatOptionsPanel.FileNameEncoding ?? "utf-8";
        ViewModel.ZipCompressionMethod = FormatOptionsPanel.ZipCompressionMethod ?? "deflate";
        ViewModel.SevenZipCompressionMethod = FormatOptionsPanel.SevenZipCompressionMethod ?? "LZMA2";
        ViewModel.SevenZipSolid = FormatOptionsPanel.SevenZipSolid;
        ViewModel.SevenZipSolidBlockSize = FormatOptionsPanel.SevenZipSolidBlockSize ?? "";
        ViewModel.SevenZipDictionarySize = FormatOptionsPanel.SevenZipDictionarySize;
        ViewModel.SevenZipNumFastBytes = FormatOptionsPanel.SevenZipNumFastBytes;
        ViewModel.SevenZipMatchFinder = FormatOptionsPanel.SevenZipMatchFinder ?? "";
        // ZipEncryptionMethod / SevenZipEncryptHeaders 已通过 XAML 双向绑定到 ViewModel，无需快照
    }

    /// <summary>
    /// Subscribe to ViewModel PropertyChanged events to update UI elements
    /// that can't be easily bound in XAML.
    /// </summary>
    private void SubscribeViewModel()
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// 批量移除选中的源文件。
    /// </summary>
    private void RemoveSelected_Click(object? sender, RoutedEventArgs e)
    {
        var toRemove = SourceFilesList.SelectedItems.Cast<string>().ToList();
        foreach (var path in toRemove)
        {
            ViewModel.SelectedPaths.Remove(path);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.Password):
            case nameof(ViewModel.ConfirmPassword):
                UpdatePasswordMatchIndicator();
                break;
            case nameof(ViewModel.IsPasswordLibraryMode):
                UpdateSaveCheckLabel();
                break;
            case nameof(ViewModel.Encrypt):
                // 切换加密时密码区域展开/折叠，等布局完成后再检查位置
                Dispatcher.UIThread.Post(AdjustWindowPosition, DispatcherPriority.Background);
                break;
        }
    }

    /// <summary>
    /// Update the password match indicator text and visibility.
    /// </summary>
    private void UpdatePasswordMatchIndicator()
    {
        if (PasswordMatchIndicator == null) return;

        if (!string.IsNullOrEmpty(ViewModel.ConfirmPassword))
        {
            if (ViewModel.PasswordsMatch)
            {
                PasswordMatchIndicator.Text = LocalizationManager.T("Compress_Pwd_Match");
                PasswordMatchIndicator.Foreground = new SolidColorBrush(Color.Parse("#4CAF50")); // Green
                PasswordMatchIndicator.IsVisible = true;
            }
            else
            {
                PasswordMatchIndicator.Text = LocalizationManager.T("Compress_Pwd_NoMatch");
                PasswordMatchIndicator.Foreground = new SolidColorBrush(Color.Parse("#F44336")); // Red
                PasswordMatchIndicator.IsVisible = true;
            }
        }
        else
        {
            PasswordMatchIndicator.IsVisible = false;
        }
    }

    /// <summary>
    /// Update the save-to-library checkbox label based on current mode.
    /// </summary>
    private void UpdateSaveCheckLabel()
    {
        // The SaveToLibrary checkbox text is bound to localized strings in XAML,
        // but the label changes based on mode (library vs new password).
        // We handle the dynamic label update here since Avalonia's binding
        // can't easily change between two different string keys.
    }

    /// <summary>
    /// 切换密码显示/隐藏。
    /// </summary>
    private void TogglePasswordReveal(object? sender, RoutedEventArgs e)
    {
        if (PasswordTextBox.PasswordChar == '●')
        {
            PasswordTextBox.PasswordChar = default;
            RevealButton.Content = LocalizationManager.T("Compress_HidePassword");
        }
        else
        {
            PasswordTextBox.PasswordChar = '●';
            RevealButton.Content = LocalizationManager.T("Compress_ShowPassword");
        }
    }

    /// <summary>从源路径列表中推导「场景相关路径」：取第一个文件/目录所在目录，供默认路径优先级链 context 使用。</summary>
    private static string? ResolveContextPath(IReadOnlyList<string> sourcePaths)
    {
        if (sourcePaths == null || sourcePaths.Count == 0) return null;
        var first = sourcePaths[0];
        if (string.IsNullOrWhiteSpace(first)) return null;
        try
        {
            if (Directory.Exists(first)) return System.IO.Path.GetFullPath(first);
            if (File.Exists(first)) return System.IO.Path.GetDirectoryName(first);
            return null;
        }
        catch { return null; }
    }
}
