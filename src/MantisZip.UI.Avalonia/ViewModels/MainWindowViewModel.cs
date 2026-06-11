using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.UI.Avalonia.Models;
using System.Collections.ObjectModel;

namespace MantisZip.UI.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "MantisZip";

    [ObservableProperty]
    private string? _currentArchivePath;

    [ObservableProperty]
    private bool _isArchiveLoaded;

    [ObservableProperty]
    private ArchiveItemModel? _selectedEntry;

    public ObservableCollection<ArchiveItemModel> Entries { get; } = [];

    public MainWindowViewModel()
    {
    }

    [RelayCommand]
    private async Task OpenArchive()
    {
        // Placeholder — will be implemented with file dialog in Task 3
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void ClearArchive()
    {
        Entries.Clear();
        CurrentArchivePath = null;
        IsArchiveLoaded = false;
        SelectedEntry = null;
    }
}
