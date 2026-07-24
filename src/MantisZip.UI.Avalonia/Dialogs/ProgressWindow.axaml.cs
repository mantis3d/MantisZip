using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// Progress window shared between compress and extract operations.
/// Shows current file name, per-file progress bar, overall progress bar,
/// batch file list, password matching section, and action buttons.
/// Must be shown non-modal (use .Show()).
/// </summary>
public partial class ProgressWindow : Window
{
    private readonly ProgressViewModel _vm;

    public ProgressWindow()
    {
        InitializeComponent();
        _vm = new ProgressViewModel();
        DataContext = _vm;

        // Wire up close request from ViewModel
        _vm.RequestClose += () =>
        {
            Dispatcher.UIThread.Post(() => Close());
        };
    }

    /// <summary>
    /// Creates a progress window with a custom title.
    /// </summary>
    /// <param name="title">Window title (e.g. "正在解压..." / "正在压缩...").</param>
    public ProgressWindow(string title) : this()
    {
        _vm.WindowTitle = title;
    }

    /// <summary>
    /// Creates an <see cref="IProgress{T}"/> that dispatches callbacks to the UI thread
    /// at <see cref="DispatcherPriority.Background"/> priority.
    /// </summary>
    public static IProgress<ArchiveProgress> CreateBackgroundProgress(ProgressWindow pw)
    {
        return ProgressViewModel.CreateBackgroundProgress(pw, pw.SetProgress);
    }

    /// <summary>
    /// Creates a general-purpose <see cref="IProgress{T}"/> at Background priority.
    /// </summary>
    public static IProgress<ArchiveProgress> CreateBackgroundProgress(
        Dispatcher dispatcher, Action<ArchiveProgress> callback)
    {
        return ProgressViewModel.CreateBackgroundProgress(null!, callback);
    }

    // ════════════════════════════════════════════
    //  Properties
    // ════════════════════════════════════════════

    /// <summary>Cancellation token for the current operation.</summary>
    public CancellationToken CancellationToken => _vm.CancellationToken;

    /// <summary>Batch file items (null when not in batch mode).</summary>
    public ObservableCollection<BatchItem>? BatchItems => _vm.BatchItems;

    /// <summary>Whether batch mode is active.</summary>
    public bool IsBatchMode => _vm.IsBatchMode;

    /// <summary>Pause event. Set = running, Reset = paused.</summary>
    public ManualResetEventSlim PauseEvent => _vm.PauseEvent;

    /// <summary>Whether the operation is currently paused.</summary>
    public bool IsPaused => _vm.IsPaused;

    /// <summary>Whether batch has at least one failed item.</summary>
    public bool HasFailures => _vm.HasFailures;

    /// <summary>Whether to keep the window open on complete.</summary>
    public bool KeepOpenOnComplete => _vm.KeepOpenOnComplete;

    // ════════════════════════════════════════════
    //  Initialization & Cancellation
    // ════════════════════════════════════════════

    /// <summary>
    /// Initialize the cancellation token source.
    /// Must be called before starting the cancellable operation.
    /// </summary>
    public void InitCancellation()
    {
        _vm.InitCancellation();
    }

    // ════════════════════════════════════════════
    //  Progress Updates
    // ════════════════════════════════════════════

    /// <summary>
    /// Update all progress displays from an <see cref="ArchiveProgress"/> report.
    /// Safe to call from any thread (dispatches to UI thread internally).
    /// </summary>
    public void SetProgress(ArchiveProgress p)
    {
        _vm.SetProgress(p);
    }

    /// <summary>
    /// Compatibility overload: set overall progress and current file name only.
    /// </summary>
    public void SetProgress(double percent, string currentFile)
    {
        _vm.SetProgress(percent, currentFile);
    }

    /// <summary>
    /// Set the status message (e.g. "正在解压..." / "正在压缩..." / "完成").
    /// </summary>
    public void SetStatus(string message)
    {
        _vm.StatusMessage = message;
    }

    /// <summary>
    /// Mark the operation as complete.
    /// Sets all progress bars to 100% and changes cancel button to "Close".
    /// </summary>
    public void SetComplete(string message)
    {
        _vm.SetComplete(message);
        CancelButtonIcon.Data = (Geometry?)this.FindResource("IconCheckmark");
        CancelButtonText.Text = _vm.LocalizedStrings.TryGetValue("Progress_Button_Close", out var closeText)
            ? closeText
            : "Close";
    }

    /// <summary>
    /// Set error summary text (selectable, shown between progress bars and buttons).
    /// </summary>
    public void SetErrorSummary(string message)
    {
        _vm.SetErrorSummary(message);
    }

    // ════════════════════════════════════════════
    //  Auto-close / Manual Close
    // ════════════════════════════════════════════

    /// <summary>
    /// After completion, wait for auto-close or manual close.
    /// If KeepOpenOnComplete is set during countdown, switches to manual wait.
    /// </summary>
    public async Task AutoCloseOrWaitAsync(int delayMs, Action closeAction)
    {
        if (_vm.KeepOpenOnComplete)
        {
            await WaitForManualCloseAsync();
        }
        else
        {
            int step = 100;
            int elapsed = 0;
            while (elapsed < delayMs)
            {
                await Task.Delay(step);
                elapsed += step;
                if (_vm.KeepOpenOnComplete)
                {
                    await WaitForManualCloseAsync();
                    break;
                }
            }
        }
        closeAction();
    }

    /// <summary>
    /// Wait for the user to manually close the window.
    /// </summary>
    private async Task WaitForManualCloseAsync()
    {
        if (!IsVisible)
            return;

        var closed = new ManualResetEventSlim(false);
        EventHandler handler = null!;
        handler = (_, _) => { closed.Set(); Closed -= handler; };
        Closed += handler;
        try
        {
            await Task.Run(() => closed.Wait());
        }
        finally
        {
            Closed -= handler;
        }
    }

    // ════════════════════════════════════════════
    //  Batch Mode
    // ════════════════════════════════════════════

    /// <summary>
    /// Initialize batch mode with the given file paths.
    /// Shows the batch file list and sets the title.
    /// Must be called on the UI thread.
    /// </summary>
    public void InitBatchMode(System.Collections.Generic.IReadOnlyList<string> paths)
    {
        _vm.InitBatchMode(paths);
        BatchFileList.IsVisible = true;
        PauseButtonIcon.Data = (Geometry?)this.FindResource("IconPause");
        PauseButtonText.Text = _vm.LocalizedStrings.TryGetValue("Progress_Button_Pause", out var pauseText)
            ? pauseText
            : "Pause";
        Width = 500;
    }

    /// <summary>
    /// Mark the specified batch item as "in progress".
    /// Safe to call from any thread.
    /// </summary>
    public void SetCurrentBatchItem(int index)
    {
        DispatchIfNeeded(() => _vm.SetCurrentBatchItem(index), DispatcherPriority.Background);
    }

    /// <summary>
    /// Update the status of a specific batch item.
    /// Safe to call from any thread.
    /// </summary>
    public void UpdateBatchItemStatus(int index, BatchItemStatus status, string? errorMessage = null)
    {
        DispatchIfNeeded(() => _vm.UpdateBatchItemStatus(index, status, errorMessage), DispatcherPriority.Background);
    }

    /// <summary>
    /// Called when batch completes with errors. Shows success/failure summary.
    /// Safe to call from any thread.
    /// </summary>
    public void CompleteWithErrors()
    {
        DispatchIfNeeded(() => _vm.CompleteWithErrors(), DispatcherPriority.Background);
    }

    /// <summary>
    /// Finalize batch: marks all still-InProgress items as Completed.
    /// Safe to call from any thread.
    /// </summary>
    public void FinalizeBatch()
    {
        DispatchIfNeeded(() => _vm.FinalizeBatch(), DispatcherPriority.Background);
    }

    // ════════════════════════════════════════════
    //  Password Section
    // ════════════════════════════════════════════

    /// <summary>
    /// Show the password section with "matching..." status.
    /// Safe to call from any thread.
    /// </summary>
    public void ShowPasswordAttempt(string description)
    {
        DispatchIfNeeded(() =>
        {
            _vm.ShowPasswordAttempt(description);
            // Set warning background
            if (Application.Current?.Resources.TryGetResource("ThemeStatusWarningBrush", null, out var warningBrush) == true)
            {
                PasswordSection.Background = (IBrush)warningBrush!;
                PasswordSection.BorderBrush = (IBrush)warningBrush!;
            }
        });
    }

    /// <summary>
    /// Show that password was matched successfully.
    /// Safe to call from any thread.
    /// </summary>
    public void ShowPasswordMatched(string password, string description)
    {
        DispatchIfNeeded(() =>
        {
            _vm.ShowPasswordMatched(password, description);
            // Set success background
            if (Application.Current?.Resources.TryGetResource("ThemeStatusSuccessBrush", null, out var successBrush) == true)
            {
                PasswordSection.Background = (IBrush)successBrush!;
                PasswordSection.BorderBrush = (IBrush)successBrush!;
            }
        });
    }

    /// <summary>
    /// Hide the password section.
    /// Safe to call from any thread.
    /// </summary>
    public void HidePasswordSection()
    {
        DispatchIfNeeded(() => _vm.HidePasswordSection());
    }

    /// <summary>
    /// Disable the cancel button. Used before entering non-interruptible operations.
    /// </summary>
    public void DisableCancel()
    {
        DispatchIfNeeded(() => _vm.DisableCancel());
    }

    // ════════════════════════════════════════════
    //  Pause / Resume
    // ════════════════════════════════════════════

    /// <summary>
    /// Called by conflict dialogs to enter paused state without toggling the button text loop.
    /// </summary>
    public void PauseFromConflict()
    {
        _vm.PauseEvent.Reset();
        _vm.IsPaused = true;
        PauseButtonIcon.Data = (Geometry?)this.FindResource("IconPlay");
        if (_vm.LocalizedStrings.TryGetValue("Progress_Button_Resume", out var resumeText))
            PauseButtonText.Text = resumeText;
        _vm.StatusMessage = LocalizationManager.T("Progress_Paused");
    }

    /// <summary>
    /// Create a pause-aware progress wrapper.
    /// </summary>
    public IProgress<ArchiveProgress> CreatePauseAwareProgress(IProgress<ArchiveProgress> inner)
    {
        return _vm.CreatePauseAwareProgress(inner);
    }

    // ════════════════════════════════════════════
    //  Event Handlers
    // ════════════════════════════════════════════

    private async void PwdCopyBtn_Click(object? sender, RoutedEventArgs e)
    {
        var password = _vm.GetPassword();
        if (string.IsNullOrEmpty(password)) return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var clipboard = topLevel?.Clipboard;
            if (clipboard != null)
            {
                var transfer = new global::Avalonia.Input.DataTransfer();
                var item = new global::Avalonia.Input.DataTransferItem();
                item.SetText(password);
                transfer.Add(item);
                await clipboard.SetDataAsync(transfer);
                _vm.PasswordStatusText = LocalizationManager.T("Progress_PwdToClipboard");
            }
        }
        catch
        {
            // Best-effort clipboard copy
            _vm.PasswordStatusText = LocalizationManager.T("Progress_PwdCopyFailed");
        }
    }

    private void PauseButton_Click(object? sender, RoutedEventArgs e)
    {
        _vm.TogglePauseCommand.Execute(null);
if (_vm.IsPaused)
        {
            PauseButtonIcon.Data = (Geometry?)this.FindResource("IconPlay");
            if (_vm.LocalizedStrings.TryGetValue("Progress_Button_Resume", out var resumeText))
                PauseButtonText.Text = resumeText;
        }
        else
        {
            PauseButtonIcon.Data = (Geometry?)this.FindResource("IconPause");
            if (_vm.LocalizedStrings.TryGetValue("Progress_Button_Pause", out var pauseText))
                PauseButtonText.Text = pauseText;
        }
    }

    // ════════════════════════════════════════════
    //  Window Lifecycle
    // ════════════════════════════════════════════

    protected override void OnClosed(EventArgs e)
    {
        _vm.PauseEvent.Set();
        base.OnClosed(e);
    }

    /// <summary>
    /// Dispatch an action to the UI thread.
    /// </summary>
    private void DispatchIfNeeded(Action action, DispatcherPriority? priority = null)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action, priority ?? DispatcherPriority.Normal);
        }
    }
}
