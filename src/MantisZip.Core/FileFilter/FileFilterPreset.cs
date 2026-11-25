using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MantisZip.Core.FileFilter;

/// <summary>
/// 文件过滤预设。包含预设名称和过滤条件。
/// </summary>
public class FileFilterPreset
{
    /// <summary>预设名称（用户预设的用户输入名；内置预设恒为空，名字走 <see cref="NameKey"/>）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>过滤条件</summary>
    public FileFilterCriteria Criteria { get; set; } = new();

    /// <summary>是否为内置预设（用户不可删除/修改）</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// 内置预设名对应的 i18n 键（如 <c>FileFilter_BuiltInPreset_Image</c>）；
    /// 用户预设恒为 null，直接显示 <see cref="Name"/>。
    /// </summary>
    /// <remarks>
    /// 内置名是界面文案，不能写死中文——英文界面下会原样显示中文。
    /// 与 <c>Core/Models/AdaptiveOverrideRule.NameKey</c> 同一套做法，
    /// 但这里**不能**像它那样把 <see cref="Name"/> 清空就完事：
    /// <see cref="Name"/> 在本类型里兼任运行期标识（预设下拉的选中回找、
    /// 基底预设是否还在的判定都按名字比对），所以另引入 <see cref="Key"/>
    /// 承担标识职责，显示名与标识名分开。
    /// </remarks>
    public string? NameKey { get; set; }

    /// <summary>
    /// 运行期标识：内置预设为 <see cref="NameKey"/>，用户预设为 <see cref="Name"/>。
    /// </summary>
    /// <remarks>
    /// 凡是「这是哪一个预设」的判定一律用本属性，不要再用 <see cref="Name"/>：
    /// 内置预设的 <see cref="Name"/> 恒为空串，拿它比对会让 8 条内置预设全部命中空串。
    /// 不参与序列化（它是计算值，写进 settings.json 只会在改名后变成过期快照）。
    /// </remarks>
    [JsonIgnore]
    public string Key => NameKey ?? Name;

    public FileFilterPreset() { }

    public FileFilterPreset(string name, FileFilterCriteria criteria, bool isBuiltIn = false)
    {
        Name = name;
        Criteria = criteria ?? new FileFilterCriteria();
        IsBuiltIn = isBuiltIn;
    }

    /// <summary>
    /// 返回预设名用于 ComboBox 显示。
    /// </summary>
    /// <remarks>
    /// 内置预设在这里返回 i18n 键而不是本地化文本：Core 层不引用 UI 的
    /// <c>LocalizationManager</c>，解析交给调用方（<c>Controls/FileFilterEditor</c>）。
    /// 用户预设原样返回 <see cref="Name"/>。
    /// </remarks>
    public override string ToString() => NameKey ?? Name;

    /// <summary>
    /// 获取 8 个内置预设。
    /// </summary>
    /// <remarks>
    /// 名字一律填 <see cref="NameKey"/>、不填 <see cref="Name"/>：
    /// 内置名是界面文案，硬编码中文会让英文/繁中界面显示中文。
    /// </remarks>
    public static List<FileFilterPreset> GetBuiltInPresets()
    {
        var now = DateTime.Now;
        return new List<FileFilterPreset>
        {
            new(string.Empty, new FileFilterCriteria
            {
                IncludeExtensions = new List<string> { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg" }
            }, isBuiltIn: true) { NameKey = "FileFilter_BuiltInPreset_Image" },

            new(string.Empty, new FileFilterCriteria
            {
                IncludeExtensions = new List<string> { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma" }
            }, isBuiltIn: true) { NameKey = "FileFilter_BuiltInPreset_Audio" },

            new(string.Empty, new FileFilterCriteria
            {
                IncludeExtensions = new List<string> { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv" }
            }, isBuiltIn: true) { NameKey = "FileFilter_BuiltInPreset_Video" },

            new(string.Empty, new FileFilterCriteria
            {
                IncludeExtensions = new List<string> { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt" }
            }, isBuiltIn: true) { NameKey = "FileFilter_BuiltInPreset_Document" },

            new(string.Empty, new FileFilterCriteria
            {
                IncludeExtensions = new List<string> { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz" }
            }, isBuiltIn: true) { NameKey = "FileFilter_BuiltInPreset_Archive" },

            new(string.Empty, new FileFilterCriteria
            {
                MinSize = 100L * 1024 * 1024
            }, isBuiltIn: true) { NameKey = "FileFilter_BuiltInPreset_LargeFile" },

            new(string.Empty, new FileFilterCriteria
            {
                MinDate = new DateTime(now.Year, now.Month, 1)
            }, isBuiltIn: true) { NameKey = "FileFilter_BuiltInPreset_ThisMonth" },

            new(string.Empty, new FileFilterCriteria
            {
                NamePattern = "*.tmp;*.cache;*.log;*.bak"
            }, isBuiltIn: true) { NameKey = "FileFilter_BuiltInPreset_ExcludeTemp" },
        };
    }
}
