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

    /// <summary>探测到内容加密（B 类列出条目含 IsEncrypted，或 A 类解锁后置位）。</summary>
    public bool IsEncrypted { get; set; }

    /// <summary>已匹配/手动输入成功的密码（非 null = 已解锁）。仅内存传递，不落日志。</summary>
    public string? MatchedPassword { get; set; }

    /// <summary>匹配密码的描述（来自密码库条目或用户输入）。</summary>
    public string? MatchedDescription { get; set; }

    /// <summary>记录解锁结果并触发图标刷新（MatchedPassword 是普通属性，无生成通知）。</summary>
    public void SetMatched(string? password, string? description)
    {
        MatchedPassword = password;
        MatchedDescription = description;
        OnPropertyChanged(nameof(StatusIconKey));
        OnPropertyChanged(nameof(StatusForegroundKey));
    }

    /// <summary>状态图标资源键（AppIcons.axaml 矢量几何，与树节点图标体系一致）。随加密切态联动。</summary>
    public string StatusIconKey => Status switch
    {
        SourceArchiveStatus.Validating => "IconArchiveClock",
        SourceArchiveStatus.NeedsPassword => "IconLockClosed",
        SourceArchiveStatus.Failed => "IconWarning",
        // 加密三态：未匹配 钥匙 / 已解锁 开锁 / 普通包 对勾
        SourceArchiveStatus.Ok when IsEncrypted && MatchedPassword != null => "IconLockOpen",
        SourceArchiveStatus.Ok when IsEncrypted => "IconKey",
        SourceArchiveStatus.Ok => "IconCheckmark",
        _ => "IconTimer",
    };

    /// <summary>状态前景色键（复用 NodeForegroundConverter："ConflictRed" 红 / "Blue" 蓝 / "Green" 绿 / "Yellow" 黄 / null 默认主题色）。</summary>
    public string? StatusForegroundKey =>
        Status switch
        {
            SourceArchiveStatus.Failed => "ConflictRed",
            SourceArchiveStatus.NeedsPassword => "ConflictRed",
            SourceArchiveStatus.Ok when IsEncrypted && MatchedPassword == null => "Blue",
            SourceArchiveStatus.Ok when IsEncrypted => "Yellow",
            SourceArchiveStatus.Ok => "Green",
            _ => null,
        };

    partial void OnStatusChanged(SourceArchiveStatus value)
    {
        OnPropertyChanged(nameof(StatusIconKey));
        OnPropertyChanged(nameof(StatusForegroundKey));
        OnPropertyChanged(nameof(RowToolTip));
    }

    /// <summary>行 tooltip：失败时显示原因，否则显示完整路径。</summary>
    public string RowToolTip => ErrorMessage ?? Path;
}
