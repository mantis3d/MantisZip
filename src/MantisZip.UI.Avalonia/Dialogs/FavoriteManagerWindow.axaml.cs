using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 收藏夹管理窗口。管理系统路径和用户自定义收藏路径。
/// </summary>
public partial class FavoriteManagerWindow : Window
{
    private ObservableCollection<FavoriteItemViewModel> _allItems = new();

    // ── Localized string properties ──
    public string WinTitle => LocalizationManager.T("FavMgr_Title");
    public string NameColumnHeader => LocalizationManager.T("FavMgr_ColumnName");
    public string PathColumnHeader => LocalizationManager.T("FavMgr_ColumnPath");
    public string TypeColumnHeader => LocalizationManager.T("FavMgr_ColumnType");
    public string AddText => LocalizationManager.T("FavMgr_Add");
    public string EditText => LocalizationManager.T("FavMgr_Edit");
    public string DeleteText => LocalizationManager.T("FavMgr_Delete");
    public string MoveUpText => LocalizationManager.T("FavMgr_MoveUp");
    public string MoveDownText => LocalizationManager.T("FavMgr_MoveDown");
    public string OkText => LocalizationManager.T("FavMgr_Ok");

    /// <summary>
    /// 设计时需要的无参构造函数。
    /// </summary>
    public FavoriteManagerWindow()
    {
        InitializeComponent();
        DataContext = this;
        LoadFavorites();
    }

    private void LoadFavorites()
    {
        _allItems.Clear();

        // System paths (skip hidden ones)
        foreach (var sp in FavoritePathManager.GetSystemPaths())
        {
            if (FavoritePathManager.IsSystemPathHidden(sp.SystemKey ?? ""))
                continue;

            _allItems.Add(new FavoriteItemViewModel
            {
                Name = sp.Name,
                Path = sp.Path,
                PathDisplay = sp.Path,
                TypeLabel = LocalizationManager.T("FavMgr_TypeSystem"),
                IsSystem = true,
                SystemKey = sp.SystemKey ?? "",
            });
        }

        // User favorites
        foreach (var uf in FavoritePathManager.GetUserFavorites())
        {
            _allItems.Add(new FavoriteItemViewModel
            {
                Name = uf.Name,
                Path = uf.Path,
                PathDisplay = uf.Path,
                TypeLabel = LocalizationManager.T("FavMgr_TypeFavorite"),
                IsSystem = false,
                SystemKey = null,
            });
        }

        FavoritesGrid.ItemsSource = _allItems;
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        var selected = FavoritesGrid.SelectedItem as FavoriteItemViewModel;
        EditButton.IsEnabled = selected != null && !selected.IsSystem;
        DeleteButton.IsEnabled = selected != null && !selected.IsSystem;

        if (selected != null && !selected.IsSystem)
        {
            var userFavs = FavoritePathManager.GetUserFavorites();
            var idx = userFavs.FindIndex(f => f.Path == selected.Path);
            MoveUpButton.IsEnabled = idx > 0;
            MoveDownButton.IsEnabled = idx >= 0 && idx < userFavs.Count - 1;
        }
        else
        {
            MoveUpButton.IsEnabled = false;
            MoveDownButton.IsEnabled = false;
        }
    }

    private void FavoritesGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateButtonStates();
    }

    private async void AddButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new AddFavoriteDialog();
        if (await dialog.ShowDialog<bool>(this))
        {
            FavoritePathManager.Add(dialog.FavoriteName, dialog.FavoritePath);
            LoadFavorites();
        }
    }

    private async void EditButton_Click(object? sender, RoutedEventArgs e)
    {
        if (FavoritesGrid.SelectedItem is not FavoriteItemViewModel item || item.IsSystem)
            return;

        var dialog = new AddFavoriteDialog(item.Name, item.Path);
        if (await dialog.ShowDialog<bool>(this))
        {
            FavoritePathManager.Update(item.Path, dialog.FavoriteName, dialog.FavoritePath);
            LoadFavorites();
        }
    }

    private async void DeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        if (FavoritesGrid.SelectedItem is not FavoriteItemViewModel item || item.IsSystem)
            return;

        var result = await AppMessageBox.Show(
            string.Format(LocalizationManager.T("FavMgr_ConfirmDelete"), item.Name),
            LocalizationManager.T("FavMgr_Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            this);

        if (result == MessageBoxResult.Yes)
        {
            FavoritePathManager.Remove(item.Path);
            LoadFavorites();
        }
    }

    private void MoveUpButton_Click(object? sender, RoutedEventArgs e)
    {
        if (FavoritesGrid.SelectedItem is not FavoriteItemViewModel item || item.IsSystem)
            return;

        var userFavs = FavoritePathManager.GetUserFavorites();
        var idx = userFavs.FindIndex(f => f.Path == item.Path);
        if (idx > 0)
        {
            FavoritePathManager.Reorder(idx, idx - 1);
            LoadFavorites();
        }
    }

    private void MoveDownButton_Click(object? sender, RoutedEventArgs e)
    {
        if (FavoritesGrid.SelectedItem is not FavoriteItemViewModel item || item.IsSystem)
            return;

        var userFavs = FavoritePathManager.GetUserFavorites();
        var idx = userFavs.FindIndex(f => f.Path == item.Path);
        if (idx >= 0 && idx < userFavs.Count - 1)
        {
            FavoritePathManager.Reorder(idx, idx + 1);
            LoadFavorites();
        }
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}

/// <summary>
/// View model for each DataGrid row in the favorites window.
/// </summary>
public class FavoriteItemViewModel
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string PathDisplay { get; set; } = "";
    public string TypeLabel { get; set; } = "";
    public bool IsSystem { get; set; }
    public string? SystemKey { get; set; }
}
