using System.Collections.ObjectModel;
using System.Linq;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// 元数据更新辅助工具。处理通用的 FormatMetadata（向后兼容）和新的 CommonSections / FormatSections 之间的同步。
/// RenderCommonToViewModel / RenderFormatToViewModel 互不覆盖。
/// </summary>
public static class MetadataHelper
{
    /// <summary>
    /// 渲染通用信息（文件信息）到 CommonSections，同时更新 FormatMetadata 以向后兼容。
    /// 在 Phase 1 调用——格式检测之前。
    /// </summary>
    public static void RenderCommonToViewModel(
        PreviewViewModel vm,
        Dictionary<string, string?> commonValues)
    {
        var settings = MetadataSettingsManager.Load();
        var result = MetadataRenderEngine.RenderCommon(commonValues, settings);

        vm.CommonSections = result.InfoPanelSections;
        vm.ContentTopItems = result.ContentTopItems;
        vm.IsFormatPending = true;
        vm.HasFormatSections = false;

        // FormatMetadata ← infoPanel + contentTop 字段合并
        var flat = FlattenSections(result.InfoPanelSections);
        foreach (var row in result.ContentTopItems)
            foreach (var item in row.Items)
                flat.Add(item);
        vm.FormatMetadata = flat;
    }

    /// <summary>
    /// 渲染格式特有信息（如 图片信息、音频信息…）到 FormatSections，同时更新 FormatMetadata 以向后兼容。
    /// 在 Phase 2 调用——格式检测之后。
    /// </summary>
    public static void RenderFormatToViewModel(
        PreviewViewModel vm,
        Dictionary<string, string?> formatValues,
        string typeKey)
    {
        var settings = MetadataSettingsManager.Load();
        var result = MetadataRenderEngine.RenderFormat(formatValues, settings, typeKey);

        vm.FormatSections = result.InfoPanelSections;
        vm.HasFormatSections = result.InfoPanelSections.Count > 0;
        vm.IsFormatPending = false;

        // ContentTop: 追加格式行到通用行之后
        var merged = new ObservableCollection<InfoPanelRow>(vm.ContentTopItems);
        foreach (var row in result.ContentTopItems)
            merged.Add(row);
        vm.ContentTopItems = merged;

        // FormatMetadata ← infoPanel + contentTop 字段合并
        var flat = FlattenSections(result.InfoPanelSections);
        foreach (var row in result.ContentTopItems)
            foreach (var item in row.Items)
                flat.Add(item);
        vm.FormatMetadata = flat;
    }

    private static ObservableCollection<FormatMetadataItem> FlattenSections(
        ObservableCollection<MetadataSection> sections)
    {
        var items = new ObservableCollection<FormatMetadataItem>();
        foreach (var section in sections)
            foreach (var row in section.Rows)
                foreach (var item in row.Items)
                    items.Add(item);
        return items;
    }
}
