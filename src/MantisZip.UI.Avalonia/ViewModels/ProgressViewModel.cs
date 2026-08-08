using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the progress window — shared between compress and extract operations.
/// Uses CommunityToolkit.Mvvm for observable properties and relay commands.
/// </summary>
public partial class ProgressViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    private readonly ManualResetEventSlim _pauseEvent = new(initialState: true);
    private ObservableCollection<BatchItem>? _batchItems;
    private bool _isBatchMode;
    private int _currentBatchIndex = -1;
    private DateTime _lastProgressUpdate = DateTime.MinValue;
    private static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Localized strings bound by the ProgressWindow UI.
    /// </summary>
    public Dictionary<string, string> LocalizedStrings { get; }

    public ProgressViewModel()
    {
        LocalizedStrings = new Dictionary<string, string>
        {
            ["Progress_Cancel"] = LocalizationManager.T("Progress_Cancel"),
            ["Progress_Complete"] = LocalizationManager.T("Progress_Complete"),
            ["Progress_Cancelling"] = LocalizationManager.T("Progress_Cancelling"),
            ["Progress_Button_Pause"] = LocalizationManager.T("Progress_Button_Pause"),
            ["Progress_Button_Resume"] = LocalizationManager.T("Progress_Button_Resume"),
            ["Progress_Button_Close"] = LocalizationManager.T("Progress_Button_Close"),
            ["Progress_KeepOpen"] = LocalizationManager.T("Progress_KeepOpen"),
            ["Progress_Paused"] = LocalizationManager.T("Progress_Paused"),
            ["Progress_Resuming"] = LocalizationManager.T("Progress_Resuming"),
            ["MsgBox_Cancel"] = LocalizationManager.T("MsgBox_Cancel"),
        };
    }

    // ════════════════════════════════════════════
    //  Observable Properties
    // ════════════════════════════════════════════

    [ObservableProperty]
    private string _windowTitle = string.Empty;

    [ObservableProperty]
    private int _percentComplete;

    [ObservableProperty]
    private int _filePercentComplete;

    [ObservableProperty]
    private string? _fileName;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _hasErrors;

    [ObservableProperty]
    private string? _errorSummaryText;

    [ObservableProperty]
    private string? _fileCountText;

    [ObservableProperty]
    private bool _isCancelEnabled = true;

    /// <summary>Is the keep-open toggle currently checked (📌 pinned open).</summary>
    [ObservableProperty]
    private bool _keepOpenOnComplete;

    // ════════════════════════════════════════════
    //  Password Section Properties
    // ════════════════════════════════════════════

    [ObservableProperty]
    private bool _isPasswordSectionVisible;

    [ObservableProperty]
    private string? _passwordMatchText;

    [ObservableProperty]
    private string? _passwordRuleText;

    [ObservableProperty]
    private string? _passwordStatusText;

    [ObservableProperty]
    private string? _passwordRevealButtonText = "\U0001F441"; // 👁

    [ObservableProperty]
    private bool _isPasswordRevealEnabled;

    [ObservableProperty]
    private bool _isPasswordCopyEnabled;

    [ObservableProperty]
    private bool _isPasswordRevealed;

    private string? _password;

    // ════════════════════════════════════════════
    //  Batch Mode Properties
    // ════════════════════════════════════════════

    public ObservableCollection<BatchItem>? BatchItems => _batchItems;

    public bool IsBatchMode => _isBatchMode;

    /// <summary>Pause event. Set = running, Reset = paused.</summary>
    public ManualResetEventSlim PauseEvent => _pauseEvent;

    /// <summary>Batch has at least one failed item.</summary>
    public bool HasFailures => _batchItems?.Any(i => i.Status == BatchItemStatus.Failed) ?? false;

    /// <summary>Cancellation token from the internal CTS.</summary>
    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    // ════════════════════════════════════════════
    //  Events (for code-behind coordination)
    // ════════════════════════════════════════════

    /// <summary>Raised when the cancel button is clicked and batch mode should close the window.</summary>
    public event Action? RequestClose;

    // ════════════════════════════════════════════
    //  Commands
    // ════════════════════════════════════════════

    /// <summary>
    /// Cancel the current operation. Called by the Cancel button.
    /// Always requests window close (matches WPF CancelButton_Click), regardless of batch mode.
    /// The window close is a no-op when the caller already owns the close (e.g. RunWithProgress's finally).
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        CancelOperation();
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Cancel the underlying operation without requesting a window close.
    /// Used by the window's X button (OnClosing): the window is already closing,
    /// so only the cancellation token needs to be triggered.
    /// </summary>
    public void CancelOperation()
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// Toggle pause/resume state.
    /// </summary>
    [RelayCommand]
    private void TogglePause()
    {
        if (_pauseEvent.IsSet)
        {
            // Running → pause
            _pauseEvent.Reset();
            IsPaused = true;
            StatusMessage = LocalizationManager.T("Progress_Paused");
        }
        else
        {
            // Paused → resume
            _pauseEvent.Set();
            IsPaused = false;
            StatusMessage = LocalizationManager.T("Progress_Resuming");
        }
    }

    // ════════════════════════════════════════════
    //  Public Methods
    // ════════════════════════════════════════════

    /// <summary>
    /// Initialize the cancellation token source.
    /// Must be called before any cancellable operation starts.
    /// </summary>
    public void InitCancellation()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// Update all progress-bound properties from an <see cref="ArchiveProgress"/> report.
    /// Safe to call from any thread; dispatches internally.
    /// </summary>
    public void SetProgress(ArchiveProgress p)
    {
        PercentComplete = (int)Math.Clamp(p.PercentComplete, 0, 100);
        if (p.FilePercentComplete.HasValue)
            FilePercentComplete = (int)Math.Clamp(p.FilePercentComplete.Value, 0, 100);
        if (!string.IsNullOrEmpty(p.CurrentFile))
            FileName = p.CurrentFile;

        // Weighted progress for batch mode
        if (_isBatchMode && _batchItems != null && _batchItems.Count > 1)
        {
            double completedWeight = _currentBatchIndex > 0
                ? (double)_currentBatchIndex / _batchItems.Count * 100
                : 0;
            double currentWeight = p.PercentComplete / _batchItems.Count;
            double overallPct = completedWeight + currentWeight;
            PercentComplete = (int)Math.Clamp(overallPct, 0, 100);
        }

        // File count
        if (_isBatchMode && _batchItems != null && _batchItems.Count > 0)
        {
            int current = _currentBatchIndex >= 0
                ? Math.Min(_currentBatchIndex + 1, _batchItems.Count)
                : Math.Min((int)p.PercentComplete / 100 * _batchItems.Count, _batchItems.Count);
            if (current < 1) current = 1;
            FileCountText = LocalizationManager.T("Progress_FileCount", current, _batchItems.Count);
        }
        else
        {
            FileCountText = LocalizationManager.T("Progress_FileCount", 1, 1);
        }

        // Batch mode: update current item's progress (throttled to 100ms)
        if (_isBatchMode && _currentBatchIndex >= 0 && _batchItems != null &&
            _currentBatchIndex < _batchItems.Count)
        {
            var now = DateTime.UtcNow;
            if (p.PercentComplete >= 100 || p.PercentComplete <= 0 ||
                (now - _lastProgressUpdate) >= ProgressThrottle)
            {
                _batchItems[_currentBatchIndex].Progress = p.PercentComplete;
                _lastProgressUpdate = now;
            }
        }
    }

    /// <summary>
    /// Compatibility overload: set overall progress and current file name only.
    /// </summary>
    public void SetProgress(double percent, string currentFile)
    {
        SetProgress(new ArchiveProgress
        {
            PercentComplete = percent,
            CurrentFile = currentFile
        });
    }

    /// <summary>
    /// Mark the operation as complete.
    /// Sets all progress bars to 100% and changes cancel button to "Close".
    /// </summary>
    public void SetComplete(string message)
    {
        PercentComplete = 100;
        FilePercentComplete = 100;
        FileName = LocalizationManager.T("Progress_Done");
        StatusMessage = message;
        IsIndeterminate = false;
    }

    // ════════════════════════════════════════════
    //  Batch Mode Methods
    // ════════════════════════════════════════════

    /// <summary>
    /// Initialize batch mode with the given file paths.
    /// </summary>
    public void InitBatchMode(System.Collections.Generic.IReadOnlyList<string> paths)
    {
        _isBatchMode = true;
        _currentBatchIndex = -1;
        _lastProgressUpdate = DateTime.MinValue;
        _batchItems = new ObservableCollection<BatchItem>(
            paths.Select(p => new BatchItem
            {
                Name = System.IO.Path.GetFileName(p),
                FullPath = p,
                Status = BatchItemStatus.Pending
            }));
        OnPropertyChanged(nameof(BatchItems));
        OnPropertyChanged(nameof(IsBatchMode));
        // 注意：不在此处覆盖 WindowTitle —— 标题由调用方传入（非批处理操作也显示列表，标题不能只属于批处理）
    }

    /// <summary>
    /// Set the current batch item index as "in progress".
    /// Safe to call from any thread.
    /// </summary>
    public void SetCurrentBatchItem(int index)
    {
        if (_batchItems == null || index < 0 || index >= _batchItems.Count)
            return;

        // Complete previous item if still InProgress
        if (index > 0 && _batchItems[index - 1].Status == BatchItemStatus.InProgress)
        {
            _batchItems[index - 1].Status = BatchItemStatus.Completed;
            _batchItems[index - 1].Progress = 100;
        }

        // Only overwrite Pending items (don't overwrite Skipped/Completed/Failed)
        if (_batchItems[index].Status != BatchItemStatus.Pending)
            return;

        _currentBatchIndex = index;
        _batchItems[index].Status = BatchItemStatus.InProgress;
        _batchItems[index].Progress = 0;
        FileName = _batchItems[index].Name;
        // 切换压缩包时重置文件进度条，避免残留上一个包未置满的脏值
        FilePercentComplete = 0;
    }

    /// <summary>
    /// Update the status of a specific batch item.
    /// Safe to call from any thread.
    /// </summary>
    public void UpdateBatchItemStatus(int index, BatchItemStatus status, string? errorMessage = null)
    {
        if (_batchItems == null || index < 0 || index >= _batchItems.Count)
            return;

        _batchItems[index].Status = status;
        if (status == BatchItemStatus.Failed)
            _batchItems[index].ErrorMessage = errorMessage;
        if (status is BatchItemStatus.Skipped or BatchItemStatus.Completed)
        {
            _batchItems[index].Progress = 100;
            // 当前项完成/跳过时，文件进度条同步置满。
            // 引擎不一定发出最终 FilePercentComplete=100（加密 ZIP 的 s7zAccumPct
            // 累积到不了 100、7z 最终报告缺失等），UI 层在此兜底保证显示正确。
            if (index == _currentBatchIndex)
                FilePercentComplete = 100;
        }
    }

    /// <summary>
    /// Called when batch completes with errors.
    /// Shows success/failure summary.
    /// </summary>
    public void CompleteWithErrors()
    {
        if (_batchItems == null) return;

        int succeeded = _batchItems.Count(i => i.Status == BatchItemStatus.Completed);
        int failed = _batchItems.Count(i => i.Status == BatchItemStatus.Failed);
        SetComplete(LocalizationManager.T("Progress_Batch_CompleteWithErrors", succeeded, failed));
    }

    /// <summary>
    /// Finalize batch: mark all still-InProgress items as Completed.
    /// </summary>
    public void FinalizeBatch()
    {
        if (_batchItems == null) return;
        for (int i = 0; i < _batchItems.Count; i++)
        {
            if (_batchItems[i].Status == BatchItemStatus.InProgress)
            {
                _batchItems[i].Status = BatchItemStatus.Completed;
                _batchItems[i].Progress = 100;
            }
        }
    }

    // ════════════════════════════════════════════
    //  Password Section Methods
    // ════════════════════════════════════════════

    /// <summary>
    /// Show the password section with "matching..." status.
    /// </summary>
    public void ShowPasswordAttempt(string description)
    {
        IsPasswordSectionVisible = true;
        _password = null;
        IsPasswordRevealed = false;
        PasswordMatchText = LocalizationManager.T("Progress_MatchingPassword");
        PasswordRuleText = LocalizationManager.T("Progress_PwdRule", description);
        PasswordStatusText = "";
        IsPasswordRevealEnabled = false;
        IsPasswordCopyEnabled = false;
        PasswordRevealButtonText = "\U0001F441"; // 👁
    }

    /// <summary>
    /// Show that password was matched successfully.
    /// </summary>
    public void ShowPasswordMatched(string password, string description)
    {
        IsPasswordSectionVisible = true;
        _password = password;

        // Respect PasswordRevealByDefault setting
        bool revealByDefault = AppSettings.Load().PasswordRevealByDefault;
        IsPasswordRevealed = revealByDefault;
        PasswordMatchText = revealByDefault
            ? LocalizationManager.T("Progress_PwdMatched", password)
            : LocalizationManager.T("Progress_PwdMatchedHidden");
        PasswordRevealButtonText = revealByDefault ? "\U0001F648" : "\U0001F441"; // 🙈 or 👁

        PasswordRuleText = LocalizationManager.T("Progress_PwdRule", description);
        PasswordStatusText = LocalizationManager.T("Progress_PwdVerifying");
        IsPasswordRevealEnabled = true;
        IsPasswordCopyEnabled = true;
    }

    /// <summary>
    /// Toggle password reveal/hide.
    /// </summary>
    [RelayCommand]
    private void TogglePasswordReveal()
    {
        IsPasswordRevealed = !IsPasswordRevealed;
        if (IsPasswordRevealed && _password != null)
            PasswordMatchText = LocalizationManager.T("Progress_PwdMatched", _password);
        else if (_password != null)
            PasswordMatchText = LocalizationManager.T("Progress_PwdMatchedHidden");
        PasswordRevealButtonText = IsPasswordRevealed ? "\U0001F648" : "\U0001F441"; // 🙈 or 👁
    }

    /// <summary>
    /// Get the current password text (for clipboard copy from code-behind).
    /// </summary>
    public string? GetPassword() => _password;

    /// <summary>
    /// Hide the password section (for non-encrypted files).
    /// </summary>
    public void HidePasswordSection()
    {
        IsPasswordSectionVisible = false;
    }

    /// <summary>
    /// Disable the cancel button. Used before entering non-interruptible operations.
    /// </summary>
    public void DisableCancel()
    {
        IsCancelEnabled = false;
    }

    // ════════════════════════════════════════════
    //  Error Summary
    // ════════════════════════════════════════════

    /// <summary>
    /// Set error summary text (selectable, shown between progress bars and buttons).
    /// </summary>
    public void SetErrorSummary(string message)
    {
        ErrorSummaryText = message;
        HasErrors = true;
    }

    // ════════════════════════════════════════════
    //  Progress Helpers
    // ════════════════════════════════════════════

    /// <summary>
    /// Creates an <see cref="IProgress{T}"/> that dispatches callbacks to the UI thread
    /// at <see cref="DispatcherPriority.Background"/> (lowest) priority.
    /// This allows the progress bar to repaint between progress updates.
    /// Safe to call from any thread.
    /// </summary>
    public static IProgress<ArchiveProgress> CreateBackgroundProgress(
        Dialogs.ProgressWindow pw,
        Action<ArchiveProgress> callback)
    {
        return new BackgroundDispatcherProgress(callback);
    }

    /// <summary>
    /// Creates a pause-aware wrapper around the given progress reporter.
    /// Report calls will block when paused, until resumed or cancelled.
    /// </summary>
    public IProgress<ArchiveProgress> CreatePauseAwareProgress(IProgress<ArchiveProgress> inner)
    {
        return new PauseAwareProgress(inner, _pauseEvent, _cts?.Token ?? CancellationToken.None);
    }

    /// <summary>
    /// Custom IProgress implementation that dispatches to the UI thread at Background priority.
    /// </summary>
    private sealed class BackgroundDispatcherProgress : IProgress<ArchiveProgress>
    {
        private readonly Action<ArchiveProgress> _callback;

        public BackgroundDispatcherProgress(Action<ArchiveProgress> callback)
        {
            _callback = callback;
        }

        public void Report(ArchiveProgress value)
        {
            Dispatcher.UIThread.Post(() => _callback(value), DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Pause-aware IProgress wrapper. Report blocks when paused.
    /// </summary>
    private sealed class PauseAwareProgress : IProgress<ArchiveProgress>
    {
        private readonly IProgress<ArchiveProgress> _inner;
        private readonly ManualResetEventSlim _pauseEvent;
        private readonly CancellationToken _cancellationToken;

        public PauseAwareProgress(IProgress<ArchiveProgress> inner, ManualResetEventSlim pauseEvent, CancellationToken cancellationToken)
        {
            _inner = inner;
            _pauseEvent = pauseEvent;
            _cancellationToken = cancellationToken;
        }

        public void Report(ArchiveProgress value)
        {
            try
            {
                _pauseEvent.Wait(_cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            _inner.Report(value);
        }
    }
}