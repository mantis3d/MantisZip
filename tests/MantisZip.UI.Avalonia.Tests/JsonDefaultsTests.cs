using System.Text.Json;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// <see cref="JsonDefaults.CaseInsensitive"/> 回归测试。
///
/// 背景（2026-10-28 事故）：<c>Resources/languages.json</c> 的键名是全小写
/// （<c>name</c> / <c>translator</c>），目标属性是 PascalCase，而 <c>System.Text.Json</c>
/// 默认区分大小写——反序列化「成功」但字段全是空串，UI 上「翻译贡献者」一行恒为空，
/// 构建与 CI 全绿。本测试用最小复现锁住这条静默失败面。
/// </summary>
public class JsonDefaultsTests
{
    /// <summary>事故里的最小输入：小写键名，值不为空。</summary>
    private const string LowercaseJson = """{"name":"x","translator":"y"}""";

    [Fact]
    public void CaseInsensitive_LowercaseKeys_BindPascalCaseProperties()
    {
        var metadata = JsonSerializer.Deserialize<Metadata>(LowercaseJson, JsonDefaults.CaseInsensitive);

        Assert.NotNull(metadata);
        Assert.Equal("x", metadata!.Name);
        Assert.Equal("y", metadata.Translator);
    }

    [Fact]
    public void BareOptions_LowercaseKeys_ProduceEmptyFields()
    {
        // 对照断言：不传选项（= 默认区分大小写）时反序列化不抛异常，字段却是空串。
        // 这条锁的是「为什么必须显式传 options」——删掉实参不会有任何编译/运行错误提示。
        var metadata = JsonSerializer.Deserialize<Metadata>(LowercaseJson, new JsonSerializerOptions());

        Assert.NotNull(metadata);
        Assert.Equal(string.Empty, metadata!.Name);
        Assert.Equal(string.Empty, metadata.Translator);
    }

    [Fact]
    public void CaseInsensitive_HasPropertyNameCaseInsensitiveEnabled()
    {
        Assert.True(JsonDefaults.CaseInsensitive.PropertyNameCaseInsensitive);
    }

    /// <summary>与 <c>LocalizationManager.LanguageMetadata</c> 同形：PascalCase 属性、无 JSON 特性。</summary>
    private sealed class Metadata
    {
        public string Name { get; set; } = string.Empty;

        public string Translator { get; set; } = string.Empty;
    }
}
