using Avalonia.Controls;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// Progress window shared between compress and extract operations.
/// Shows current file name, per-file progress bar, overall progress bar, and a Cancel button.
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
    /// Cancellation token for the current operation.
    /// </summary>
    public CancellationToken CancellationToken => _vm.CancellationToken;

    /// <summary>
    /// Initialize the cancellation token source.
    /// Must be called before starting the cancellable operation.
    /// </summary>
    public void InitCancellation()
    {
        _vm.InitCancellation();
    }

    /// <summary>
    /// Update all progress displays from an <see cref="ArchiveProgress"/> report.
    /// Safe to call from any thread (dispatches to UI thread internally).
    /// </summary>
    public void SetProgress(ArchiveProgress p)
    {
        _vm.SetProgress(p);
    }

    /// <summary>
    /// Set the status message (e.g. "正在解压..." / "正在压缩..." / "完成").
    /// </summary>
    public void SetStatus(string message)
    {
        _vm.StatusMessage = message;
    }
}
