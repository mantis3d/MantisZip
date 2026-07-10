using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MantisZip.Core.FileFilter;
using MantisZip.UI.Localization;

namespace MantisZip.UI.Controls;

/// <summary>
/// 文件过滤编辑器用户控件。包含预设管理、扩展名/文件名/大小/日期四种维度的过滤配置。
/// </summary>
public partial class FileFilterEditor : UserControl
{
    // ── 预设扩展名映射 ──
    private static readonly Dictionary<string, string[]> PresetExtensions = new()
    {
        ["audio"] = new[] { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma" },
        ["video"] = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv" },
        ["image"] = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg" },
        ["document"] = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt" },
        ["archive"] = new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz" },
    };

    /// <summary>当前已加载的预设列表（内置 + 用户）。</summary>
    private List<FileFilterPreset> _presets = new();

    /// <summary>是否为内部更新（阻止递归事件触发）。</summary>
    private bool _isInternalUpdate;

    /// <summary>当前大小单位乘数（默认 MB）。</summary>
    private long _sizeUnitMultiplier = 1048576L;

    // ── 事件 ──

    /// <summary>过滤条件变更时触发。</summary>
    public event Action? FilterChanged;

    /// <summary>请求保存当前条件为预设。</summary>
    public event Action<string>? SavePresetRequested;

    /// <summary>请求删除指定预设。</summary>
    public event Action<FileFilterPreset>? DeletePresetRequested;

    // ── 公共属性 ──

    /// <summary>过滤是否已启用。</summary>
    public bool IsFilterEnabled
    {
        get => EnableFilterCheck.IsChecked == true;
        set => EnableFilterCheck.IsChecked = value;
    }

    /// <summary>编辑器中所有输入控件是否可编辑。</summary>
    public bool IsFilterEditable
    {
        get => EnableFilterCheck.IsEnabled;
        set
        {
            EnableFilterCheck.IsEnabled = value;
            SyncControlStates();
        }
    }

    public FileFilterEditor()
    {
        InitializeComponent();
        SyncControlStates();
    }

    // ═══════════════════════════════════════════
    //  公共方法
    // ═══════════════════════════════════════════

    /// <summary>
    /// 从 UI 控件读取当前过滤条件。
    /// </summary>
    public FileFilterCriteria GetFilter()
    {
        var filter = new FileFilterCriteria
        {
            IncludeExtensions = GetSelectedExtensions(),
            ExcludeExtensions = new List<string>(), // 暂不支持 UI 设置 ExcludeExtensions
            NamePattern = string.IsNullOrWhiteSpace(NamePatternBox.Text) ? null : NamePatternBox.Text.Trim(),
            MinSize = ParseNullableSize(MinSizeBox.Text),
            MaxSize = ParseNullableSize(MaxSizeBox.Text),
            MinDate = StartDatePicker.SelectedDate,
            MaxDate = EndDatePicker.SelectedDate,
        };
        return filter;
    }

    /// <summary>
    /// 将过滤条件填入 UI 控件。
    /// </summary>
    public void SetFilter(FileFilterCriteria filter)
    {
        if (filter == null) return;
        _isInternalUpdate = true;

        // 扩展名 — 取消全选，然后根据 filter 勾选匹配的预设
        ClearExtCheckboxes();
        if (filter.IncludeExtensions.Count > 0)
        {
            var extSet = new HashSet<string>(filter.IncludeExtensions, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in PresetExtensions)
            {
                if (kvp.Value.All(e => extSet.Contains(e)))
                    SetExtCheckbox(kvp.Key, true);
            }
            // 未匹配到预设的扩展名写入自定义输入框
            var matched = PresetExtensions.Values.SelectMany(e => e).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var custom = filter.IncludeExtensions.Where(e => !matched.Contains(e)).ToList();
            CustomExtTextBox.Text = string.Join(", ", custom.Select(e => e.TrimStart('.')));
        }
        else
        {
            CustomExtTextBox.Text = "";
        }

        // 文件名
        NamePatternBox.Text = filter.NamePattern ?? "";

        // 大小
        if (filter.MinSize.HasValue)
            MinSizeBox.Text = ConvertSizeValue(filter.MinSize.Value).ToString();
        else
            MinSizeBox.Text = "";
        if (filter.MaxSize.HasValue)
            MaxSizeBox.Text = ConvertSizeValue(filter.MaxSize.Value).ToString();
        else
            MaxSizeBox.Text = "";

        // 日期
        StartDatePicker.SelectedDate = filter.MinDate;
        EndDatePicker.SelectedDate = filter.MaxDate;

        _isInternalUpdate = false;
    }

    /// <summary>
    /// 加载预设列表到下拉框（内置 + 用户）。
    /// </summary>
    public void LoadPresets(List<FileFilterPreset> presets)
    {
        _presets = presets ?? new List<FileFilterPreset>();
        RefreshPresetCombo();
    }

    /// <summary>
    /// 当前选中的预设。
    /// </summary>
    public FileFilterPreset? SelectedPreset
        => PresetCombo.SelectedItem as FileFilterPreset;

    /// <summary>
    /// 设置过滤统计文本（提取模式使用）。
    /// </summary>
    public void SetFilterStats(string text)
    {
        FilterStatsText.Text = text;
        FilterStatsText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// 显示/隐藏过滤统计。
    /// </summary>
    public void ShowFilterStats(bool show)
    {
        FilterStatsText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    // ═══════════════════════════════════════════
    //  内部辅助
    // ═══════════════════════════════════════════

    private void SyncControlStates()
    {
        var enabled = EnableFilterCheck.IsChecked == true && EnableFilterCheck.IsEnabled;
        PresetCombo.IsEnabled = enabled;
        SavePresetBtn.IsEnabled = enabled;
        ExtAudioCheck.IsEnabled = enabled;
        ExtVideoCheck.IsEnabled = enabled;
        ExtImageCheck.IsEnabled = enabled;
        ExtDocumentCheck.IsEnabled = enabled;
        ExtArchiveCheck.IsEnabled = enabled;
        CustomExtTextBox.IsEnabled = enabled;
        NamePatternBox.IsEnabled = enabled;
        MinSizeBox.IsEnabled = enabled;
        MaxSizeBox.IsEnabled = enabled;
        SizeUnitCombo.IsEnabled = enabled;
        StartDatePicker.IsEnabled = enabled;
        EndDatePicker.IsEnabled = enabled;

        // 删除按钮：仅对用户预设启用
        UpdateDeleteBtnState();
    }

    private void UpdateDeleteBtnState()
    {
        var preset = SelectedPreset;
        DeletePresetBtn.IsEnabled = preset != null && !preset.IsBuiltIn
            && EnableFilterCheck.IsChecked == true && EnableFilterCheck.IsEnabled;
    }

    private void RefreshPresetCombo()
    {
        PresetCombo.ItemsSource = null;
        var allPresets = new List<FileFilterPreset>();
        allPresets.AddRange(FileFilterPreset.GetBuiltInPresets());
        allPresets.AddRange(_presets);
        PresetCombo.ItemsSource = allPresets;
    }

    private List<string> GetSelectedExtensions()
    {
        var exts = new List<string>();

        foreach (var kvp in PresetExtensions)
        {
            if (GetExtCheckbox(kvp.Key) == true)
                exts.AddRange(kvp.Value);
        }

        // 自定义扩展名
        var custom = CustomExtTextBox.Text;
        if (!string.IsNullOrWhiteSpace(custom))
        {
            foreach (var item in custom.Split(new[] { ',', ';', '，' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = item.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                if (!trimmed.StartsWith("."))
                    trimmed = "." + trimmed;
                if (!exts.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                    exts.Add(trimmed.ToLowerInvariant());
            }
        }

        return exts;
    }

    private bool? GetExtCheckbox(string key) => key switch
    {
        "audio" => ExtAudioCheck.IsChecked,
        "video" => ExtVideoCheck.IsChecked,
        "image" => ExtImageCheck.IsChecked,
        "document" => ExtDocumentCheck.IsChecked,
        "archive" => ExtArchiveCheck.IsChecked,
        _ => false,
    };

    private void SetExtCheckbox(string key, bool value)
    {
        switch (key)
        {
            case "audio": ExtAudioCheck.IsChecked = value; break;
            case "video": ExtVideoCheck.IsChecked = value; break;
            case "image": ExtImageCheck.IsChecked = value; break;
            case "document": ExtDocumentCheck.IsChecked = value; break;
            case "archive": ExtArchiveCheck.IsChecked = value; break;
        }
    }

    private void ClearExtCheckboxes()
    {
        ExtAudioCheck.IsChecked = false;
        ExtVideoCheck.IsChecked = false;
        ExtImageCheck.IsChecked = false;
        ExtDocumentCheck.IsChecked = false;
        ExtArchiveCheck.IsChecked = false;
    }

    private long? ParseNullableSize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (double.TryParse(text, out var val) && val >= 0)
            return (long)(val * _sizeUnitMultiplier);
        return null;
    }

    private double ConvertSizeValue(long bytes)
    {
        if (_sizeUnitMultiplier <= 1) return bytes;
        return bytes / (double)_sizeUnitMultiplier;
    }

    // ═══════════════════════════════════════════
    //  事件处理
    // ═══════════════════════════════════════════

    private void OnEnableFilterChanged(object sender, RoutedEventArgs e)
    {
        SyncControlStates();
        NotifyFilterChanged();
    }

    private void OnExtCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_isInternalUpdate) return;
        NotifyFilterChanged();
    }

    private void CustomExtTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInternalUpdate) return;
        NotifyFilterChanged();
    }

    private void NamePatternBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInternalUpdate) return;
        NotifyFilterChanged();
    }

    private void OnSizeFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInternalUpdate) return;
        NotifyFilterChanged();
    }

    private void OnSizeUnitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInternalUpdate) return;
        if (SizeUnitCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _sizeUnitMultiplier = long.TryParse(tag, out var m) ? m : 1048576L;
        }
        NotifyFilterChanged();
    }

    private void OnDateFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInternalUpdate) return;
        NotifyFilterChanged();
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInternalUpdate) return;
        UpdateDeleteBtnState();

        if (PresetCombo.SelectedItem is FileFilterPreset preset && !_isInternalUpdate)
        {
            SetFilter(preset.Criteria);
            NotifyFilterChanged();
        }
    }

    private async void SavePresetBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog(
            L.T(L.FileFilter_SavePresetTitle),
            L.T(L.FileFilter_PresetNamePrompt),
            "")
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            SavePresetRequested?.Invoke(dialog.InputText.Trim());
        }
    }

    private void DeletePresetBtn_Click(object sender, RoutedEventArgs e)
    {
        var preset = SelectedPreset;
        if (preset == null || preset.IsBuiltIn) return;

        var result = AppMessageBox.Show(
            L.TF(L.FileFilter_PresetDeleteConfirm, preset.Name),
            L.T(L.FileFilter_SavePresetTitle),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            DeletePresetRequested?.Invoke(preset);
        }
    }

    private void NotifyFilterChanged()
    {
        FilterChanged?.Invoke();
    }
}

/// <summary>
/// 简单的输入对话框。
/// </summary>
public class InputDialog : Window
{
    public string InputText { get; private set; } = "";

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        Title = title;
        Width = 350;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = System.Windows.Application.Current.TryFindResource("Theme_WindowBg") as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.White;

        var grid = new Grid { Margin = new Thickness(15) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = prompt,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = TryFindResource("Theme_TextPrimary") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Black,
        };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);

        var textBox = new TextBox
        {
            Text = defaultValue,
            Margin = new Thickness(0, 0, 0, 10),
            MinHeight = 22,
        };
        Grid.SetRow(textBox, 1);
        grid.Children.Add(textBox);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var okBtn = new Button
        {
            Content = "确定",
            Width = 70,
            Height = 26,
            Margin = new Thickness(0, 0, 6, 0),
            IsDefault = true,
        };
        okBtn.Click += (_, _) =>
        {
            InputText = textBox.Text;
            DialogResult = true;
        };
        btnPanel.Children.Add(okBtn);

        var cancelBtn = new Button
        {
            Content = "取消",
            Width = 70,
            Height = 26,
            IsCancel = true,
        };
        btnPanel.Children.Add(cancelBtn);

        Grid.SetRow(btnPanel, 2);
        grid.Children.Add(btnPanel);
        Content = grid;
    }
}
