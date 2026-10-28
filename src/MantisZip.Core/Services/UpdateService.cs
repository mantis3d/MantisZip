using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using MantisZip.Core.Utils;

namespace MantisZip.Core.Services;

/// <summary>
/// 自动更新检测服务（Core 层，不引用任何 UI / 本地化类型）。
/// <para>
/// 通过 GitHub 的 <c>releases/latest</c> 接口判断是否有比当前版本更新的正式发布。
/// 网络 / 超时 / 取消 / JSON 结构异常全部在内部吞掉并写诊断日志，调用方只会得到
/// <c>null</c>（= 无更新或检查失败），不会收到异常。
/// </para>
/// <para>
/// JSON 解析使用 <see cref="JsonDocument"/> 手写读取，**不使用**
/// <c>JsonSerializer.Deserialize&lt;T&gt;</c>：本仓库有裁剪 / AOT 分析器门禁，
/// 反射式序列化会触发 IL2026 / IL3050。
/// </para>
/// </summary>
public static class UpdateService
{
    /// <summary>GitHub REST API 根地址。</summary>
    private const string GitHubApiBase = "https://api.github.com";

    /// <summary>GitHub 网页根地址（用于拼发布页链接）。</summary>
    private const string GitHubWebBase = "https://github.com";

    /// <summary>HTTP 超时秒数：GitHub 不可达时不应拖慢启动流程。</summary>
    private const int RequestTimeoutSeconds = 5;

    /// <summary><see cref="UpdateCheckFrequency.Daily"/> 档的最小检查间隔（24 小时）。</summary>
    private static readonly TimeSpan DailyCheckInterval = TimeSpan.FromHours(24);

    /// <summary><see cref="UpdateCheckFrequency.Weekly"/> 档的最小检查间隔（7 天）。</summary>
    private static readonly TimeSpan WeeklyCheckInterval = TimeSpan.FromDays(7);

    /// <summary>
    /// 发布说明正文的最大保留长度。
    /// <para>
    /// GitHub 的 release body 没有长度限制，而这段文本最终要进 UI 渲染（更新提示窗 / 关于窗）：
    /// 异常大的响应会明显拖慢界面，因此在解析处直接截断。
    /// </para>
    /// </summary>
    private const int MaxReleaseNotesLength = 20000;

    /// <summary>
    /// 共享的静态 <see cref="HttpClient"/>：复用 socket / 连接池，避免每次检查都新建。
    /// <para>
    /// <see cref="HttpClient"/> 的 handler 一旦创建就无法原地替换，因此测试注入
    /// （<see cref="SetTestHandler"/>）是「整体重建客户端并释放旧实例」，用
    /// <see cref="Interlocked.Exchange{T}(ref T, T)"/> 保证替换本身原子。
    /// </para>
    /// </summary>
    private static HttpClient _client = CreateClient(null);

    /// <summary>
    /// 检查是否有新版本可用。
    /// </summary>
    /// <param name="currentVersion">当前版本号（如 <c>0.5.4</c>，也接受带 <c>v</c> 前缀）。</param>
    /// <param name="repository">GitHub 仓库，格式 <c>owner/name</c>。</param>
    /// <param name="cancellationToken">取消令牌；取消时返回 <c>null</c>，不抛出异常。</param>
    /// <returns>
    /// 仅当最新正式版严格大于当前版本时返回 <see cref="UpdateInfo"/>；
    /// 已是最新、无正式发布、版本号无法解析、网络失败、超时、取消均返回 <c>null</c>。
    /// </returns>
    public static async Task<UpdateInfo?> CheckForUpdateAsync(
        string currentVersion,
        string repository = "mantis3d/MantisZip",
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 连当前版本都解析不了就没必要发请求（也避免用脏版本号做比较）
            if (!TryParseVersion(currentVersion, out var current))
            {
                CoreLog.Info($"[UpdateService] 当前版本号无法解析：{currentVersion}");
                return null;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{GitHubApiBase}/repos/{repository}/releases/latest");

            // GitHub API 强制要求 User-Agent（缺失会直接 403）；Accept 固定为推荐的媒体类型
            request.Headers.TryAddWithoutValidation("User-Agent", $"MantisZip/{currentVersion}");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

            using var response = await _client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 404（仓库无 release）等 HTTP 错误按「无更新」处理，不视为异常
                CoreLog.Info($"[UpdateService] GitHub 返回 HTTP {(int)response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseLatestRelease(json, current, repository);
        }
        catch (Exception ex)
        {
            // 网络 / 超时 / 取消 / JSON 结构异常一律兜底：自动更新检查失败不能影响主流程。
            // 注意：这里不写空 catch，诊断日志是排查「检查更新没反应」的唯一线索。
            CoreLog.Info($"[UpdateService] 检查更新失败：{ex.GetType().Name} {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 按设置里选定的频率判断这次是否应该发起自动检查（两个时间都应是 UTC）。
    /// </summary>
    /// <param name="frequency">用户选择的检查频率。</param>
    /// <param name="lastCheckTime">上次检查时间；<c>null</c> 表示从未检查过。</param>
    /// <param name="nowUtc">当前 UTC 时间（由调用方传入，便于测试）。</param>
    /// <returns>本次应检查返回 <c>true</c>，否则 <c>false</c>。</returns>
    public static bool ShouldCheck(UpdateCheckFrequency frequency, DateTime? lastCheckTime, DateTime nowUtc)
        => frequency switch
        {
            UpdateCheckFrequency.Never => false,
            UpdateCheckFrequency.EveryStartup => true,
            UpdateCheckFrequency.Daily => IsIntervalElapsed(lastCheckTime, nowUtc, DailyCheckInterval),
            UpdateCheckFrequency.Weekly => IsIntervalElapsed(lastCheckTime, nowUtc, WeeklyCheckInterval),
            // 防御性兜底：枚举越界值（例如将来新增档位、旧程序读到新配置）按 Daily 处理，绝不抛异常
            _ => IsIntervalElapsed(lastCheckTime, nowUtc, DailyCheckInterval),
        };

    /// <summary>
    /// 从未检查过，或距上次检查已满 <paramref name="interval"/> 时返回 <c>true</c>。
    /// </summary>
    private static bool IsIntervalElapsed(DateTime? lastCheckTime, DateTime nowUtc, TimeSpan interval)
        => lastCheckTime is null || nowUtc - lastCheckTime.Value >= interval;

    /// <summary>
    /// 测试专用：替换静态 <see cref="HttpClient"/> 使用的 handler。
    /// <paramref name="handler"/> 为 <c>null</c> 时恢复默认网络客户端。
    /// <para>
    /// 测试必须用 <c>try / finally</c> 保证用例结束后传 <c>null</c>，否则静态客户端会一直指向桩，
    /// 污染同进程内的其他用例。
    /// </para>
    /// </summary>
    internal static void SetTestHandler(HttpMessageHandler? handler)
    {
        var previous = Interlocked.Exchange(ref _client, CreateClient(handler));
        // 旧客户端可能正被并发的检查请求持有；本方法仅用于测试，测试类已串行执行
        previous.Dispose();
    }

    /// <summary>
    /// 解析 <c>releases/latest</c> 的 JSON 响应。
    /// 结构不合法、tag 无法解析、预发布、版本不新于当前版本时返回 <c>null</c>。
    /// </summary>
    private static UpdateInfo? ParseLatestRelease(string json, Version current, string repository)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            CoreLog.Info("[UpdateService] 响应根节点不是 JSON 对象");
            return null;
        }

        // /releases/latest 本身已排除 pre-release 与 draft；这里再兜底一次，
        // 防止将来接口语义变化或响应被替换时把预发布当正式版推给用户
        if (root.TryGetProperty("prerelease", out var prerelease)
            && prerelease.ValueKind == JsonValueKind.True)
        {
            CoreLog.Info("[UpdateService] 最新发布被标记为预发布，忽略");
            return null;
        }

        if (!root.TryGetProperty("tag_name", out var tagElement)
            || tagElement.ValueKind != JsonValueKind.String)
        {
            CoreLog.Info("[UpdateService] 响应缺少 tag_name");
            return null;
        }

        var rawTag = tagElement.GetString();
        var latestVersion = StripVersionPrefix(rawTag);
        if (!TryParseVersion(latestVersion, out var latest))
        {
            CoreLog.Info($"[UpdateService] 发布 tag 无法解析：{rawTag}");
            return null;
        }

        // 逐段数值比较（0.5.10 > 0.5.3）：相等或更旧都不提示
        if (latest.CompareTo(current) <= 0)
            return null;

        var releaseUrl = $"{GitHubWebBase}/{repository}/releases/tag/v{latestVersion}";
        return new UpdateInfo(
            LatestVersion: latestVersion,
            DownloadUrl: releaseUrl,
            ReleaseNotesUrl: releaseUrl,
            ReleaseDate: ParsePublishedAt(root),
            ReleaseNotes: ParseReleaseNotes(root));
    }

    /// <summary>
    /// 读取 <c>body</c>（发布说明的 Markdown 原文）并裁掉首尾空白。
    /// 字段缺失或不是字符串时返回空串；超过 <see cref="MaxReleaseNotesLength"/> 字符时截断。
    /// </summary>
    private static string ParseReleaseNotes(JsonElement root)
    {
        if (!root.TryGetProperty("body", out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        var notes = element.GetString()?.Trim() ?? string.Empty;
        return notes.Length > MaxReleaseNotesLength
            ? notes[..MaxReleaseNotesLength]
            : notes;
    }

    /// <summary>
    /// 读取 <c>published_at</c> 并解析为 UTC 时间；字段缺失、不是字符串或解析失败时返回 <c>null</c>。
    /// </summary>
    private static DateTime? ParsePublishedAt(JsonElement root)
    {
        if (!root.TryGetProperty("published_at", out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTime.TryParse(
            element.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var publishedAt)
            ? publishedAt
            : null;
    }

    /// <summary>
    /// 去掉版本号前导的 <c>v</c> / <c>V</c> 并裁掉首尾空白（<c>v0.5.4</c> → <c>0.5.4</c>）。
    /// </summary>
    private static string StripVersionPrefix(string? text)
        => (text ?? string.Empty).Trim().TrimStart('v', 'V');

    /// <summary>
    /// 把版本号补齐到 3 段后用 <see cref="Version"/> 解析：<c>1.2</c> 与 <c>1.2.0</c> 视为相等，
    /// 多余段（如 <c>1.2.3.4</c>）只比较前 3 段。解析失败返回 <c>false</c>（调用方当作「无更新」）。
    /// </summary>
    private static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (!Version.TryParse(StripVersionPrefix(text), out var parsed))
            return false;

        // 只有 2 段时 Build 为 -1，补成 0；Revision 留给默认值 -1，从而不参与比较
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }

    /// <summary>
    /// 按给定 handler 创建客户端；<paramref name="handler"/> 为 <c>null</c> 时使用默认网络栈。
    /// </summary>
    private static HttpClient CreateClient(HttpMessageHandler? handler)
    {
        // disposeHandler:false —— 测试桩 handler 的生命周期由用例自己管理（通常配合 using），
        // 客户端被替换 / 释放时不应把桩提前 Dispose 掉
        var client = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds);
        return client;
    }
}
