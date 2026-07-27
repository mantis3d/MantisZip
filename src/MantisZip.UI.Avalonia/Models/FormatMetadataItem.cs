namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 元数据面板的一个字段键值对。
/// </summary>
/// <param name="Key">字段显示名称（如 "文件名", "大小"）</param>
/// <param name="Value">字段值（如 "report.docx", "1.2 MB"）</param>
public record FormatMetadataItem(string Key, string Value);
