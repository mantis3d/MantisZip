namespace MantisZip.Core.Services;

/// <summary>
/// 一次「发现新版本」检查的结果快照。
/// <para>
/// 纯数据模型：不含任何 UI / 本地化依赖，<see cref="PublishDateDisplay"/> 只做
/// <c>yyyy-MM-dd</c> 的格式化（不跟随当前语言区域），显示文案由 UI 层自行决定。
/// </para>
/// </summary>
/// <param name="LatestVersion">最新版本号，已去掉 <c>v</c> 前缀（如 <c>0.5.4</c>）。</param>
/// <param name="DownloadUrl">发布页地址（浏览器打开后由用户自行下载）。</param>
/// <param name="ReleaseNotesUrl">发布说明地址（GitHub 上发布页即说明页，故与 <paramref name="DownloadUrl"/> 相同）。</param>
/// <param name="ReleaseDate">发布日期（UTC）；接口未给出或无法解析时为 <c>null</c>。</param>
/// <param name="ReleaseNotes">
/// 发布说明正文：GitHub release <c>body</c> 字段的 Markdown 原文。
/// 接口未给出该字段、字段为 <c>null</c> 或为空时是空串；
/// 长度已在解析处（<see cref="UpdateService"/>）截断，最长 20000 字符。
/// </param>
public readonly record struct UpdateInfo(
    string LatestVersion,
    string DownloadUrl,
    string ReleaseNotesUrl,
    DateTime? ReleaseDate,
    string ReleaseNotes = "")
{
    /// <summary>
    /// 发布日期显示文本（<c>yyyy-MM-dd</c>）。无发布日期时返回空字符串，由调用方决定是否隐藏该行。
    /// </summary>
    public string PublishDateDisplay => ReleaseDate?.ToString("yyyy-MM-dd") ?? string.Empty;
}
