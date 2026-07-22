using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 解压设置窗口。通过 ShowDialog 返回 bool 结果，
/// 调用方在结果为 true 后读取 ViewModel 的 DestinationPath / ConflictAction / OpenFolderAfterExtract 属性。
/// </summary>
public partial class ExtractSettingsWindow : Window
{
    private bool _loaded;
    private IReadOnlyList<ArchiveItem>? _entries;

    /// <summary>
    /// ViewModel，公开属性供调用方关闭后读取。
    /// </summary>
    public ExtractSettingsViewModel ViewModel { get; }

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="ExtractSettingsWindow(IReadOnlyList{string})"/>。
    /// </summary>
    public ExtractSettingsWindow()
    {
        InitializeComponent();
        ViewModel = new ExtractSettingsViewModel(Array.Empty<string>());
        DataContext = ViewModel;
    }

    public ExtractSettingsWindow(IReadOnlyList<string> archivePaths)
    {
        InitializeComponent();

        ViewModel = new ExtractSettingsViewModel(archivePaths);

        // 设置文件夹浏览回调
        ViewModel.BrowseFolder = async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select destination folder",
                AllowMultiple = false
            });
            return folders.Count >= 1 ? folders[0].Path.LocalPath : null;
        };

        // 设置关闭回调
        ViewModel.CloseAction = async (result) =>
        {
            Close(result);
            await Task.CompletedTask;
        };

        DataContext = ViewModel;

        // 绑定文件列表
        FileListBox.ItemsSource = archivePaths;

        Loaded += OnLoaded;
    }

    /// <summary>设置压缩包条目列表，用于过滤统计和 GetFilteredEntryKeys。</summary>
    public void SetEntries(IReadOnlyList<ArchiveItem> entries)
    {
        _entries = entries;
    }

    /// <summary>获取当前过滤条件。仅当启用过滤且 filter.IsActive 时有效。</summary>
    public FileFilterCriteria? GetFilter()
    {
        if (FileFilterControl == null) return null;
        if (!FileFilterControl.IsFilterEnabled) return null;
        var filter = FileFilterControl.GetFilter();
        return filter.IsActive ? filter : null;
    }

    /// <summary>
    /// 对 _entries 应用过滤条件，返回匹配条目的 key 列表。
    /// </summary>
    public List<string>? GetFilteredEntryKeys()
    {
        var filter = GetFilter();
        if (filter == null || _entries == null) return null;

        return _entries
            .Where(e => FileFilterMatcher.IsMatch(filter, e))
            .Select(e => e.FullPath)
            .ToList();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        InitFileFilter();
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

        // 更新过滤统计
        UpdateFilterStats();
        FileFilterControl.FilterChanged += UpdateFilterStats;
    }

    private void UpdateFilterStats()
    {
        if (_entries == null) return;

        var filter = GetFilter();
        if (filter != null)
        {
            var matched = _entries.Count(e => !e.IsDirectory && FileFilterMatcher.IsMatch(filter, e));
            var total = _entries.Count(e => !e.IsDirectory);
            FileFilterControl.SetFilterStats($"{matched} / {total} 文件匹配");
        }
        else
        {
            FileFilterControl.SetFilterStats("");
        }
    }
}
