using System;
using System.IO;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 字体预览样本文本的换行归一（2026-11-25）。
///
/// 背景：用户手动把 <c>FontPreviewSampleText</c> 追加了一行连字样本，写进去的是 CRLF；
/// 折行只按 <c>'\n'</c> 切，行尾残留的 <c>'\r'</c> 会被画出来 ——
/// 字形表里没有 U+000D 的字体（Arial / 微软雅黑实测）会多画一个方块。
/// </summary>
public class FontSampleTextNewlineTests
{
    [Theory]
    [InlineData("abc\r\ndef", "abc\ndef")]
    [InlineData("abc\rdef", "abc\ndef")]
    [InlineData("abc\ndef", "abc\ndef")]
    [InlineData("abc\r\n\r\ndef", "abc\n\ndef")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeFontSampleText_CollapsesEveryLineEndingToLf(string? input, string expected)
    {
        Assert.Equal(expected, PreviewViewModel.NormalizeFontSampleText(input));
    }

    [Fact]
    public void NormalizeFontSampleText_LeavesTextWithoutCarriageReturnUntouched()
    {
        // 纯 LF 的文本不能被「归一」出别的变化（含中文与 emoji）
        const string text = "The quick brown fox jumps over the lazy dog\n0123456789\n天地玄黄 🔒";

        Assert.Same(text, PreviewViewModel.NormalizeFontSampleText(text));
    }

    [Fact]
    public void FactoryDefaultSampleText_HasNoCarriageReturn()
    {
        // 出厂快照里那行连字样本必须是纯 LF —— 写 CRLF 就会在部分字体下多画方块
        using var sandbox = new AppSettingsTestSandbox();
        var settings = AppSettings.Load();

        Assert.DoesNotContain('\r', settings.FontPreviewSampleText);
        // 连字样本（>= <= <> !=）确实在出厂默认里，否则本用例守的是一条空规则
        Assert.Contains(">=", settings.FontPreviewSampleText);
    }
}
