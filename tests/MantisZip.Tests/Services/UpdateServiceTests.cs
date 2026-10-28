using System.Net;
using System.Net.Http;
using System.Text;
using MantisZip.Core.Services;
using Xunit;

namespace MantisZip.Tests.Services;

/// <summary>
/// <see cref="UpdateService"/> 的单元测试。
/// 静态 <c>HttpClient</c> 是进程级共享状态，本类通过独立 collection 与其他测试类隔离，
/// 每个用例再用 <c>try / finally</c> 把桩 handler 复位为 <c>null</c>。
/// </summary>
[Collection("UpdateServiceSerial")]
public class UpdateServiceTests
{
    // ── 检查频率 ──

    [Fact]
    public void ShouldCheck_Never_ReturnsFalse_EvenWhenNeverChecked()
    {
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(UpdateService.ShouldCheck(UpdateCheckFrequency.Never, null, now));
    }

    [Fact]
    public void ShouldCheck_Never_ReturnsFalse_EvenWhenLongOverdue()
    {
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(UpdateService.ShouldCheck(UpdateCheckFrequency.Never, now.AddDays(-365), now));
    }

    [Fact]
    public void ShouldCheck_EveryStartup_ReturnsTrue_EvenAfterJustChecked()
    {
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

        // 每次启动都检查：刚检查过也要检查；从未检查过当然也检查
        Assert.True(UpdateService.ShouldCheck(UpdateCheckFrequency.EveryStartup, now.AddMinutes(-1), now));
        Assert.True(UpdateService.ShouldCheck(UpdateCheckFrequency.EveryStartup, null, now));
    }

    [Fact]
    public void ShouldCheck_Daily_NeverChecked_ReturnsTrue()
    {
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(UpdateService.ShouldCheck(UpdateCheckFrequency.Daily, null, now));
    }

    [Fact]
    public void ShouldCheck_Daily_23h59m_ReturnsFalse()
    {
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(UpdateService.ShouldCheck(UpdateCheckFrequency.Daily, now.AddHours(-23).AddMinutes(-59), now));
    }

    [Fact]
    public void ShouldCheck_Daily_Exactly24h_ReturnsTrue()
    {
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(UpdateService.ShouldCheck(UpdateCheckFrequency.Daily, now.AddHours(-24), now));
    }

    [Fact]
    public void ShouldCheck_Weekly_NeverChecked_ReturnsTrue()
    {
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(UpdateService.ShouldCheck(UpdateCheckFrequency.Weekly, null, now));
    }

    [Fact]
    public void ShouldCheck_Weekly_6d23h_ReturnsFalse()
    {
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(UpdateService.ShouldCheck(UpdateCheckFrequency.Weekly, now.AddDays(-6).AddHours(-23), now));
    }

    [Fact]
    public void ShouldCheck_Weekly_Exactly7d_ReturnsTrue()
    {
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(UpdateService.ShouldCheck(UpdateCheckFrequency.Weekly, now.AddDays(-7), now));
    }

    [Fact]
    public void ShouldCheck_UndefinedFrequency_FallsBackToDaily()
    {
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

        // 枚举越界值（如将来新增档位被旧程序读到）按 Daily 处理，且绝不抛异常
        var undefined = (UpdateCheckFrequency)42;
        Assert.False(UpdateService.ShouldCheck(undefined, now.AddHours(-23).AddMinutes(-59), now));
        Assert.True(UpdateService.ShouldCheck(undefined, now.AddHours(-24), now));
    }

    // ── GitHub API 解析 ──

    [Fact]
    public async Task CheckForUpdateAsync_NewerVersion_ReturnsUpdateInfo()
    {
        HttpMethod? method = null;
        Uri? requestUri = null;
        string? userAgent = null;
        string? accept = null;

        var handler = new StubHandler(request =>
        {
            method = request.Method;
            requestUri = request.RequestUri;
            userAgent = request.Headers.TryGetValues("User-Agent", out var ua) ? ua.FirstOrDefault() : null;
            accept = request.Headers.TryGetValues("Accept", out var acc) ? acc.FirstOrDefault() : null;
            return JsonResponse(BuildReleaseJson("v0.5.5"));
        });

        try
        {
            UpdateService.SetTestHandler(handler);
            var result = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            var update = result.Update.GetValueOrDefault();
            Assert.Equal("0.5.5", update.LatestVersion);
            Assert.Equal("https://github.com/mantis3d/MantisZip/releases/tag/v0.5.5", update.DownloadUrl);
            Assert.Equal(update.DownloadUrl, update.ReleaseNotesUrl);
            Assert.Equal(new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), update.ReleaseDate);
            Assert.Equal(DateTimeKind.Utc, update.ReleaseDate.GetValueOrDefault().Kind);
            Assert.Equal("2026-10-01", update.PublishDateDisplay);
            // 响应里没有 body 字段时发布说明为空串（新增参数必须向后兼容）
            Assert.Equal(string.Empty, update.ReleaseNotes);

            // 请求本身也应是 GitHub 期望的形状（URL / 方法 / 必需请求头）
            Assert.Equal(HttpMethod.Get, method);
            Assert.Equal("https://api.github.com/repos/mantis3d/MantisZip/releases/latest", requestUri?.ToString());
            Assert.Equal("MantisZip/0.5.4", userAgent);
            Assert.Equal("application/vnd.github+json", accept);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_BodyPresent_ReturnsTrimmedMarkdown()
    {
        const string markdown = "## 修复\n\n- 条目一";
        try
        {
            // body 带首尾空白：解析处 Trim，值应与去掉空白后的原文完全一致
            UpdateService.SetTestHandler(
                new StubHandler(_ => JsonResponse(
                    BuildReleaseJson("v0.5.5", bodyJson: JsonStringLiteral($"\n  {markdown}  \n")))));
            var result = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.Equal(markdown, result.Update.GetValueOrDefault().ReleaseNotes);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_BodyMissing_ReturnsEmpty()
    {
        try
        {
            UpdateService.SetTestHandler(
                new StubHandler(_ => JsonResponse(BuildReleaseJson("v0.5.5"))));
            var result = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.Equal(string.Empty, result.Update.GetValueOrDefault().ReleaseNotes);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_BodyNull_ReturnsEmpty()
    {
        try
        {
            UpdateService.SetTestHandler(
                new StubHandler(_ => JsonResponse(BuildReleaseJson("v0.5.5", bodyJson: "null"))));
            var result = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.Equal(string.Empty, result.Update.GetValueOrDefault().ReleaseNotes);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_BodyTooLong_IsTruncatedTo20000()
    {
        var oversized = new string('x', 20050);
        try
        {
            UpdateService.SetTestHandler(
                new StubHandler(_ => JsonResponse(
                    BuildReleaseJson("v0.5.5", bodyJson: JsonStringLiteral(oversized)))));
            var result = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            var notes = result.Update.GetValueOrDefault().ReleaseNotes;
            Assert.Equal(20000, notes.Length);
            Assert.Equal(new string('x', 20000), notes);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_SameVersion_ReturnsUpToDate()
    {
        try
        {
            UpdateService.SetTestHandler(
                new StubHandler(_ => JsonResponse(BuildReleaseJson("v0.5.4"))));
            var result = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_HttpThrows_ReturnsFailed()
    {
        try
        {
            // 模拟网络不可达：客户端内部会把它变成 faulted task，服务必须吞掉
            UpdateService.SetTestHandler(
                new StubHandler(_ => throw new HttpRequestException("模拟网络不可达")));
            var result = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.NotEmpty(result.Reason);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_PreRelease_ReturnsUpToDate()
    {
        try
        {
            UpdateService.SetTestHandler(
                new StubHandler(_ => JsonResponse(BuildReleaseJson("v0.5.5", prerelease: true))));
            var result = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_PatchBump_0_5_10_Beats_0_5_3()
    {
        try
        {
            UpdateService.SetTestHandler(
                new StubHandler(_ => JsonResponse(BuildReleaseJson("v0.5.10"))));
            var result = await UpdateService.CheckForUpdateAsync("0.5.3", cancellationToken: CancellationToken.None);

            // 字符串比较会得出 "0.5.10" < "0.5.3"，这里必须按数值逐段比较
            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.Equal("0.5.10", result.Update.GetValueOrDefault().LatestVersion);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    // ── 失败路径（KI-008：必须与「已是最新」区分开）──

    [Fact]
    public async Task CheckForUpdateAsync_HttpServerError_ReturnsFailed()
    {
        try
        {
            // 5xx 是服务端故障，绝不能当成「已是最新」——那正是 KI-008 的核心误导
            UpdateService.SetTestHandler(
                new StubHandler(_ => JsonResponse("{}", HttpStatusCode.InternalServerError)));
            var result = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.Contains("500", result.Reason, StringComparison.Ordinal);
            Assert.Null(result.Update);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_HttpNotFound_ReturnsUpToDate()
    {
        try
        {
            // 404 = 仓库还没有任何正式发布，语义上是「没有可更新的版本」，不是失败
            UpdateService.SetTestHandler(
                new StubHandler(_ => JsonResponse("{}", HttpStatusCode.NotFound)));
            var result = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
            Assert.Null(result.Update);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_MalformedJson_ReturnsFailed()
    {
        try
        {
            UpdateService.SetTestHandler(new StubHandler(_ => JsonResponse("not-json")));
            var result = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.NotEmpty(result.Reason);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_MissingTagName_ReturnsFailed()
    {
        try
        {
            UpdateService.SetTestHandler(new StubHandler(_ => JsonResponse("{\"prerelease\": false}")));
            var result = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.Contains("tag_name", result.Reason, StringComparison.Ordinal);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_Cancelled_ReturnsFailed()
    {
        try
        {
            UpdateService.SetTestHandler(
                new StubHandler(_ => throw new OperationCanceledException("模拟用户取消")));
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var result = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: cts.Token);

            Assert.Equal(UpdateCheckStatus.Failed, result.Status);
            Assert.Contains("取消", result.Reason, StringComparison.Ordinal);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    [Fact]
    public void UpdateCheckResult_Failed_RequiresNonEmptyReason()
    {
        // 造不出「失败没原因」：空原因会让「为什么没查到更新」重新变成黑箱
        Assert.Throws<ArgumentException>(() => UpdateCheckResult.Failed(""));
        Assert.Throws<ArgumentException>(() => UpdateCheckResult.Failed("   "));
        Assert.Equal("boom", UpdateCheckResult.Failed("boom").Reason);
    }

    [Fact]
    public void UpdateCheckResult_Factories_KeepStatusInvariants()
    {
        Assert.True(UpdateCheckResult.UpToDate().IsUpToDate);
        Assert.Null(UpdateCheckResult.UpToDate().Update);

        var info = new UpdateInfo("1.0.0", "u", "u", null);
        var available = UpdateCheckResult.Available(info);
        Assert.True(available.IsUpdateAvailable);
        Assert.Equal("1.0.0", available.Update.GetValueOrDefault().LatestVersion);
        Assert.Equal(string.Empty, available.Reason);

        Assert.True(UpdateCheckResult.Failed("x").IsFailed);
    }

    // ── 测试替身与数据构造 ──

    /// <summary>
    /// 返回固定响应的 HTTP 桩（不联网、不依赖 Moq）。
    /// </summary>
    private sealed class StubHandler : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
            // DelegatingHandler 若不挂 InnerHandler，默认实现会抛 InvalidOperationException；
            // 本桩完全重写 SendAsync、不调用 base，因此这个 handler 永远不会真的发包。
            InnerHandler = new HttpClientHandler();
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    /// <summary>构造一个 HTTP 200 + JSON 内容的响应。</summary>
    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    /// <summary>把文本编码成 JSON 字符串字面量（只处理测试用到的转义）。</summary>
    private static string JsonStringLiteral(string value)
        => "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// 构造贴近 GitHub <c>releases/latest</c> 真实形状的 JSON（只保留服务读取的字段）。
    /// </summary>
    /// <param name="bodyJson">
    /// <c>body</c> 字段的 JSON 片段：<c>null</c> = 整个字段缺失；<c>"null"</c> = 字段为 JSON null；
    /// 字符串字面量 = 真实正文。
    /// </param>
    private static string BuildReleaseJson(
        string tagName,
        bool prerelease = false,
        string? publishedAt = "2026-10-01T08:00:00Z",
        string? bodyJson = null)
    {
        var publishedValue = publishedAt is null ? "null" : $"\"{publishedAt}\"";
        var bodyLine = bodyJson is null ? string.Empty : $"\n  \"body\": {bodyJson},";
        return $$"""
            {
              "url": "https://api.github.com/repos/mantis3d/MantisZip/releases/1",
              "html_url": "https://github.com/mantis3d/MantisZip/releases/tag/{{tagName}}",
              "tag_name": "{{tagName}}",
              "name": "MantisZip {{tagName}}",
              "draft": false,
              "prerelease": {{(prerelease ? "true" : "false")}},{{bodyLine}}
              "created_at": "2026-10-01T07:00:00Z",
              "published_at": {{publishedValue}}
            }
            """;
    }
}
