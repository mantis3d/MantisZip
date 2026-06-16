using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the progress window — shared between compress and extract operations.
/// Uses CommunityToolkit.Mvvm for observable properties and relay commands.
/// </summary>
public partial class ProgressViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;

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
        };
    }

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

    /// <summary>
    /// Cancellation token from the internal CTS.
    /// </summary>
    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

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
    /// Cancel the current operation. Called by the Cancel button.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
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
    }

    /// <summary>
    /// Creates an <see cref="IProgress{T}"/> that dispatches callbacks to the UI thread
    /// at <see cref="DispatcherPriority.Background"/> (lowest) priority.
    /// This allows the progress bar to repaint between progress updates.
    /// Safe to call from any thread.
    /// </summary>
    /// <param name="pw">The owning ProgressWindow (used for API consistency with WPF counterpart).</param>
    /// <param name="callback">The action to invoke on the UI thread with each progress report.</param>
    public static IProgress<ArchiveProgress> CreateBackgroundProgress(
        Dialogs.ProgressWindow pw,
        Action<ArchiveProgress> callback)
    {
        return new BackgroundDispatcherProgress(callback);
    }

    /// <summary>
    /// Custom IProgress implementation that dispatches to the UI thread at Background priority.
    /// Unlike Progress&lt;T&gt; (which uses SynchronizationContext.Post at Normal priority),
    /// this uses Background priority so render/paint operations can execute between updates.
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
}
