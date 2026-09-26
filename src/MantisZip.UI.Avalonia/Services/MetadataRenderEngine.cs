using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 元数据渲染引擎。根据配置将元数据值分发到 InfoPanel (底部固定) 和 ContentTop (滚动区顶部)。
/// 提供独立的 RenderCommon / RenderFormat 方法，分别处理通用信息和格式特有信息。
/// </summary>
public static class MetadataRenderEngine
{
    /// <summary>
    /// 只渲染通用信息 section（文件信息）。
    /// </summary>
    public static MetadataRenderResult RenderCommon(
        Dictionary<string, string?> commonValues,
        MetadataPanelSettings settings)
    {
        var config = settings.Types.GetValueOrDefault("common");
        if (config?.Enabled != true)
            return new MetadataRenderResult();

        var fields = config.Fields;
        return new MetadataRenderResult
        {
            InfoPanelSections = BuildCommonSection(fields, commonValues),
            ContentTopItems = BuildContentTopRows(fields, commonValues, "common")
        };
    }

    /// <summary>
    /// 只渲染格式特有信息 section（如 图片信息、音频信息…）。
    /// </summary>
    public static MetadataRenderResult RenderFormat(
        Dictionary<string, string?> formatValues,
        MetadataPanelSettings settings,
        string typeKey)
    {
        var config = settings.Types.GetValueOrDefault(typeKey);
        if (config?.Enabled != true)
            return new MetadataRenderResult();

        var fields = config.Fields;
        return new MetadataRenderResult
        {
            InfoPanelSections = BuildFormatSection(fields, formatValues, typeKey),
            ContentTopItems = BuildContentTopRows(fields, formatValues, typeKey)
        };
    }

    // ── Private helpers ──

    private static ObservableCollection<MetadataSection> BuildCommonSection(
        Dictionary<string, FieldConfig> fields,
        Dictionary<string, string?> values)
    {
        var sections = new ObservableCollection<MetadataSection>();
        var section = new MetadataSection
        {
            Title = LocalizationManager.T("Metadata_FileInfo"),
            ShowSeparator = false
        };
        var displayNames = MetadataRegistry.GetFields("common")
            .ToDictionary(f => f.Key, f => f.DisplayName);
        BuildRows(section, fields.Where(kv => kv.Value.Position == "infoPanel"), values, displayNames);
        if (section.Rows.Count > 0)
            sections.Add(section);
        return sections;
    }

    private static ObservableCollection<MetadataSection> BuildFormatSection(
        Dictionary<string, FieldConfig> fields,
        Dictionary<string, string?> values,
        string typeKey)
    {
        var sections = new ObservableCollection<MetadataSection>();
        var section = new MetadataSection
        {
            Title = GetTypeDisplayName(typeKey),
            ShowSeparator = false
        };
        var displayNames = MetadataRegistry.GetFields(typeKey)
            .ToDictionary(f => f.Key, f => f.DisplayName);
        BuildRows(section, fields.Where(kv => kv.Value.Position == "infoPanel"), values, displayNames);
        if (section.Rows.Count > 0)
            sections.Add(section);
        return sections;
    }

    private static void BuildRows(
        MetadataSection section,
        IEnumerable<KeyValuePair<string, FieldConfig>> fieldEntries,
        Dictionary<string, string?> values,
        Dictionary<string, string> displayNames)
    {
        var grouped = fieldEntries
            .GroupBy(kv => kv.Value.Row)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var items = group
                .OrderBy(kv => kv.Value.Order)
                .Select(kv => new FormatMetadataItem(
                    GetFieldDisplayName(kv.Key, displayNames),
                    values.GetValueOrDefault(kv.Key) ?? "-"))
                .ToList();

            if (items.Count == 0) continue;

            var row = new InfoPanelRow();
            foreach (var item in items)
                row.Items.Add(item);
            section.Rows.Add(row);
        }
    }

    private static ObservableCollection<InfoPanelRow> BuildContentTopRows(
        Dictionary<string, FieldConfig> fields,
        Dictionary<string, string?> values,
        string typeKey)
    {
        var displayNames = MetadataRegistry.GetFields(typeKey)
            .ToDictionary(f => f.Key, f => f.DisplayName);
        var rows = new ObservableCollection<InfoPanelRow>();
        var grouped = fields
            .Where(kv => kv.Value.Position == "contentTop")
            .GroupBy(kv => kv.Value.Row)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var items = group
                .OrderBy(kv => kv.Value.Order)
                .Select(kv => new FormatMetadataItem(
                    GetFieldDisplayName(kv.Key, displayNames),
                    values.GetValueOrDefault(kv.Key) ?? "-"))
                .ToList();

            if (items.Count == 0) continue;

            var row = new InfoPanelRow();
            foreach (var item in items)
                row.Items.Add(item);
            rows.Add(row);
        }
        return rows;
    }

    private static string GetTypeDisplayName(string typeKey)
    {
        // 优先使用 i18n 键，其次 fallback
        var key = $"Metadata_Type_{typeKey}";
        var localized = LocalizationManager.T(key);
        if (localized != key) return localized;

        return LocalizationManager.T("Metadata_FormatInfo");
    }

    /// <summary>
    /// 获取字段的显示名称。优先用 i18n (Metadata_Key_{fieldKey})，其次用 registry DisplayName（中文），最后 fallback 到 fieldKey。
    /// </summary>
    private static string GetFieldDisplayName(string fieldKey, Dictionary<string, string> displayNames)
    {
        var i18nKey = $"Metadata_Key_{fieldKey}";
        var localized = LocalizationManager.T(i18nKey);
        if (localized != i18nKey) return localized;
        return displayNames.GetValueOrDefault(fieldKey, fieldKey);
    }
}

public class MetadataRenderResult
{
    public ObservableCollection<MetadataSection> InfoPanelSections { get; set; } = [];
    public ObservableCollection<InfoPanelRow> ContentTopItems { get; set; } = [];
}

/// <summary>
/// 信息栏的一个分区（如 "文件信息" 或 "图片信息"）。
/// </summary>
public class MetadataSection
{
    public string Title { get; set; } = string.Empty;
    public ObservableCollection<InfoPanelRow> Rows { get; set; } = [];
    public bool ShowSeparator { get; set; }
}

/// <summary>
/// 信息栏内的一行（包含多个同行的字段）。
/// </summary>
public class InfoPanelRow
{
    public ObservableCollection<FormatMetadataItem> Items { get; set; } = [];
}
