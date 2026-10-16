using System.ComponentModel;

namespace MantisZip.Core.Models;

/// <summary>
/// 批量处理项的状态
/// </summary>
public enum BatchItemStatus
{
    Pending,
    InProgress,
    Completed,
    Skipped,
    Failed
}

/// <summary>批处理行密码徽标状态。</summary>
public enum BatchPasswordState
{
    None,      // 无密码/未开始
    Matching,  // 🔄 匹配中
    Matched,   // 🔑 已匹配（显示 ●●●●，可 Flyout 查看）
}

/// <summary>
/// 批量处理列表中的单个项（如压缩/解压任务）
/// </summary>
public class BatchItem : INotifyPropertyChanged
{
    /// <summary>显示名称（如文件名或任务名）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>完整路径</summary>
    public string? FullPath { get; set; }

    private BatchItemStatus _status;

    /// <summary>当前处理状态</summary>
    public BatchItemStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
                // 状态变更必须同步刷新画刷资源键，否则行颜色不更新
                OnPropertyChanged(nameof(StatusBrushName));
                // 同步刷新 IsFailed：批量行据此在「错误信息」与「百分比」之间互斥切换显示
                OnPropertyChanged(nameof(IsFailed));
            }
        }
    }

    private string? _errorMessage;

    /// <summary>失败时的错误信息（Set 通知——更新时机在 Status 之后，必须触发绑定刷新）</summary>
    public string? ErrorMessage { get => _errorMessage; set => Set(ref _errorMessage, value); }

    /// <summary>行状态为 Failed（批量行显示 ErrorMessage 而非百分比）。</summary>
    public bool IsFailed => Status == BatchItemStatus.Failed;

    private double _progress;

    /// <summary>进度百分比 (0–100)</summary>
    public double Progress
    {
        get => _progress;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (Math.Abs(_progress - clamped) > 0.01)
            {
                _progress = clamped;
                OnPropertyChanged(nameof(Progress));
            }
        }
    }

    // ── 解压统计（来自 ArchiveProgress 最终报告；UI 按 HasXxx 可见性控制） ──
    private long _totalFiles;
    private long _processedFiles;
    private long _skippedFiles;
    private long _failedFiles;
    private long _overwrittenFiles;

    public long TotalFiles { get => _totalFiles; set => Set(ref _totalFiles, value); }
    public long ProcessedFiles { get => _processedFiles; set { if (Set(ref _processedFiles, value)) NotifyBatchProperties(); } }
    public long SkippedFiles { get => _skippedFiles; set { if (Set(ref _skippedFiles, value)) NotifyBatchProperties(); } }
    public long FailedFiles { get => _failedFiles; set { if (Set(ref _failedFiles, value)) NotifyBatchProperties(); } }
    public long OverwrittenFiles { get => _overwrittenFiles; set { if (Set(ref _overwrittenFiles, value)) NotifyBatchProperties(); } }

    // ── 密码徽标 ──
    private BatchPasswordState _passwordState;
    private string? _matchedPassword;
    private string? _passwordRule;
    private string? _passwordDescription;
    private string? _summaryText;

    public BatchPasswordState PasswordState { get => _passwordState; set { if (Set(ref _passwordState, value)) NotifyBatchProperties(); } }
    public string? MatchedPassword { get => _matchedPassword; set => Set(ref _matchedPassword, value); }
    public string? PasswordRule { get => _passwordRule; set => Set(ref _passwordRule, value); }
    public string? PasswordDescription { get => _passwordDescription; set => Set(ref _passwordDescription, value); }

    /// <summary>状态行摘要（完成时填充，如 "12 成功 · 1 跳过"——文案 key 由 UI 层拼装，此处存格式化结果）。</summary>
    public string? SummaryText { get => _summaryText; set { if (Set(ref _summaryText, value)) NotifyBatchProperties(); } }

    // ── 派生显示属性（集中通知） ──
    public bool HasSkipped => SkippedFiles > 0;
    public bool HasFailures => FailedFiles > 0;
    public bool HasOverwritten => OverwrittenFiles > 0;
    public bool HasPasswordBadge => PasswordState != BatchPasswordState.None;

    /// <summary>显示 🔄 匹配中徽标。</summary>
    public bool IsPasswordMatching => PasswordState == BatchPasswordState.Matching;

    /// <summary>显示 🔑●●●● 已匹配徽标（挂 Flyout 查看/复制）。</summary>
    public bool IsPasswordMatched => PasswordState == BatchPasswordState.Matched;

    // ── 状态画刷资源键（单一机制：UI 用 BrushResourceConverter 物化；T5 注册主题键）──
    // 枚举实名 BatchItemStatus（Pending/InProgress/Completed/Skipped/Failed——无 Success、无 Cancelled）
    public string StatusBrushName => Status switch
    {
        BatchItemStatus.Completed => "ThemeStatusSuccessBrush",  // 既有键（Themes/ThemeLight.axaml:144）
        BatchItemStatus.Failed    => "ThemeStatusFailedBrush",   // T5 成对新增
        BatchItemStatus.Skipped   => "ThemeStatusSkippedBrush",  // T5 成对新增
        _ => "ThemeBorderBrush",   // Pending/InProgress（无 Cancelled 成员，不设专属键）
    };

    /// <summary>派生属性集中通知（AGENTS.md 派生属性通知模式，禁止逐字段 NotifyPropertyChangedFor）。</summary>
    private void NotifyBatchProperties()
    {
        OnPropertyChanged(nameof(HasSkipped));
        OnPropertyChanged(nameof(HasFailures));
        OnPropertyChanged(nameof(HasOverwritten));
        OnPropertyChanged(nameof(HasPasswordBadge));
        OnPropertyChanged(nameof(IsPasswordMatching));
        OnPropertyChanged(nameof(IsPasswordMatched));
        OnPropertyChanged(nameof(StatusBrushName));
        OnPropertyChanged(nameof(IsFailed));
    }

    /// <summary>字段赋值辅助：值变更时返回 true 并发出通知（镜像 ProgressViewModel 的 Set 模式）。</summary>
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
