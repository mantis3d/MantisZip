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
    // ── 24 小时节流 ──

    [Fact]
    public void ShouldCheck_Within24Hours_ReturnsFalse()
    {
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(UpdateService.ShouldCheck(now.AddHours(-23), now));
    }

    [Fact]
    public void ShouldCheck_Over24Hours_ReturnsTrue()
    {
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(UpdateService.ShouldCheck(now.AddHours(-25), now));
    }

    [Fact]
    public void ShouldCheck_NeverChecked_ReturnsTrue()
    {
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(UpdateService.ShouldCheck(null, now));
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
            var info = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.True(info.HasValue);
            var update = info.GetValueOrDefault();
            Assert.Equal("0.5.5", update.LatestVersion);
            Assert.Equal("https://github.com/mantis3d/MantisZip/releases/tag/v0.5.5", update.DownloadUrl);
            Assert.Equal(update.DownloadUrl, update.ReleaseNotesUrl);
            Assert.Equal(new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), update.ReleaseDate);
            Assert.Equal(DateTimeKind.Utc, update.ReleaseDate.GetValueOrDefault().Kind);
            Assert.Equal("2026-10-01", update.PublishDateDisplay);

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
    public async Task CheckForUpdateAsync_SameVersion_ReturnsNull()
    {
        try
        {
            UpdateService.SetTestHandler(
                new StubHandler(_ => JsonResponse(BuildReleaseJson("v0.5.4"))));
            var info = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.False(info.HasValue);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_HttpThrows_ReturnsNull()
    {
        try
        {
            // 模拟网络不可达：客户端内部会把它变成 faulted task，服务必须吞掉
            UpdateService.SetTestHandler(
                new StubHandler(_ => throw new HttpRequestException("模拟网络不可达")));
            var info = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.False(info.HasValue);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_PreRelease_ReturnsNull()
    {
        try
        {
            UpdateService.SetTestHandler(
                new StubHandler(_ => JsonResponse(BuildReleaseJson("v0.5.5", prerelease: true))));
            var info = await UpdateService.CheckForUpdateAsync("0.5.4", cancellationToken: CancellationToken.None);

            Assert.False(info.HasValue);
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
            var info = await UpdateService.CheckForUpdateAsync("0.5.3", cancellationToken: CancellationToken.None);

            // 字符串比较会得出 "0.5.10" < "0.5.3"，这里必须按数值逐段比较
            Assert.True(info.HasValue);
            Assert.Equal("0.5.10", info.GetValueOrDefault().LatestVersion);
        }
        finally
        {
            UpdateService.SetTestHandler(null);
        }
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

    /// <summary>
    /// 构造贴近 GitHub <c>releases/latest</c> 真实形状的 JSON（只保留服务读取的字段）。
    /// </summary>
    private static string BuildReleaseJson(
        string tagName,
        bool prerelease = false,
        string? publishedAt = "2026-10-01T08:00:00Z")
    {
        var publishedValue = publishedAt is null ? "null" : $"\"{publishedAt}\"";
        return $$"""
            {
              "url": "https://api.github.com/repos/mantis3d/MantisZip/releases/1",
              "html_url": "https://github.com/mantis3d/MantisZip/releases/tag/{{tagName}}",
              "tag_name": "{{tagName}}",
              "name": "MantisZip {{tagName}}",
              "draft": false,
              "prerelease": {{(prerelease ? "true" : "false")}},
              "created_at": "2026-10-01T07:00:00Z",
              "published_at": {{publishedValue}}
            }
            """;
    }
}
