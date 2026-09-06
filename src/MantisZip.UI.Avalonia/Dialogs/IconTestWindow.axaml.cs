using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Dialogs;

public partial class IconTestWindow : Window
{
    private readonly IconTestViewModel _vm;

    public IconTestWindow()
    {
        InitializeComponent();
        _vm = new IconTestViewModel();
        DataContext = _vm;

        // 监听 ViewModel 的筛选变化，更新 DataGrid 数据源
        _vm.PropertyChanged += OnVmPropertyChanged;
        LoadFilteredItems();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IconTestViewModel.FilterText))
        {
            LoadFilteredItems();
        }
    }

    private void LoadFilteredItems()
    {
        var filtered = _vm.GetFilteredIcons();
        IconGrid.ItemsSource = filtered;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
