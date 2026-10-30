using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// 「选择要解压的文件」页签（v0.5.5 第三步后半，2026-10-30）。
/// 只在条目选择模式下启用：宿主（右键「解压到」、拖拽兜底）把本次触发的条目传进来，
/// 用户勾选/取消后确认，载荷的 SelectedEntries 就是这组条目，交给唯一执行器（ExtractSession）解压。
/// </summary>
public partial class ExtractSettingsViewModel
{
    private string? _entryCurrentFolder;

    /// <summary>是否处于「选择要解压的文件」模式（决定页签显隐与载荷里 SelectedEntries 的取值）。</summary>
    public bool IsEntrySelectionMode { get; private set; }

    /// <summary>条目勾选行集合。</summary>
    public ObservableCollection<ExtractEntrySelectionRow> EntryRows { get; } = new();

    /// <summary>该页的「保留完整路径」勾选（仅本模式生效，初值取宿主传入值）。</summary>
    [ObservableProperty]
    private bool _preserveFullPathOption;

    /// <summary>已选/总数摘要。</summary>
    public string EntrySelectionSummary =>
        $"{EntryRows.Count(r => r.IsSelected)} / {EntryRows.Count}";

    /// <summary>
    /// 进入条目选择模式并填充行集合。重复路径只保留一条（右键多选可能带出重复的目录展开结果）。
    /// </summary>
    public void InitializeEntrySelection(
        IReadOnlyList<ArchiveItem> entries,
        string? currentFolder,
        bool preserveFullPath)
    {
        IsEntrySelectionMode = true;
        _entryCurrentFolder = currentFolder;
        PreserveFullPathOption = preserveFullPath;

        EntryRows.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var key = entry.FullPath ?? entry.Name;
            if (!seen.Add(key)) continue;
            var row = new ExtractEntrySelectionRow(entry, isSelected: true);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ExtractEntrySelectionRow.IsSelected))
                    OnPropertyChanged(nameof(EntrySelectionSummary));
            };
            EntryRows.Add(row);
        }

        OnPropertyChanged(nameof(EntrySelectionSummary));
    }

    /// <summary>当前勾选的条目（供载荷快照使用）。</summary>
    public List<ArchiveItem> BuildSelectedEntries() =>
        EntryRows.Where(r => r.IsSelected).Select(r => r.Item).ToList();

    [RelayCommand]
    private void SelectAllEntries()
    {
        foreach (var row in EntryRows) row.IsSelected = true;
        OnPropertyChanged(nameof(EntrySelectionSummary));
    }

    [RelayCommand]
    private void ClearEntrySelection()
    {
        foreach (var row in EntryRows) row.IsSelected = false;
        OnPropertyChanged(nameof(EntrySelectionSummary));
    }

    [RelayCommand]
    private void SelectFilesOnly()
    {
        foreach (var row in EntryRows) row.IsSelected = !row.IsDirectory;
        OnPropertyChanged(nameof(EntrySelectionSummary));
    }
}