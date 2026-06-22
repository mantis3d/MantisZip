using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MantisZip.Core.Utils;

namespace MantisZip.UI;

public partial class FavoriteManagerWindow : Window
{
    private List<FavoriteItemViewModel>? _allItems;

    public FavoriteManagerWindow()
    {
        InitializeComponent();
        LoadFavorites();
    }

    private void LoadFavorites()
    {
        var allItems = new List<FavoriteItemViewModel>();

        // System paths
        foreach (var sp in FavoritePathManager.GetSystemPaths())
        {
            var hidden = FavoritePathManager.IsSystemPathHidden(sp.SystemKey ?? "");
            allItems.Add(new FavoriteItemViewModel
            {
                Name = sp.Name,
                Path = sp.Path,
                TypeLabel = "系统",
                IsSystem = true,
                SystemKey = sp.SystemKey ?? "",
                IsHidden = hidden
            });
        }

        // User favorites
        foreach (var uf in FavoritePathManager.GetUserFavorites())
        {
            allItems.Add(new FavoriteItemViewModel
            {
                Name = uf.Name,
                Path = uf.Path,
                TypeLabel = "收藏",
                IsSystem = false,
                SystemKey = null,
                IsHidden = false
            });
        }

        _allItems = allItems;
        FavoritesListView.ItemsSource = _allItems;
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        var selected = FavoritesListView.SelectedItem as FavoriteItemViewModel;
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

    private void FavoritesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateButtonStates();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddFavoriteDialog();
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            FavoritePathManager.Add(dialog.FavoriteName, dialog.FavoritePath);
            LoadFavorites();
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (FavoritesListView.SelectedItem is not FavoriteItemViewModel item || item.IsSystem) return;

        var dialog = new AddFavoriteDialog(item.Name, item.Path);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            FavoritePathManager.Update(item.Path, dialog.FavoriteName, dialog.FavoritePath);
            LoadFavorites();
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (FavoritesListView.SelectedItem is not FavoriteItemViewModel item || item.IsSystem) return;

        var result = AppMessageBox.Show(
            $"确定要删除收藏 \"{item.Name}\" 吗？",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            FavoritePathManager.Remove(item.Path);
            LoadFavorites();
        }
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (FavoritesListView.SelectedItem is not FavoriteItemViewModel item || item.IsSystem) return;
        var userFavs = FavoritePathManager.GetUserFavorites();
        var idx = userFavs.FindIndex(f => f.Path == item.Path);
        if (idx > 0)
        {
            FavoritePathManager.Reorder(idx, idx - 1);
            LoadFavorites();
        }
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (FavoritesListView.SelectedItem is not FavoriteItemViewModel item || item.IsSystem) return;
        var userFavs = FavoritePathManager.GetUserFavorites();
        var idx = userFavs.FindIndex(f => f.Path == item.Path);
        if (idx >= 0 && idx < userFavs.Count - 1)
        {
            FavoritePathManager.Reorder(idx, idx + 1);
            LoadFavorites();
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

public class FavoriteItemViewModel
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string TypeLabel { get; set; } = "";
    public bool IsSystem { get; set; }
    public string? SystemKey { get; set; }
    public bool IsHidden { get; set; }

    public string DisplayName => IsSystem ? $"🔒 {Name}" : Name;
}

/// <summary>
/// Simple dialog for adding/editing a favorite path.
/// </summary>
public partial class AddFavoriteDialog : Window
{
    public string FavoriteName { get; private set; } = "";
    public string FavoritePath { get; private set; } = "";

    public AddFavoriteDialog(string? existingName = null, string? existingPath = null)
    {
        InitializeComponent();
        if (existingName != null) NameTextBox.Text = existingName;
        if (existingPath != null) PathTextBox.Text = existingPath;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            AppMessageBox.Show("请输入名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(PathTextBox.Text))
        {
            AppMessageBox.Show("请输入路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FavoriteName = NameTextBox.Text.Trim();
        FavoritePath = PathTextBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
        if (!string.IsNullOrEmpty(PathTextBox.Text))
            dialog.SelectedPath = PathTextBox.Text;
        if (dialog.ShowDialog(this) == true)
            PathTextBox.Text = dialog.SelectedPath;
    }
}