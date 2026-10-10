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

    /// <summary>当前文件的字节进度 0..1，驱动文件名格底纹宽度。</summary>
    [ObservableProperty]
    private double _fileRatio;

    // ── channel-info 计划：左右分组 + 合并百分比 + 信息列 + ToolTip ──

    /// <summary>该行是真实并行批次（true）还是非并行合成单行（false）。创建后不变。</summary>
    public bool IsParallel { get; init; }

    /// <summary>左区：目录部分（已剥前缀 + 中间省略由 VM 写入；根文件为空串）。</summary>
    [ObservableProperty]
    private string _directoryText = "";

    /// <summary>左区：文件名部分（恒完整）。</summary>
    [ObservableProperty]
    private string _fileNameText = "";

    /// <summary>左区：文件大小（空=隐藏，规则 6）。</summary>
    [ObservableProperty]
    private string _fileSizeText = "";

    /// <summary>右区：合并百分比+明细（"45% (12/40)"；单行仅 "45%"）。</summary>
    [ObservableProperty]
    private string _pctDetailText = "";

    /// <summary>右区：信息列（字节进度 · 压缩率；空=隐藏）。</summary>
    [ObservableProperty]
    private string _infoText = "";

    /// <summary>右区：批次底纹比例 0-1（= Percent/100）。</summary>
    [ObservableProperty]
    private double _batchRatio;

    /// <summary>右区：ToolTip 文本（VM 拼好，\n 分隔；null = 无 ToolTip）。</summary>
    [ObservableProperty]
    private string? _tooltipText;
}
