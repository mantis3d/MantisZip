using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.ViewModels;

public partial class MetadataPanelSettingsViewModel : ObservableObject
{
    private MetadataPanelSettings _settings = null!;

    /// <summary>所有可用的类型选项。</summary>
    public List<TypeOption> TypeOptions { get; } = [];

    /// <summary>当前选中的类型。</summary>
    [ObservableProperty]
    private TypeOption? _selectedType;

    /// <summary>当前选中类型的字段列表。</summary>
    [ObservableProperty]
    private ObservableCollection<FieldEditItem> _fields = [];

    /// <summary>字段显示方向："vertical" 或 "horizontal"。</summary>
    public string FieldLayoutMode
    {
        get => _settings?.FieldLayoutMode ?? "vertical";
        set
        {
            if (_settings != null)
            {
                _settings.FieldLayoutMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsHorizontalLayout));
                RebuildPreview();
            }
        }
    }

    /// <summary>字段是否左右并排。</summary>
    public bool IsHorizontalLayout
    {
        get => FieldLayoutMode == "horizontal";
        set
        {
            FieldLayoutMode = value ? "horizontal" : "vertical";
            OnPropertyChanged();
            OnPropertyChanged(nameof(FieldLayoutMode));
        }
    }

    /// <summary>预览用的 sections（仅 infoPanel 位置）。</summary>
    public ObservableCollection<MetadataSection> PreviewSections { get; } = [];

    // ── 本地化文本 ──
    public string FieldLayoutText => LocalizationManager.T("Metadata_Panel_FieldLayout");
    public string ColumnFieldText => LocalizationManager.T("Metadata_Panel_ColumnField");
    public string ColumnRowText => LocalizationManager.T("Metadata_Panel_ColumnRow");
    public string ColumnOrderText => LocalizationManager.T("Metadata_Panel_ColumnOrder");
    public string ColumnPositionText => LocalizationManager.T("Metadata_Panel_ColumnPosition");
    public string PreviewTitleText => LocalizationManager.T("Metadata_Panel_PreviewTitle");

    public MetadataPanelSettingsViewModel()
    {
        // 从 MetadataRegistry 加载所有已注册类型
        foreach (var key in MetadataRegistry.GetAllTypeKeys())
        {
            var displayName = GetTypeDisplayName(key);
            TypeOptions.Add(new TypeOption(key, displayName));
        }
        LocalizationManager.CultureChanged += OnCultureChanged;
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        // 静态本地化文本
        OnPropertyChanged(nameof(FieldLayoutText));
        OnPropertyChanged(nameof(ColumnFieldText));
        OnPropertyChanged(nameof(ColumnRowText));
        OnPropertyChanged(nameof(ColumnOrderText));
        OnPropertyChanged(nameof(ColumnPositionText));
        OnPropertyChanged(nameof(PreviewTitleText));

        // 类型下拉显示名
        foreach (var opt in TypeOptions)
            opt.DisplayName = GetTypeDisplayName(opt.TypeKey);

        // 字段列表显示名 + 位置下拉显示（PositionDisplayConverter 绑定 Position，需一并通知）
        foreach (var item in Fields)
            item.RefreshLocalizedText();

        // 预览区标题与字段名随语言变化，重建
        RebuildPreview();
    }

    /// <summary>从磁盘加载配置。</summary>
    public void Load()
    {
        _settings = MetadataSettingsManager.Load();
        if (TypeOptions.Count > 0)
            SelectedType = TypeOptions[0];
        OnPropertyChanged(nameof(FieldLayoutMode));
        OnPropertyChanged(nameof(IsHorizontalLayout));
    }

    /// <summary>写入配置到磁盘。</summary>
    public void Save()
    {
        if (_settings != null)
            MetadataSettingsManager.Save(_settings);
    }

    partial void OnSelectedTypeChanged(TypeOption? value)
    {
        // 先保存当前类型的编辑
        if (value != null)
        {
            // ApplyCurrentTypeConfig() 用 _previousTypeKey 找出要保存的类型
            // 但需要知道之前是哪个类型。用字段记住。
        }
        LoadFieldsForType(value?.TypeKey);
    }

    private string? _previousTypeKey;

    private void LoadFieldsForType(string? typeKey)
    {
        // 先保存上一个类型的修改
        if (_previousTypeKey != null && _settings != null)
        {
            if (_settings.Types.TryGetValue(_previousTypeKey, out var prevConfig))
            {
                foreach (var item in Fields)
                {
                    prevConfig.Fields[item.Key] = new FieldConfig
                    {
                        Row = item.Row,
                        Order = item.Order,
                        Position = item.Position,
                    };
                }
            }
        }

        Fields.Clear();
        _previousTypeKey = typeKey;
        if (typeKey == null || _settings == null) return;

        if (!_settings.Types.TryGetValue(typeKey, out var typeConfig))
        {
            typeConfig = new TypeMetadataConfig();
            _settings.Types[typeKey] = typeConfig;
        }

        var registryFields = MetadataRegistry.GetFields(typeKey);
        foreach (var def in registryFields)
        {
            var hasConfig = typeConfig.Fields.TryGetValue(def.Key, out var fieldConfig);
            var item = new FieldEditItem
            {
                Key = def.Key,
                DisplayName = def.DisplayName,
                Row = fieldConfig?.Row ?? 0,
                Order = fieldConfig?.Order ?? 10,
                Position = fieldConfig?.Position ?? "infoPanel",
            };
            item.SetChangeCallback(RebuildPreview);
            Fields.Add(item);
        }

        RebuildPreview();
    }

    /// <summary>根据当前 Fields 重建预览。</summary>
    public void RebuildPreview()
    {
        PreviewSections.Clear();
        var typeKey = _previousTypeKey;
        if (typeKey == null) return;

        var title = GetTypeDisplayName(typeKey);

        // ── infoPanel section ──
        var infoSection = new MetadataSection { Title = LocalizationManager.T("Metadata_Panel_SectionSidebar", title), ShowSeparator = true };
        BuildRows(infoSection, f => f.Position == "infoPanel");

        // ── contentTop section ──
        var topSection = new MetadataSection { Title = LocalizationManager.T("Metadata_Panel_SectionContentTop", title), ShowSeparator = false };
        BuildRows(topSection, f => f.Position == "contentTop");

        if (infoSection.Rows.Count > 0)
            PreviewSections.Add(infoSection);
        if (topSection.Rows.Count > 0)
            PreviewSections.Add(topSection);
    }

    private void BuildRows(MetadataSection section, Func<FieldEditItem, bool> filter)
    {
        var grouped = Fields
            .Where(filter)
            .GroupBy(f => f.Row)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var row = new InfoPanelRow();
            foreach (var item in group.OrderBy(f => f.Order))
                row.Items.Add(new FormatMetadataItem(item.DisplayName, ""));
            if (row.Items.Count > 0)
                section.Rows.Add(row);
        }
    }

    /// <summary>预览中移动字段到上一行。</summary>
    public void MoveFieldUp(string fieldKeyOrDisplay)
    {
        var item = Fields.FirstOrDefault(f => f.Key == fieldKeyOrDisplay || f.DisplayName == fieldKeyOrDisplay);
        if (item != null && item.Row > 0) item.Row--;
    }

    /// <summary>预览中移动字段到下一行。</summary>
    public void MoveFieldDown(string fieldKeyOrDisplay)
    {
        var item = Fields.FirstOrDefault(f => f.Key == fieldKeyOrDisplay || f.DisplayName == fieldKeyOrDisplay);
        if (item != null) item.Row++;
    }

    /// <summary>将所有类型的编辑写入 settings。</summary>
    public void ApplyAllTypeConfigs()
    {
        // 先保存当前选中类型的字段
        if (_previousTypeKey != null && _settings != null)
        {
            if (_settings.Types.TryGetValue(_previousTypeKey, out var typeConfig))
            {
                foreach (var item in Fields)
                {
                    typeConfig.Fields[item.Key] = new FieldConfig
                    {
                        Row = item.Row,
                        Order = item.Order,
                        Position = item.Position,
                    };
                }
            }
        }
    }

    private static string GetTypeDisplayName(string typeKey)
    {
        var i18nKey = $"Metadata_Type_{typeKey}";
        var localized = LocalizationManager.T(i18nKey);
        if (localized != i18nKey) return localized;

        return typeKey;
    }
}

/// <summary>类型选择器的一个选项。</summary>
public class TypeOption : ObservableObject
{
    public string TypeKey { get; }

    private string _displayName;
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public TypeOption(string typeKey, string displayName)
    {
        TypeKey = typeKey;
        _displayName = displayName;
    }
}

/// <summary>字段编辑项。</summary>
public class FieldEditItem : ObservableObject
{
    public string Key { get; set; } = "";

    private string _displayName = "";
    /// <summary>字段显示名：优先 i18n（Metadata_Key_{Key}），fallback 到 registry 显示名。</summary>
    public string DisplayName
    {
        get
        {
            var i18nKey = $"Metadata_Key_{Key}";
            var localized = LocalizationManager.T(i18nKey);
            return localized != i18nKey ? localized : _displayName;
        }
        set => SetProperty(ref _displayName, value);
    }

    private int _row;
    public int Row
    {
        get => _row;
        set { if (SetProperty(ref _row, value)) _onChanged?.Invoke(); }
    }

    private int _order;
    public int Order
    {
        get => _order;
        set { if (SetProperty(ref _order, value)) _onChanged?.Invoke(); }
    }

    private string _position = "infoPanel";
    public string Position
    {
        get => _position;
        set
        {
            if (SetProperty(ref _position, value))
            {
                _onChanged?.Invoke();
                OnPropertyChanged(nameof(PositionDisplay));
            }
        }
    }

    /// <summary>位置的显示名，只读绑定用。</summary>
    public string PositionDisplay => GetPositionDisplayName(_position);

    /// <summary>位置值 → 本地化显示名。JSON key 为 PascalCase（Metadata_PositionInfoPanel 等），位置值为 camelCase（infoPanel 等），需首字母大写后再查表。</summary>
    public static string GetPositionDisplayName(string pos)
    {
        if (string.IsNullOrEmpty(pos))
            return pos;
        return LocalizationManager.T($"Metadata_Position{char.ToUpperInvariant(pos[0])}{pos[1..]}");
    }

    public string[] PositionOptions => ["infoPanel", "contentTop", "hidden"];

    /// <summary>语言切换后刷新本地化显示（字段名 + 位置下拉，Position 走 PositionDisplayConverter 求值）。</summary>
    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(PositionDisplay));
    }

    private Action? _onChanged;
    public void SetChangeCallback(Action onChanged) => _onChanged = onChanged;
}
