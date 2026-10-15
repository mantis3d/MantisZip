using CommunityToolkit.Mvvm.ComponentModel;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>进度窗通道行（并行批次行 IsParallel=true，或非并行合成单行 IsParallel=false；详细模式常显，D2）。</summary>
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

    /// <summary>右区：合并百分比+明细（"45% (12/40)"；仅并行行使用——单行与底部总进度重复，不填充）。</summary>
    [ObservableProperty]
    private string _pctDetailText = "";

    /// <summary>右区：信息列（字节进度 · 压缩率；空=隐藏）。变更时同步刷新 ShowRightRegion。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRightRegion))]
    private string _infoText = "";

    /// <summary>右区：批次底纹比例 0-1（= Percent/100；仅并行行使用——单行底纹与底部总进度重复，不填充）。</summary>
    [ObservableProperty]
    private double _batchRatio;

    /// <summary>右区整体是否显示：并行行恒显（序号/底纹/百分比有效）；单行仅信息列非空时显示（空时整区隐藏让左区占满，7z/解压无字节无压缩率即此场景）。</summary>
    public bool ShowRightRegion => IsParallel || !string.IsNullOrEmpty(InfoText);

    /// <summary>右区：ToolTip 文本（VM 拼好，\n 分隔；null = 无 ToolTip）。</summary>
    [ObservableProperty]
    private string? _tooltipText;
}
