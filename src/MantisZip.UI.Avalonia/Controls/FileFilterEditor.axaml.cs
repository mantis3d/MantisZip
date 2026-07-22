using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using MantisZip.Core.FileFilter;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Controls;

/// <summary>
/// ComboBox 中显示的预设条目包装类。
/// </summary>
public class PresetItem
{
    /// <summary>下拉菜单显示的文本。</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>关联的实际预设（临时预设/None 项为 null）。</summary>
    public FileFilterPreset? Preset { get; set; }

    /// <summary>是否为"— 无 —"条目。</summary>
    public bool IsNoneItem { get; set; }

    /// <summary>是否为临时预设（修改自动生成）。</summary>
    public bool IsTemporary { get; set; }

    /// <summary>临时预设保存的过滤条件。</summary>
    public FileFilterCriteria? TempCriteria { get; set; }

    /// <summary>临时预设对应的基底预设名。</summary>
    public string? SourcePresetName { get; set; }
}

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

    // ── 状态字段 ──

    /// <summary>原始预设列表（不含 None/临时）。</summary>
    private List<FileFilterPreset> _presets = new();

    /// <summary>ComboBox 显示条目列表。</summary>
    private readonly List<PresetItem> _displayItems = new();

    /// <summary>"— 无 —" 单例条目。</summary>
    private readonly PresetItem _noneItem;

    /// <summary>是否为内部更新（阻止递归事件触发）。</summary>
    private bool _isInternalUpdate;

    /// <summary>当前大小单位乘数（默认 MB）。</summary>
    private long _sizeUnitMultiplier = 1048576L;

    /// <summary>当前选中的基底预设名（null 表示无预设或"— 无 —"）。</summary>
    private string? _basePresetName;

    /// <summary>基底预设被选中时的条件快照，用于检测修改。</summary>
    private FileFilterCriteria? _baseCriteria;

    /// <summary>临时预设条目（修改自动生成，null 表示无）。</summary>
    private PresetItem? _tempPresetItem;

    /// <summary>保存预设后待选中的预设名。</summary>
    private string? _pendingSelectName;

    // ── 事件 ──

    /// <summary>过滤条件变更时触发。</summary>
    public event Action? FilterChanged;

    /// <summary>请求保存当前条件为预设。</summary>
    public event Action<string>? SavePresetRequested;

    /// <summary>请求删除指定预设。</summary>
    public event Action<FileFilterPreset>? DeletePresetRequested;

    /// <summary>请求重命名当前选中的预设。</summary>
    public event Action<FileFilterPreset, string>? RenamePresetRequested;

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

    /// <summary>当前选中的用户预设（不含 None/临时）。</summary>
    public FileFilterPreset? SelectedPreset
    {
        get
        {
            if (PresetCombo.SelectedItem is PresetItem pi && pi.Preset != null && !pi.IsNoneItem && !pi.IsTemporary)
                return pi.Preset;
            return null;
        }
    }

    public FileFilterEditor()
    {
        InitializeComponent();

        _noneItem = new PresetItem
        {
            DisplayName = LocalizationManager.T("FileFilter_None"),
            IsNoneItem = true,
        };

        // 本地化所有标签文本
        EnableFilterCheck.Content = LocalizationManager.T("FileFilter_Enable");
        PresetsLabel.Text = LocalizationManager.T("FileFilter_Presets");
        SavePresetBtn.Content = LocalizationManager.T("FileFilter_PresetSave");
        RenamePresetBtn.Content = LocalizationManager.T("FileFilter_PresetRename");
        DeletePresetBtn.Content = LocalizationManager.T("FileFilter_PresetDelete");
        ExtensionsLabel.Text = LocalizationManager.T("FileFilter_Extensions");
        ExtAudioCheck.Content = LocalizationManager.T("FileFilter_Audio");
        ExtVideoCheck.Content = LocalizationManager.T("FileFilter_Video");
        ExtImageCheck.Content = LocalizationManager.T("FileFilter_Image");
        ExtDocumentCheck.Content = LocalizationManager.T("FileFilter_Document");
        ExtArchiveCheck.Content = LocalizationManager.T("FileFilter_Archive");
        CustomExtLabel.Text = LocalizationManager.T("FileFilter_CustomExtensions");
        NamePatternLabel.Text = LocalizationManager.T("FileFilter_NamePattern");
        NamePatternHint.Text = LocalizationManager.T("FileFilter_NamePatternHint");
        SizeLabel.Text = LocalizationManager.T("FileFilter_Size");
        MinSizeLabel.Text = LocalizationManager.T("FileFilter_MinSize");
        MaxSizeLabel.Text = LocalizationManager.T("FileFilter_MaxSize");
        DateRangeLabel.Text = LocalizationManager.T("FileFilter_DateRange");
        StartDateLabel.Text = LocalizationManager.T("FileFilter_StartDate");
        EndDateLabel.Text = LocalizationManager.T("FileFilter_EndDate");

        // 初始化大小单位 ComboBox
        SizeUnitCombo.ItemsSource = new List<string> { "B", "KB", "MB", "GB" };
        SizeUnitCombo.SelectedIndex = 2; // default MB

        // 初始显示"— 无 —"
        RebuildDisplayItems();
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
            ExcludeExtensions = new List<string>(),
            NamePattern = string.IsNullOrWhiteSpace(NamePatternBox.Text) ? null : NamePatternBox.Text.Trim(),
            MinSize = ParseNullableSize(MinSizeBox.Text),
            MaxSize = ParseNullableSize(MaxSizeBox.Text),
            MinDate = StartDatePicker.SelectedDate?.DateTime,
            MaxDate = EndDatePicker.SelectedDate?.DateTime,
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

        ClearExtCheckboxes();
        if (filter.IncludeExtensions.Count > 0)
        {
            var extSet = new HashSet<string>(filter.IncludeExtensions, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in PresetExtensions)
            {
                if (kvp.Value.All(e => extSet.Contains(e)))
                    SetExtCheckbox(kvp.Key, true);
            }
            var matched = PresetExtensions.Values.SelectMany(e => e).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var custom = filter.IncludeExtensions.Where(e => !matched.Contains(e)).ToList();
            CustomExtTextBox.Text = string.Join(", ", custom.Select(e => e.TrimStart('.')));
        }
        else
        {
            CustomExtTextBox.Text = "";
        }

        NamePatternBox.Text = filter.NamePattern ?? "";

        if (filter.MinSize.HasValue)
            MinSizeBox.Text = ConvertSizeValue(filter.MinSize.Value).ToString();
        else
            MinSizeBox.Text = "";
        if (filter.MaxSize.HasValue)
            MaxSizeBox.Text = ConvertSizeValue(filter.MaxSize.Value).ToString();
        else
            MaxSizeBox.Text = "";

        StartDatePicker.SelectedDate = filter.MinDate.HasValue
            ? new DateTimeOffset(filter.MinDate.Value, TimeSpan.Zero)
            : null;
        EndDatePicker.SelectedDate = filter.MaxDate.HasValue
            ? new DateTimeOffset(filter.MaxDate.Value, TimeSpan.Zero)
            : null;

        _isInternalUpdate = false;
    }

    /// <summary>
    /// 清空所有过滤条件。
    /// </summary>
    public void ClearFilter()
    {
        _isInternalUpdate = true;
        ClearExtCheckboxes();
        CustomExtTextBox.Text = "";
        NamePatternBox.Text = "";
        MinSizeBox.Text = "";
        MaxSizeBox.Text = "";
        StartDatePicker.SelectedDate = null;
        EndDatePicker.SelectedDate = null;
        _isInternalUpdate = false;
    }

    /// <summary>
    /// 加载预设列表到下拉框（内置 + 用户）。
    /// </summary>
    public void LoadPresets(List<FileFilterPreset> presets, string? autoSelectName = null)
    {
        _presets = presets ?? new List<FileFilterPreset>();
        var autoSelect = autoSelectName ?? _pendingSelectName;
        _pendingSelectName = null;
        RebuildDisplayItems(autoSelect);
    }

    /// <summary>
    /// 设置过滤统计文本（提取模式使用）。
    /// </summary>
    public void SetFilterStats(string text)
    {
        FilterStatsText.Text = text;
    }

    /// <summary>
    /// 显示/隐藏过滤统计（已弃用——始终显示以避免 UI 跳动）。
    /// </summary>
    public void ShowFilterStats(bool show)
    {
        // 始终显示，不做折叠
    }

    // ═══════════════════════════════════════════
    //  内部辅助
    // ═══════════════════════════════════════════

    private void SyncControlStates()
    {
        // 方案A：隐藏整个过滤内容区，而非逐个禁用控件
        FilterContentPanel.IsVisible = EnableFilterCheck.IsChecked == true && EnableFilterCheck.IsEnabled;

        UpdateDeleteBtnState();
    }

    private void UpdateDeleteBtnState()
    {
        var preset = SelectedPreset;
        var canEdit = preset != null && !preset.IsBuiltIn
            && EnableFilterCheck.IsChecked == true && EnableFilterCheck.IsEnabled;
        DeletePresetBtn.IsEnabled = canEdit;
        RenamePresetBtn.IsEnabled = canEdit;
    }

    /// <summary>
    /// 重建 ComboBox 显示列表。包含：None 项 + 所有预设 + 临时预设。
    /// 自动恢复或指定选中项。
    /// </summary>
    private void RebuildDisplayItems(string? autoSelectName = null)
    {
        _displayItems.Clear();
        _displayItems.Add(_noneItem);

        // 内置预设
        foreach (var p in FileFilterPreset.GetBuiltInPresets())
        {
            _displayItems.Add(new PresetItem { DisplayName = p.Name, Preset = p });
        }

        // 用户预设
        foreach (var p in _presets)
        {
            _displayItems.Add(new PresetItem { DisplayName = p.Name, Preset = p });
        }

        if (_tempPresetItem != null)
            _displayItems.Add(_tempPresetItem);

        _isInternalUpdate = true;
        PresetCombo.ItemsSource = null;
        PresetCombo.ItemsSource = _displayItems;

        // 确定选中项
        PresetItem? toSelect;
        if (autoSelectName != null)
        {
            toSelect = _displayItems.Find(i => !i.IsNoneItem && !i.IsTemporary && i.Preset?.Name == autoSelectName);
            if (toSelect != null)
            {
                _basePresetName = autoSelectName;
                _baseCriteria = CloneCriteria(toSelect.Preset!.Criteria);
                PresetCombo.SelectedItem = toSelect;
                SetFilter(toSelect.Preset.Criteria);
            }
            else
            {
                PresetCombo.SelectedIndex = 0;
            }
        }
        else if (_tempPresetItem != null)
        {
            PresetCombo.SelectedItem = _tempPresetItem;
        }
        else if (_basePresetName != null)
        {
            toSelect = _displayItems.Find(i => !i.IsNoneItem && !i.IsTemporary && i.Preset?.Name == _basePresetName);
            if (toSelect != null)
                PresetCombo.SelectedItem = toSelect;
            else
                PresetCombo.SelectedIndex = 0; // 预设已被删除
        }
        else
        {
            PresetCombo.SelectedIndex = 0;
        }

        _isInternalUpdate = false;
        UpdateDeleteBtnState();
    }

    /// <summary>比较两组过滤条件是否相等。</summary>
    private static bool CriteriaEquals(FileFilterCriteria a, FileFilterCriteria b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;

        var aExts = a.IncludeExtensions ?? new List<string>();
        var bExts = b.IncludeExtensions ?? new List<string>();
        if (aExts.Count != bExts.Count) return false;
        var bSet = new HashSet<string>(bExts, StringComparer.OrdinalIgnoreCase);
        if (aExts.Any(e => !bSet.Contains(e))) return false;

        return a.NamePattern == b.NamePattern
            && a.MinSize == b.MinSize
            && a.MaxSize == b.MaxSize
            && a.MinDate == b.MinDate
            && a.MaxDate == b.MaxDate;
    }

    /// <summary>深拷贝 FileFilterCriteria。</summary>
    private static FileFilterCriteria CloneCriteria(FileFilterCriteria source)
    {
        return new FileFilterCriteria
        {
            IncludeExtensions = source.IncludeExtensions?.ToList() ?? new List<string>(),
            ExcludeExtensions = source.ExcludeExtensions?.ToList() ?? new List<string>(),
            NamePattern = source.NamePattern,
            MinSize = source.MinSize,
            MaxSize = source.MaxSize,
            MinDate = source.MinDate,
            MaxDate = source.MaxDate,
        };
    }

    /// <summary>
    /// 检查当前过滤条件与基底预设是否一致，不一致时创建/更新临时预设。
    /// </summary>
    private void CheckAndUpdateTempPreset()
    {
        if (_basePresetName == null || _baseCriteria == null) return;

        // 基底预设已被删除时不创建临时（内置 + 用户）
        var baseExists = FileFilterPreset.GetBuiltInPresets().Any(p => p.Name == _basePresetName)
                      || _presets.Any(p => p.Name == _basePresetName);
        if (!baseExists)
        {
            if (_tempPresetItem != null)
            {
                _tempPresetItem = null;
                RebuildDisplayItems();
            }
            return;
        }

        var current = GetFilter();
        if (CriteriaEquals(current, _baseCriteria))
        {
            // 与基底一致 → 清除临时
            if (_tempPresetItem != null)
            {
                _tempPresetItem = null;
                RebuildDisplayItems();
            }
        }
        else
        {
            // 与基底不一致 → 创建或更新临时
            var modifier = LocalizationManager.T("FileFilter_Modified");
            var tempName = _basePresetName + modifier;
            if (_tempPresetItem == null)
            {
                _tempPresetItem = new PresetItem
                {
                    DisplayName = tempName,
                    IsTemporary = true,
                    TempCriteria = CloneCriteria(current),
                    SourcePresetName = _basePresetName,
                };
                RebuildDisplayItems();
            }
            else
            {
                // 仅更新条件，不切换选中
                _tempPresetItem.TempCriteria = CloneCriteria(current);
            }
        }
    }

    private List<string> GetSelectedExtensions()
    {
        var exts = new List<string>();

        foreach (var kvp in PresetExtensions)
        {
            if (GetExtCheckbox(kvp.Key) == true)
                exts.AddRange(kvp.Value);
        }

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

    private void OnEnableFilterChanged(object? sender, RoutedEventArgs e)
    {
        SyncControlStates();
        NotifyFilterChanged();
    }

    private void OnExtCheckChanged(object? sender, RoutedEventArgs e)
    {
        if (_isInternalUpdate) return;
        NotifyFilterChanged();
    }

    private void CustomExtTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isInternalUpdate) return;
        NotifyFilterChanged();
    }

    private void NamePatternBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isInternalUpdate) return;
        NotifyFilterChanged();
    }

    private void SizeBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isInternalUpdate) return;
        NotifyFilterChanged();
    }

    private void SizeUnitCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInternalUpdate) return;
        if (SizeUnitCombo.SelectedItem is string unit)
        {
            _sizeUnitMultiplier = unit switch
            {
                "B" => 1L,
                "KB" => 1024L,
                "GB" => 1073741824L,
                _ => 1048576L, // MB default
            };
        }
        NotifyFilterChanged();
    }

    private void StartDatePicker_SelectedDateChanged(object? sender, DatePickerSelectedValueChangedEventArgs e)
    {
        if (_isInternalUpdate) return;
        NotifyFilterChanged();
    }

    private void EndDatePicker_SelectedDateChanged(object? sender, DatePickerSelectedValueChangedEventArgs e)
    {
        if (_isInternalUpdate) return;
        NotifyFilterChanged();
    }

    private void PresetCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInternalUpdate) return;

        var item = PresetCombo.SelectedItem as PresetItem;
        if (item == null) return;

        _isInternalUpdate = true;

        if (item.IsNoneItem)
        {
            // 选择"— 无 —"：清空过滤、清空预设跟踪
            ClearFilter();
            _basePresetName = null;
            _baseCriteria = null;
            _tempPresetItem = null;
        }
        else if (item.IsTemporary)
        {
            // 选择临时预设：加载其过滤条件，不改变基底跟踪
            if (item.TempCriteria != null)
                SetFilter(item.TempCriteria);
        }
        else
        {
            // 选择一个基底预设
            var preset = item.Preset;
            if (preset != null)
            {
                _basePresetName = preset.Name;
                _baseCriteria = CloneCriteria(preset.Criteria);
                SetFilter(preset.Criteria);

                // 重建列表，保留已有的临时预设，强制选中当前基底
                _isInternalUpdate = false;
                RebuildDisplayItems(autoSelectName: preset.Name);
                _isInternalUpdate = true;
            }
            else
            {
                ClearFilter();
                _basePresetName = null;
                _baseCriteria = null;
                _tempPresetItem = null;
            }
        }

        _isInternalUpdate = false;
        UpdateDeleteBtnState();
        FilterChanged?.Invoke();
    }

    private async void SavePresetBtn_Click(object? sender, RoutedEventArgs e)
    {
        var name = await ShowSavePresetDialog();
        if (!string.IsNullOrWhiteSpace(name))
        {
            _pendingSelectName = name.Trim();
            SavePresetRequested?.Invoke(name.Trim());
        }
    }

    private async void DeletePresetBtn_Click(object? sender, RoutedEventArgs e)
    {
        var preset = SelectedPreset;
        if (preset == null || preset.IsBuiltIn) return;

        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        var result = await AppMessageBox.Show(
            LocalizationManager.T("FileFilter_PresetDeleteConfirm", preset.Name),
            LocalizationManager.T("FileFilter_SavePresetTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            parentWindow);

        if (result == MessageBoxResult.Yes)
        {
            DeletePresetRequested?.Invoke(preset);
        }
    }

    private async void RenamePresetBtn_Click(object? sender, RoutedEventArgs e)
    {
        var preset = SelectedPreset;
        if (preset == null || preset.IsBuiltIn) return;

        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return;

        var dialog = new InputDialog(
            LocalizationManager.T("FileFilter_RenamePresetTitle"),
            LocalizationManager.T("FileFilter_PresetNamePrompt"),
            preset.Name)   // pre-fill with current name
        {
            Width = 350,
            Height = 160,
        };
        var result = await dialog.ShowDialog<bool?>(window);
        if (result == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            var newName = dialog.InputText.Trim();
            if (newName != preset.Name)
                RenamePresetRequested?.Invoke(preset, newName);
        }
    }

    private void NotifyFilterChanged()
    {
        FilterChanged?.Invoke();
        CheckAndUpdateTempPreset();
    }

    private async Task<string?> ShowSavePresetDialog()
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return null;

        var dialog = new InputDialog(
            LocalizationManager.T("FileFilter_SavePresetTitle"),
            LocalizationManager.T("FileFilter_PresetNamePrompt"),
            "")
        {
            Width = 350,
            Height = 160,
        };
        var result = await dialog.ShowDialog<bool?>(window);
        if (result == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            return dialog.InputText.Trim();
        return null;
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
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var grid = new Grid { Margin = new Thickness(15) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = prompt,
            Margin = new Thickness(0, 0, 0, 8),
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
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
        };
        var okBtn = new Button
        {
            Content = LocalizationManager.T("Common_OK"),
            Width = 70,
            Height = 26,
            Margin = new Thickness(0, 0, 6, 0),
        };
        okBtn.Click += (_, _) =>
        {
            InputText = textBox.Text ?? "";
            Close(true);
        };
        btnPanel.Children.Add(okBtn);

        var cancelBtn = new Button
        {
            Content = LocalizationManager.T("Common_Cancel"),
            Width = 70,
            Height = 26,
        };
        cancelBtn.Click += (_, _) => Close(false);
        btnPanel.Children.Add(cancelBtn);

        Grid.SetRow(btnPanel, 2);
        grid.Children.Add(btnPanel);
        Content = grid;
    }
}
