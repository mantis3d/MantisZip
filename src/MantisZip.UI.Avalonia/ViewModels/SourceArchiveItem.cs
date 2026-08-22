using CommunityToolkit.Mvvm.ComponentModel;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// 源压缩包列表行的健康状态（解压设置窗口逐包校验结果）。
/// </summary>
public enum SourceArchiveStatus
{
    /// <summary>尚未开始校验。</summary>
    Pending,

    /// <summary>正在读取条目列表。</summary>
    Validating,

    /// <summary>头部索引可正常读取。</summary>
    Ok,

    /// <summary>已加密，需要密码才能读取（不算损坏）。</summary>
    NeedsPassword,

    /// <summary>无法读取（损坏或格式不支持）。</summary>
    Failed,
}

/// <summary>
/// 解压设置窗口「源压缩包」列表的行模型：路径 + 校验状态 + 失败原因。
/// 状态由逐包 ListEntriesAsync 校验驱动，点击行时预览树跟随切换。
/// </summary>
public partial class SourceArchiveItem : ObservableObject
{
    /// <summary>压缩包完整路径。</summary>
    public string Path { get; }

    /// <summary>显示名（文件名）。</summary>
    public string DisplayName { get; }

    public SourceArchiveItem(string path)
    {
        Path = path;
        DisplayName = System.IO.Path.GetFileName(path);
    }

    [ObservableProperty]
    private SourceArchiveStatus _status = SourceArchiveStatus.Pending;

    /// <summary>失败原因原文（行 tooltip 与预览树错误占位显示；Ok 时为 null）。</summary>
    [ObservableProperty]
    private string? _errorMessage;

    partial void OnErrorMessageChanged(string? value)
        => OnPropertyChanged(nameof(RowToolTip));

    /// <summary>状态图标（emoji，随 Status 联动）。</summary>
    public string StatusIcon => Status switch
    {
        SourceArchiveStatus.Validating => "⏳",
        SourceArchiveStatus.Ok => "✅",
        SourceArchiveStatus.NeedsPassword => "🔒",
        SourceArchiveStatus.Failed => "⚠️",
        _ => "·",
    };

    partial void OnStatusChanged(SourceArchiveStatus value)
        => OnPropertyChanged(nameof(StatusIcon));

    /// <summary>行 tooltip：失败时显示原因，否则显示完整路径。</summary>
    public string RowToolTip => ErrorMessage ?? Path;
}
