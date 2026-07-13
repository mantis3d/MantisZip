using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Controls;

/// <summary>
/// A reusable path selection control with:
/// - AutoCompleteBox for path input with history suggestions
/// - Browse button that opens system folder/file picker
/// - Configurable mode: folder picker, file-open, or file-save
/// </summary>
public partial class QuickPathControl : UserControl
{
    // ── Styled Properties ──────────────────────────────────────────────────

    public static readonly StyledProperty<bool> IsFolderModeProperty =
        AvaloniaProperty.Register<QuickPathControl, bool>(nameof(IsFolderMode), true);

    public static readonly StyledProperty<bool> IsFileOpenModeProperty =
        AvaloniaProperty.Register<QuickPathControl, bool>(nameof(IsFileOpenMode), false);

    public static readonly StyledProperty<string> FileTypeFilterProperty =
        AvaloniaProperty.Register<QuickPathControl, string>(nameof(FileTypeFilter), string.Empty);

    public static readonly StyledProperty<string> FileNameProperty =
        AvaloniaProperty.Register<QuickPathControl, string>(nameof(FileName), string.Empty);

    public static readonly StyledProperty<string> DefaultFileNameProperty =
        AvaloniaProperty.Register<QuickPathControl, string>(nameof(DefaultFileName), string.Empty);

    public static readonly StyledProperty<string> PathTextProperty =
        AvaloniaProperty.Register<QuickPathControl, string>(nameof(PathText), string.Empty,
            defaultBindingMode: BindingMode.TwoWay);

    // ── CLR Properties ─────────────────────────────────────────────────────

    /// <summary>
    /// When true, browse button opens a folder picker. When false, opens file picker.
    /// </summary>
    public bool IsFolderMode
    {
        get => GetValue(IsFolderModeProperty);
        set => SetValue(IsFolderModeProperty, value);
    }

    /// <summary>
    /// When IsFolderMode=false and IsFileOpenMode=true, browse opens a file-open dialog.
    /// When IsFolderMode=false and IsFileOpenMode=false, browse opens a folder picker then
    /// combines the selected folder with the FileName/DefaultFileName.
    /// </summary>
    public bool IsFileOpenMode
    {
        get => GetValue(IsFileOpenModeProperty);
        set => SetValue(IsFileOpenModeProperty, value);
    }

    /// <summary>
    /// File filter string for file dialogs (e.g. "ZIP files|*.zip|All files|*.*").
    /// </summary>
    public string FileTypeFilter
    {
        get => GetValue(FileTypeFilterProperty);
        set => SetValue(FileTypeFilterProperty, value);
    }

    /// <summary>
    /// Current filename (used in save mode).
    /// </summary>
    public string FileName
    {
        get => GetValue(FileNameProperty);
        set => SetValue(FileNameProperty, value);
    }

    /// <summary>
    /// Default filename when FileName is empty (used in save mode).
    /// </summary>
    public string DefaultFileName
    {
        get => GetValue(DefaultFileNameProperty);
        set => SetValue(DefaultFileNameProperty, value);
    }

    /// <summary>
    /// The current path text displayed in the AutoCompleteBox.
    /// </summary>
    public string PathText
    {
        get => GetValue(PathTextProperty);
        set => SetValue(PathTextProperty, value);
    }

    // ── History ────────────────────────────────────────────────────────────

    /// <summary>
    /// Collection of recent paths shown as AutoCompleteBox suggestions.
    /// </summary>
    public ObservableCollection<string> RecentPaths { get; } = new();

    // ── Flag to prevent re-entrant updates ─────────────────────────────────

    private bool _isUpdatingText;

    // ── Constructor ────────────────────────────────────────────────────────

    public QuickPathControl()
    {
        InitializeComponent();

        PathAutoComplete.ItemsSource = RecentPaths;
        PathAutoComplete.PlaceholderText = LocalizationManager.T("QuickPath_Hint");
        PathAutoComplete.TextChanged += OnPathTextChanged;
    }

    // ── Property Changed ───────────────────────────────────────────────────

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PathTextProperty && !_isUpdatingText)
        {
            PathAutoComplete.Text = change.GetNewValue<string>() ?? string.Empty;
        }
    }

    // ── Handlers ───────────────────────────────────────────────────────────

    private void OnPathTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_isUpdatingText)
        {
            _isUpdatingText = true;
            PathText = PathAutoComplete.Text ?? string.Empty;
            _isUpdatingText = false;
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storage) return;

        try
        {
            if (IsFolderMode)
            {
                var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = LocalizationManager.T("QuickPath_SelectFolder"),
                    AllowMultiple = false
                });

                if (folders.Count >= 1)
                {
                    var path = folders[0].Path?.LocalPath ?? string.Empty;
                    SetPath(path);
                }
            }
            else if (IsFileOpenMode)
            {
                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = LocalizationManager.T("QuickPath_SelectFile"),
                    AllowMultiple = false,
                    FileTypeFilter = ParseFileFilter(FileTypeFilter)
                });

                if (files.Count >= 1)
                {
                    var path = files[0].Path?.LocalPath ?? string.Empty;
                    SetPath(path);
                }
            }
            else
            {
                // Save mode: pick a directory then combine with file name
                var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = LocalizationManager.T("QuickPath_SelectSaveFolder"),
                    AllowMultiple = false
                });

                if (folders.Count >= 1)
                {
                    var folderPath = folders[0].Path?.LocalPath ?? string.Empty;
                    var fileName = !string.IsNullOrEmpty(FileName) ? FileName : DefaultFileName;
                    var fullPath = !string.IsNullOrEmpty(fileName)
                        ? System.IO.Path.Combine(folderPath, fileName)
                        : folderPath;
                    SetPath(fullPath);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QuickPathControl] Browse error: {ex.Message}");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void SetPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        PathAutoComplete.Text = path;
        // TextChanged handler will update PathText

        AddToHistory(path);
    }

    /// <summary>
    /// Add a path to the recent history (deduplicated, max 20).
    /// </summary>
    public void AddToHistory(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        RecentPaths.Remove(path);
        RecentPaths.Insert(0, path);
        while (RecentPaths.Count > 20)
            RecentPaths.RemoveAt(RecentPaths.Count - 1);
    }

    /// <summary>
    /// Parse a file filter string into Avalonia FilePickerFileType list.
    /// Format: "Display name|*.ext1;*.ext2|Display name 2|*.ext3"
    /// </summary>
    private static List<FilePickerFileType>? ParseFileFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return null;

        var types = new List<FilePickerFileType>();
        var parts = filter.Split('|');

        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            var name = parts[i].Trim();
            var pattern = parts[i + 1].Trim();
            var patterns = pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (patterns.Length > 0)
            {
                types.Add(new FilePickerFileType(name) { Patterns = patterns });
            }
        }

        return types.Count > 0 ? types : null;
    }
}
