using CommunityToolkit.Mvvm.ComponentModel;
using MantisZip.Core.Abstractions;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 「选择要解压的文件」页签里的一行：包内条目 + 勾选状态。
/// 勾选状态必须是通知属性（行模板绑 IsChecked），否则列表不会重绘。
/// </summary>
public sealed partial class ExtractEntrySelectionRow : ObservableObject
{
    public ExtractEntrySelectionRow(ArchiveItem item, bool isSelected)
    {
        Item = item;
        _isSelected = isSelected;
    }

    /// <summary>包内条目（只读，载荷里原样交回执行器）。</summary>
    public ArchiveItem Item { get; }

    /// <summary>列表显示路径（目录带尾斜杠，便于一眼看出层级）。</summary>
    public string DisplayPath => Item.IsDirectory
        ? (Item.FullPath ?? Item.Name).TrimEnd('/') + "/"
        : Item.FullPath ?? Item.Name;

    public bool IsDirectory => Item.IsDirectory;

    public long Size => Item.Size;

    [ObservableProperty]
    private bool _isSelected;
}