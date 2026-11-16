namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 设置页「文件名模板」的三行入口（每行各记一份模板默认值 + 一个预设名框）。
/// 让预设的存/删知道该写哪一行，避免三次复制同一段逻辑。
/// </summary>
public enum FileNameTemplateRow
{
    /// <summary>压缩对话框的默认模板。</summary>
    Dialog,

    /// <summary>右键快速压缩（无对话框入口）的默认模板。</summary>
    Quick,

    /// <summary>CLI <c>--compress-separate</c> 的默认模板。</summary>
    Separate,
}
