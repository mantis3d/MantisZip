using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MantisZip.Core.Utils;
using Ookii.Dialogs.Wpf;

namespace MantisZip.UI.Controls;

public partial class QuickPathControl : UserControl
{
    // ── Dependency Properties ─────────────────────────────────────────────────

    public static readonly DependencyProperty PathTextProperty =
        DependencyProperty.Register(nameof(PathText), typeof(string), typeof(QuickPathControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnPathTextChanged));

    public static readonly DependencyProperty FileNameProperty =
        DependencyProperty.Register(nameof(FileName), typeof(string), typeof(QuickPathControl),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsFolderModeProperty =
        DependencyProperty.Register(nameof(IsFolderMode), typeof(bool), typeof(QuickPathControl),
            new PropertyMetadata(true, OnModeChanged));

    public static readonly DependencyProperty IsFileOpenModeProperty =
        DependencyProperty.Register(nameof(IsFileOpenMode), typeof(bool), typeof(QuickPathControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty FileTypeFilterProperty =
        DependencyProperty.Register(nameof(FileTypeFilter), typeof(string), typeof(QuickPathControl),
            new PropertyMetadata("所有文件|*.*"));

    public static readonly DependencyProperty FileOpenFilterProperty =
        DependencyProperty.Register(nameof(FileOpenFilter), typeof(string), typeof(QuickPathControl),
            new PropertyMetadata("所有文件|*.*"));

    public static readonly DependencyProperty DefaultFileNameProperty =
        DependencyProperty.Register(nameof(DefaultFileName), typeof(string), typeof(QuickPathControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(QuickPathControl),
            new PropertyMetadata(false, OnReadOnlyChanged));

    // ── CLR Properties ────────────────────────────────────────────────────────

    public string PathText
    {
        get => (string)GetValue(PathTextProperty);
        set => SetValue(PathTextProperty, value);
    }

    public string FileName
    {
        get => (string)GetValue(FileNameProperty);
        set => SetValue(FileNameProperty, value);
    }

    public bool IsFolderMode
    {
        get => (bool)GetValue(IsFolderModeProperty);
        set => SetValue(IsFolderModeProperty, value);
    }

    public bool IsFileOpenMode
    {
        get => (bool)GetValue(IsFileOpenModeProperty);
        set => SetValue(IsFileOpenModeProperty, value);
    }

    public string FileTypeFilter
    {
        get => (string)GetValue(FileTypeFilterProperty);
        set => SetValue(FileTypeFilterProperty, value);
    }

    public string FileOpenFilter
    {
        get => (string)GetValue(FileOpenFilterProperty);
        set => SetValue(FileOpenFilterProperty, value);
    }

    public string DefaultFileName
    {
        get => (string)GetValue(DefaultFileNameProperty);
        set => SetValue(DefaultFileNameProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public QuickPathControl()
    {
        InitializeComponent();
    }

    // ── Property Change Handlers ──────────────────────────────────────────────

    private static void OnPathTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Notify external bindings
    }

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Mode switch handled at runtime in Browse
    }

    private static void OnReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is QuickPathControl ctrl)
        {
            ctrl.PathTextBox.IsReadOnly = ctrl.IsReadOnly;
        }
    }

    // ── Event Handlers ────────────────────────────────────────────────────────

    private void PathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsReadOnly)
            PathText = PathTextBox.Text;
    }

    // ⭐ Favorites dropdown
    private void FavoritesButton_Click(object sender, RoutedEventArgs e)
    {
        var popup = new ContextMenu();
        var items = FavoritePathManager.GetAll();

        if (items.Count == 0)
        {
            popup.Items.Add(new MenuItem { Header = "暂无收藏", IsEnabled = false });
        }
        else
        {
            foreach (var item in items)
            {
                var display = item.IsSystem ? $"🔒 {item.Name}" : item.Name;
                var mi = new MenuItem
                {
                    Header = $"{display}  ({item.Path})",
                    Tag = item.Path
                };
                mi.Click += (s, args) =>
                {
                    PathText = item.Path;
                    PathHistoryManager.Record(item.Path);
                };
                popup.Items.Add(mi);
            }
        }

        popup.Items.Add(new Separator());
        var manageItem = new MenuItem { Header = "管理收藏…" };
        manageItem.Click += (s, args) =>
        {
            var win = new FavoriteManagerWindow();
            win.Owner = Window.GetWindow(this);
            win.ShowDialog();
        };
        popup.Items.Add(manageItem);

        popup.IsOpen = true;
    }

    // 🕐 History dropdown
    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var popup = new ContextMenu();
        var entries = PathHistoryManager.GetRecent(50);

        if (entries.Count == 0)
        {
            popup.Items.Add(new MenuItem { Header = "暂无历史记录", IsEnabled = false });
        }
        else
        {
            foreach (var entry in entries)
            {
                var mi = new MenuItem
                {
                    Header = entry.Path,
                    Tag = entry.Path
                };
                mi.Click += (s, args) =>
                {
                    PathText = entry.Path;
                };
                popup.Items.Add(mi);
            }
        }

        popup.IsOpen = true;
    }

    // 🪟 Explorer windows dropdown
    private void ExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        var popup = new ContextMenu();
        List<ExplorerWindowInfo> windows;

        try
        {
            windows = ExplorerWindowTracker.GetOpenExplorerWindows();
        }
        catch
        {
            windows = new List<ExplorerWindowInfo>();
        }

        if (windows.Count == 0)
        {
            popup.Items.Add(new MenuItem { Header = "没有打开的文件夹", IsEnabled = false });
        }
        else
        {
            foreach (var win in windows)
            {
                var header = win.IsActive ? $"▶ {win.Path}" : win.Path;
                var mi = new MenuItem
                {
                    Header = header,
                    Tag = win.Path,
                    FontWeight = win.IsActive ? FontWeights.Bold : FontWeights.Normal
                };
                mi.Click += (s, args) =>
                {
                    PathText = win.Path;
                    PathHistoryManager.Record(win.Path);
                };
                popup.Items.Add(mi);
            }
        }

        popup.IsOpen = true;
    }

    // 📁 Browse button
    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsFileOpenMode)
        {
            // File open mode
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                CheckFileExists = true,
                Multiselect = false,
                Filter = FileOpenFilter
            };

            if (!string.IsNullOrEmpty(PathText))
            {
                try
                {
                    dialog.InitialDirectory = System.IO.Path.GetDirectoryName(PathText);
                    dialog.FileName = System.IO.Path.GetFileName(PathText);
                }
                catch { }
            }

            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            {
                PathText = dialog.FileName;
                PathHistoryManager.Record(System.IO.Path.GetDirectoryName(dialog.FileName));
            }
        }
        else if (IsFolderMode)
        {
            // Folder mode
            var dialog = new VistaFolderBrowserDialog();
            if (!string.IsNullOrEmpty(PathText))
                dialog.SelectedPath = PathText;

            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            {
                PathText = dialog.SelectedPath;
                PathHistoryManager.Record(dialog.SelectedPath);
            }
        }
        else
        {
            // File save mode
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = FileTypeFilter,
                FileName = DefaultFileName
            };

            if (!string.IsNullOrEmpty(PathText))
            {
                try
                {
                    dialog.InitialDirectory = PathText;
                }
                catch { }
            }

            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            {
                PathText = System.IO.Path.GetDirectoryName(dialog.FileName);
                FileName = System.IO.Path.GetFileName(dialog.FileName);
            }
        }
    }
}