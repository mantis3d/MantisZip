using CommunityToolkit.Mvvm.ComponentModel;

namespace MantisZip.UI.Avalonia.Models;

public partial class ArchiveItemModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private long _size;

    [ObservableProperty]
    private string _sizeDisplay = string.Empty;

    [ObservableProperty]
    private long _compressedSize;

    [ObservableProperty]
    private string _compressedSizeDisplay = string.Empty;

    [ObservableProperty]
    private DateTime _lastModified;

    [ObservableProperty]
    private string _lastModifiedDisplay = string.Empty;

    [ObservableProperty]
    private bool _isDirectory;

    [ObservableProperty]
    private double _compressionRatio;
}
