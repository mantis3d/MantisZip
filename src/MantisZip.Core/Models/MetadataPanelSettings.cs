using System.Collections.Generic;

namespace MantisZip.Core.Models;

/// <summary>
/// 元数据面板的全局配置。
/// </summary>
public class MetadataPanelSettings
{
    /// <summary>
    /// 决定通用和格式信息的上下顺序。
    /// "common" = 通用文件信息, "format" = 当前格式特有的信息。
    /// </summary>
    public List<string> SectionOrder { get; set; } = ["common", "format"];

    /// <summary>
    /// 每个类型的配置，包括 "common"。
    /// </summary>
    public Dictionary<string, TypeMetadataConfig> Types { get; set; } = new();

    /// <summary>
    /// 信息面板字段的显示方向："vertical"（上下）或 "horizontal"（左右）。
    /// </summary>
    public string FieldLayoutMode { get; set; } = "vertical";
}

/// <summary>
/// 单个类型（如 image, common）的配置。
/// </summary>
public class TypeMetadataConfig
{
    /// <summary>
    /// 是否启用该类型的特有字段显示。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 该类型下的字段配置。
    /// </summary>
    public Dictionary<string, FieldConfig> Fields { get; set; } = new();
}

/// <summary>
/// 单个字段的配置。
/// </summary>
public class FieldConfig
{
    /// <summary>
    /// 显示位置： "infoPanel" (底部固定), "contentTop" (滚动区顶部), "hidden"。
    /// </summary>
    public string Position { get; set; } = "infoPanel";

    /// <summary>
    /// 排序权重。10, 20, 30... 间隔便于后续插入。
    /// </summary>
    public int Order { get; set; } = 10;

    /// <summary>
    /// 行号。相同 Row 的字段在同一行并排显示，不同 Row 换行。
    /// </summary>
    public int Row { get; set; }
}
