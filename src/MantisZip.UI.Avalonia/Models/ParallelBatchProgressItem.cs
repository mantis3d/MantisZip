using CommunityToolkit.Mvvm.ComponentModel;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>ZIP 并行解压批次详细行（ArchiveProgress.BatchIndex 驱动；仅并行时由 VM 填充，非并行集合为空按 Rule 6 隐藏）。</summary>
public partial class ParallelBatchProgressItem : ObservableObject
{
    /// <summary>BatchIndex+1（1-based 显示序号）。</summary>
    public int Index { get; init; }

    /// <summary>完成百分比 0-100。</summary>
    [ObservableProperty]
    private double _percent;

    /// <summary>状态画刷资源键（BrushResourceConverter 物化）。</summary>
    [ObservableProperty]
    private string _statusBrushName = "ThemeBorderBrush";

    /// <summary>明细文本（如 "12/40 文件"，VM 用 T() 拼好传入）。</summary>
    [ObservableProperty]
    private string _detailText = "";

    /// <summary>批次当前文件名（详细模式行显示；引擎未上报时保持空串）。</summary>
    [ObservableProperty]
    private string _currentFile = "";
}
