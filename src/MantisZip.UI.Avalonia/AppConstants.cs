namespace MantisZip.UI.Avalonia;

/// <summary>
/// 应用级常量
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// 版本号（仅数字）
    /// </summary>
    public const string Version = "0.5.0";

    /// <summary>
    /// 状态栏显示的版本号（自动加 v 前缀）
    /// </summary>
    public static string VersionDisplay => "v" + Version;

    /// <summary>
    /// 是否显示主菜单的「测试」菜单（Debug 构建显示，Release 构建自动隐藏）
    /// </summary>
    public const bool ShowTestMenu =
#if DEBUG
        true;
#else
        false;
#endif
}
