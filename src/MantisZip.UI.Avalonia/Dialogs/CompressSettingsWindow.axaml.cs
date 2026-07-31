using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
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
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select files to compress",
                AllowMultiple = true
            });
            return files?.Select(f => f.Path?.LocalPath).Where(p => p != null).ToList()!;
        };

        ViewModel.PickFolder = async () =>
        {
            return await CustomFilePickerDialog.ShowFolderAsync(this);
        };

        // 设置关闭回调
        ViewModel.CloseAction = async (result) =>
        {
            if (result)
            {
                // 关闭前将 DynamicFormatOptionsPanel 当前值写回 AppSettings
                SaveFormatOptionsToSettings();
            }
            Close(result);
            await Task.CompletedTask;
        };

        DataContext = ViewModel;
        SubscribeViewModel();
        Loaded += OnLoaded;
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
        SyncOutputPathControl();
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
    /// 将 DynamicFormatOptionsPanel 的当前值保存到 AppSettings。
    /// 在关闭前调用，确保后续压缩流程能读取到最新的高级选项设置。
    /// </summary>
    private void SaveFormatOptionsToSettings()
    {
        var s = AppSettings.Load();

        s.DefaultFormat = ViewModel.DefaultFormat;
        s.ZipEncoding = FormatOptionsPanel.FileNameEncoding ?? "utf-8";
        s.ZipCompressionMethod = FormatOptionsPanel.ZipCompressionMethod ?? "deflate";
        s.SevenZipCompressionMethod = FormatOptionsPanel.SevenZipCompressionMethod ?? "LZMA2";
        s.SevenZipSolid = FormatOptionsPanel.SevenZipSolid;
        s.SevenZipSolidBlockSize = FormatOptionsPanel.SevenZipSolidBlockSize ?? "";
        s.SevenZipDictionarySize = FormatOptionsPanel.SevenZipDictionarySize;
        s.SevenZipNumFastBytes = FormatOptionsPanel.SevenZipNumFastBytes;
        s.SevenZipMatchFinder = FormatOptionsPanel.SevenZipMatchFinder ?? "";
        s.ZipEncryptionMethod = ViewModel.ZipEncryptionMethod;
        s.SevenZipEncryptHeaders = ViewModel.SevenZipEncryptHeaders;
        s.SplitSizeTag = ViewModel.SelectedSplitSizeOption?.Tag ?? "0";
        s.CustomSplitSizeMB = ViewModel.CustomSplitSizeText;

        s.Save();
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
    /// QuickPathControl 选中路径 → 写入 ViewModel.OutputDirectory（Manual 模式），关闭下拉浮层。
    /// </summary>
    private void OutputPathControl_PathSelected(object? sender, string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (ViewModel.OutputMode != CompressOutputMode.Manual) return;
        ViewModel.OutputDirectory = path;

        // 收起下拉浮层
        OutputPathToggle.IsChecked = false;
        OutputPathPopup.IsOpen = false;
    }

    /// <summary>OutputPath/OutputMode 变化时同步 QuickPathControl 当前路径高亮。</summary>
    private void SyncOutputPathControl()
    {
        if (OutputPathControl == null) return;
        var dir = ViewModel.OutputMode == CompressOutputMode.Manual
            ? ViewModel.OutputDirectory
            : null;
        if (!string.IsNullOrEmpty(dir))
        {
            OutputPathControl.SetCurrentPath(dir);
        }
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
            case nameof(ViewModel.OutputDirectory):
            case nameof(ViewModel.OutputMode):
                SyncOutputPathControl();
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
            RevealButton.Content = LocalizationManager.T("Compress_ShowPassword") == "Show" ? "Hide" : "隐藏";
        }
        else
        {
            PasswordTextBox.PasswordChar = '●';
            RevealButton.Content = LocalizationManager.T("Compress_ShowPassword");
        }
    }
}
