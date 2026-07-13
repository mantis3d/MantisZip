using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 添加/编辑收藏路径对话框。
/// </summary>
public partial class AddFavoriteDialog : Window
{
    public string FavoriteName { get; private set; } = "";
    public string FavoritePath { get; private set; } = "";
    private readonly bool _isEditMode;

    // ── Localized string properties ──
    public string WinTitle => _isEditMode
        ? LocalizationManager.T("AddFav_EditTitle")
        : LocalizationManager.T("AddFav_Title");
    public string NameLabel => LocalizationManager.T("AddFav_NameLabel");
    public string PathLabel => LocalizationManager.T("AddFav_PathLabel");
    public string BrowseText => LocalizationManager.T("AddFav_Browse");
    public string OkText => LocalizationManager.T("AddFav_Ok");
    public string CancelText => LocalizationManager.T("AddFav_Cancel");

    /// <summary>
    /// 设计时需要的无参构造函数。
    /// </summary>
    public AddFavoriteDialog()
    {
        InitializeComponent();
        DataContext = this;
        _isEditMode = false;
    }

    public AddFavoriteDialog(string? existingName = null, string? existingPath = null)
    {
        InitializeComponent();
        DataContext = this;
        _isEditMode = existingName != null || existingPath != null;

        if (existingName != null)
            NameTextBox.Text = existingName;
        if (existingPath != null)
            PathTextBox.Text = existingPath;
    }

    private async void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            await AppMessageBox.Show(LocalizationManager.T("AddFav_WarningName"), "",
                MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return;
        }
        if (string.IsNullOrWhiteSpace(PathTextBox.Text))
        {
            await AppMessageBox.Show(LocalizationManager.T("AddFav_WarningPath"), "",
                MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return;
        }

        FavoriteName = NameTextBox.Text.Trim();
        FavoritePath = PathTextBox.Text.Trim();
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void BrowsePath_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = BrowseText,
            AllowMultiple = false,
        });

        var folder = folders?.FirstOrDefault();
        if (folder != null && folder.TryGetLocalPath() is { } localPath)
        {
            PathTextBox.Text = localPath;
        }
    }
}
