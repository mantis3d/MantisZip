#if DEBUG
using System;
using System.IO;
#endif
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 更新检查的「当前版本」解析器（**调试夹具，仅 Debug 构建生效**）。
/// <para>
/// 为什么要它：本地开发版（如 0.5.4）永远比 GitHub 上已发布的正式版（如 0.5.3）新，
/// 于是「发现新版本」这条链路在开发者机器上**永远走不到**，只能靠改 <see cref="AppConstants.Version"/>
/// 硬编码去测——那会同时弄红 <c>verify-repo.ps1 -Task version</c> 的六处版本校验。
/// </para>
/// <para>
/// 用法：在应用数据目录写一行版本号到 <c>fake-current-version.txt</c>
/// （普通模式 <c>%LOCALAPPDATA%\MantisZip\</c>，便携模式 <c>exe 旁 Data\</c>），
/// 更新检查就会拿这个假版本号去和线上最新版比。删掉文件即恢复真实版本。
/// 启动检查与「关于 → 更新」的手动检查都认它；Release 构建把整段编译掉，行为恒等于真实版本。
/// </para>
/// </summary>
internal static class DebugUpdateVersion
{
#if DEBUG
    /// <summary>覆盖文件名（与 settings.json 同目录，便于手工编辑与清理）。</summary>
    public const string OverrideFileName = "fake-current-version.txt";
#endif

    /// <summary>
    /// 参与版本比较的「当前版本」。Release 构建恒为 <see cref="AppConstants.Version"/>；
    /// Debug 构建在有合法覆盖文件时取文件内容（必须是能解析的版本号，否则忽略并记日志）。
    /// </summary>
    public static string Effective
    {
        get
        {
#if DEBUG
            try
            {
                var path = Path.Combine(AppSettings.DataDir, OverrideFileName);
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path).Trim();
                    if (Version.TryParse(text, out _))
                        return text;

                    // 文件存在但内容不是合法版本号：不静默失效，留一条日志便于自查
                    App.DebugLog($"[DebugUpdateVersion] 忽略非法版本号覆盖：\"{text}\"");
                }
            }
            catch (Exception ex)
            {
                // 夹具失败绝不能影响真实的更新检查
                App.DebugLog($"[DebugUpdateVersion] 读取覆盖文件失败：{ex.GetType().Name} {ex.Message}");
            }
#endif
            return AppConstants.Version;
        }
    }

    /// <summary>当前是否正被调试夹具覆盖（供界面提示用；Release 恒为 false）。</summary>
    public static bool IsOverridden
    {
        get
        {
#if DEBUG
            return !string.Equals(Effective, AppConstants.Version, StringComparison.Ordinal);
#else
            return false;
#endif
        }
    }
}
