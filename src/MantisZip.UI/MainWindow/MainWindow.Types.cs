using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using MantisZip.Core.Abstractions;

namespace MantisZip.UI;

/// <summary>
/// 文件夹树节点
/// </summary>
public class FolderNode : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string Icon => string.IsNullOrEmpty(FullPath) ? "📦" : "📁";
    public List<FolderNode> Children { get; set; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded))); } }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class ArchiveItem : Core.Abstractions.ArchiveItem
{
    /// <summary>压缩后大小的显示模式</summary>
    public enum CompressedDisplayMode
    {
        /// <summary>正常显示实际的 CompressedSize（ZIP）</summary>
        Normal,
        /// <summary>格式本身不压缩（ISO, TAR），用 Size 作为压缩后大小，压缩率始终 100%</summary>
        NotCompressed,
        /// <summary>有压缩但无法获取逐项压缩后大小（7z, RAR, TGZ/GZ），显示 ---</summary>
        Unavailable
    }

    public string NameForSort { get; set; } = string.Empty;
    public ImageSource? IconSource { get; set; }

    public CompressedDisplayMode CompressedDisplay { get; set; } = CompressedDisplayMode.Normal;

    public string SizeDisplay => FormatSize(Size);

    public string CompressedSizeDisplay => CompressedDisplay switch
    {
        CompressedDisplayMode.Unavailable => "---",
        CompressedDisplayMode.NotCompressed => FormatSize(Size),
        _ => FormatSize(CompressedSize)
    };

    public string NameDisplay
    {
        get { return !string.IsNullOrEmpty(DisplayName) ? DisplayName : Name; }
    }

    public int SortOrder => IsDirectory ? 0 : 1;

    public string DateDisplay => LastModified > DateTime.MinValue
        ? LastModified.ToString("yyyy-MM-dd HH:mm")
        : "---";

    public string Crc32Display
    {
        get { return Crc32 != 0 ? $"{(uint)Crc32:X8}" : "---"; }
    }

    public string RatioDisplay
    {
        get
        {
            if (IsDirectory || Size == 0) return "---";
            return CompressedDisplay switch
            {
                CompressedDisplayMode.Unavailable => "---",
                CompressedDisplayMode.NotCompressed => "100%",
                _ when CompressedSize == 0 => "---",
                _ => $"{Math.Min((double)CompressedSize / Size, 1.0) * 100:F1}%"
            };
        }
    }

    public double RatioSort
    {
        get
        {
            if (IsDirectory || Size == 0) return double.MaxValue;
            return CompressedDisplay switch
            {
                CompressedDisplayMode.Unavailable => double.MaxValue,
                CompressedDisplayMode.NotCompressed => 1.0,
                _ when CompressedSize == 0 => double.MaxValue,
                _ => Math.Min((double)CompressedSize / Size, 1.0)
            };
        }
    }

    // ——— 进度条属性 ———
    /// <summary>全局开关（由菜单切换）</summary>
    public bool ProgressBarEnabled { get; set; } = true;
    /// <summary>目录独立基准模式（由菜单切换）</summary>
    public bool SeparateDirBaseline { get; set; } = false;

    /// <summary>目录在分列基准模式下使用深色进度条</summary>
    public bool UseDirProgressColor => IsDirectory && SeparateDirBaseline;

    /// <summary>大小相对比例（0.0 ~ 1.0，FilterFiles 中计算赋值）</summary>
    public double SizeRatio { get; set; }
    /// <summary>压缩后大小相对比例（0.0 ~ 1.0，FilterFiles 中计算赋值）</summary>
    public double CompressedSizeRatio { get; set; }
    /// <summary>日期相对比例（0.0 ~ 1.0，FilterFiles 中计算赋值）</summary>
    public double DateRatio { get; set; }

    /// <summary>压缩率进度条值（绝对比例，复用 RatioSort，门控 IsDirectory/Size=0/Unavailable）</summary>
    public double RatioBarValue => ProgressBarEnabled && !IsDirectory && Size > 0
        && CompressedDisplay != CompressedDisplayMode.Unavailable
        ? Math.Min(RatioSort, 1.0)
        : 0;

    internal static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }
}
