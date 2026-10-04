using System.ComponentModel;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;

namespace MantisZip.Core.Models;

/// <summary>
/// 列表模式行状态（UI 侧；Pending/Active 为播种/推导态，其余来自引擎上报）。
/// </summary>
public enum EntryRowState
{
    /// <summary>已播种但引擎尚未上报（○ 等待）。</summary>
    Pending,

    /// <summary>正在处理（⏳ n%），由 CurrentFile 推导。</summary>
    Active,

    /// <summary>正常写盘/压缩完成（✓）。</summary>
    Completed,

    /// <summary>因冲突策略跳过（⏭）。</summary>
    Skipped,

    /// <summary>写入失败（✗）。</summary>
    Failed,

    /// <summary>因冲突策略覆盖旧文件（🔄）。</summary>
    Overwritten
}

/// <summary>
/// 进度窗口「列表模式」的逐条目行模型（框架无关；中文文案与画刷键由 UI 层物化）。
/// </summary>
public class EntryProgressItem : INotifyPropertyChanged
{
    /// <summary>条目唯一键（与 <see cref="ArchiveProgress.EntryKey"/> 一致；禁止截断路径，否则同名条目互相覆盖）。</summary>
    public string EntryKey { get; set; } = string.Empty;

    /// <summary>显示名（条目键的文件名部分）。</summary>
    public string Name { get; set; } = string.Empty;

    private long _size;

    /// <summary>字节数（播种时来自条目大小；未知为 0）。</summary>
    public long Size
    {
        get => _size;
        set { if (Set(ref _size, value)) OnPropertyChanged(nameof(SizeText)); }
    }

    private double _percent;

    /// <summary>进度百分比 (0–100)，仅 Active 行有意义。</summary>
    public double Percent
    {
        get => _percent;
        set { if (Set(ref _percent, Math.Clamp(value, 0, 100))) OnPropertyChanged(nameof(PercentText)); }
    }

    private EntryRowState _state;

    /// <summary>当前行状态。</summary>
    public EntryRowState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            NotifyStateProperties();
        }
    }

    private string _statusText = string.Empty;

    /// <summary>
    /// 行内状态文案。Core 层保持框架无关：本属性只存文本，已本地化字符串由 UI 层
    /// （<c>ProgressViewModel</c> 经 <c>LocalizationManager.T</c>）赋值。
    /// <see cref="EntryRowState.Active"/> 行不赋值——视图层改显 <see cref="PercentText"/>。
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    /// <summary>大小文本；<see cref="Size"/> 为 0（未知）时返回空串，不显示 "0 B"。</summary>
    public string SizeText => _size > 0 ? FormatUtil.FormatSize(_size) : string.Empty;

    /// <summary>百分比文本（Active 行显示 "42%"）。</summary>
    public string PercentText => $"{_percent:F0}%";

    // ── 派生显示属性（集中通知） ──

    /// <summary>○ 等待。</summary>
    public bool IsPending => _state == EntryRowState.Pending;

    /// <summary>⏳ 进行中。</summary>
    public bool IsActive => _state == EntryRowState.Active;

    /// <summary>已到终态（Completed/Skipped/Failed/Overwritten）。</summary>
    public bool IsTerminal => _state is EntryRowState.Completed
        or EntryRowState.Skipped
        or EntryRowState.Failed
        or EntryRowState.Overwritten;

    /// <summary>状态图标字形（对齐 v6 原型 file-item 状态列）。</summary>
    public string StatusIcon => _state switch
    {
        EntryRowState.Completed   => "✅",
        EntryRowState.Skipped     => "⏭",
        EntryRowState.Failed      => "❌",
        EntryRowState.Overwritten => "🔄",
        EntryRowState.Active      => "⏳",
        _                         => "○",
    };

    /// <summary>状态画刷资源键（单一机制：UI 用 <c>BrushResourceConverter</c> 物化）。</summary>
    public string StatusBrushName => _state switch
    {
        EntryRowState.Completed   => "ThemeStatusSuccessBrush",
        EntryRowState.Skipped     => "ThemeStatusSkippedBrush",
        EntryRowState.Failed      => "ThemeStatusFailedBrush",
        EntryRowState.Overwritten => "ThemeProgressFillBrush",
        EntryRowState.Active      => "ThemeStatusWarningBrush",
        _                         => "ThemeBorderBrush",
    };

    /// <summary>
    /// 引擎上报的 4 个事实终态 → UI 行状态。
    /// 显式列出全部成员；<c>_</c> 臂只为消除 CS8524（枚举可含未命名值），
    /// 一旦 <see cref="ArchiveEntryStatus"/> 新增成员会立即抛异常而非静默错映射。
    /// </summary>
    public static EntryRowState MapEntryStatus(ArchiveEntryStatus status) => status switch
    {
        ArchiveEntryStatus.Completed   => EntryRowState.Completed,
        ArchiveEntryStatus.Skipped     => EntryRowState.Skipped,
        ArchiveEntryStatus.Failed      => EntryRowState.Failed,
        ArchiveEntryStatus.Overwritten => EntryRowState.Overwritten,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "未映射的条目终态"),
    };

    /// <summary>状态变更时集中通知全部派生属性（AGENTS.md 派生属性通知模式，禁止逐字段 NotifyPropertyChangedFor）。</summary>
    private void NotifyStateProperties()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsTerminal));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusBrushName));
    }

    /// <summary>字段赋值辅助：值变更时返回 true 并发出通知（镜像 <see cref="BatchItem"/> 的 Set 模式）。</summary>
    private bool Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;
}
