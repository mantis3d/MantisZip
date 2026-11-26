using System;

namespace MantisZip.Core.Utils;

/// <summary>
/// 用户保存的文件名模板预设（形态对齐 <c>Core/FileFilter/FileFilterPreset</c>：
/// 属性 + 覆盖 <see cref="ToString"/> 供 ComboBox 直接显示）。
/// </summary>
public class FileNameTemplatePreset
{
    /// <summary>预设名称（ComboBox 里显示的文字）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>模板正文（占位符原样保存，不做转义）。</summary>
    public string Template { get; set; } = string.Empty;

    public FileNameTemplatePreset() { }

    public FileNameTemplatePreset(string name, string template)
    {
        Name = name ?? string.Empty;
        Template = template ?? string.Empty;
    }

    /// <summary>供 ComboBox / ListBox 直接显示预设名。</summary>
    public override string ToString() => Name;
}
