using System;
using System.IO;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// FileNameTemplate —— 占位符渲染与校验。
/// 覆盖：每个占位符、大小写不敏感、转义、未知/未闭合、结果类校验（空/非法字符/结尾点空格/保留设备名/超长）。
/// </summary>
public class FileNameTemplateTests
{
    private static readonly DateTime Stamp = new(2026, 11, 6, 15, 30, 24);

    private static FileNameTemplateContext Ctx(
        string sourcePath = @"D:\工作\报告\a.txt",
        int sequence = 1,
        string? autoName = null)
        => new(sourcePath, sequence, Stamp, autoName);

    private static string Render(string template, FileNameTemplateContext? ctx = null)
    {
        Assert.True(
            FileNameTemplate.TryRender(template, ctx ?? Ctx(), out var baseName, out var error),
            $"渲染失败：{error}");
        Assert.Null(error);
        return baseName!;
    }

    // ════════════════════════════════════════════════════════════════
    //  未启用（模板为空）
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void TryRender_EmptyTemplate_ReturnsFalseWithoutError(string? template)
    {
        Assert.False(FileNameTemplate.TryRender(template, Ctx(), out var baseName, out var error));
        Assert.Null(baseName);
        Assert.Null(error); // 空模板 = 未启用，不是错误
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryValidateSyntax_EmptyTemplate_IsValid(string? template)
    {
        Assert.True(FileNameTemplate.TryValidateSyntax(template, out var error));
        Assert.Null(error);
    }

    // ════════════════════════════════════════════════════════════════
    //  各占位符
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Render_Name_FileWithoutExtension()
        => Assert.Equal("a", Render("{name}"));

    [Fact]
    public void Render_Name_Directory_UsesDirectoryName()
        => Assert.Equal("报告", Render("{name}", Ctx(@"D:\工作\报告\")));

    [Fact]
    public void Render_Name_MultiDotFileName_OnlyLastExtensionDropped()
        => Assert.Equal("archive.tar", Render("{name}", Ctx(@"D:\x\archive.tar.gz")));

    [Fact]
    public void Render_Ext_FileWithExtension_IncludesDot()
        => Assert.Equal(".txt", Render("{ext}"));

    [Fact]
    public void Render_Ext_TarGz_OnlyLastSegment()
        => Assert.Equal(".gz", Render("{ext}", Ctx(@"D:\x\archive.tar.gz")));

    [Fact]
    public void Render_Ext_Directory_IsEmpty()
    {
        // {ext} 为空 ⇒ "x" 而不是 "x."
        Assert.Equal("x", Render("x{ext}", Ctx(@"D:\工作\报告\")));
    }

    [Fact]
    public void Render_Parent_ReturnsParentDirectoryName()
        => Assert.Equal("报告", Render("{parent}"));

    [Fact]
    public void Render_Parent_AtDriveRoot_IsEmpty()
        => Assert.Equal("x", Render("x{parent}", Ctx(@"C:\file.txt")));

    [Fact]
    public void Render_Parent_OfDirectorySource_UsesGrandparent()
        => Assert.Equal("工作", Render("{parent}", Ctx(@"D:\工作\报告\")));

    [Fact]
    public void Render_Date_IsIsoDate()
        => Assert.Equal("2026-11-06", Render("{date}"));

    [Fact]
    public void Render_DateTime_IsIsoDateWithUnderscoreTime()
        => Assert.Equal("2026-11-06_153024", Render("{datetime}"));

    [Fact]
    public void Render_Seq_DefaultsToThreeDigits()
        => Assert.Equal("001", Render("{seq}"));

    [Fact]
    public void Render_Seq_LargeNumberKeepsAllDigits()
        => Assert.Equal("1234", Render("{seq}", Ctx(sequence: 1234)));

    [Fact]
    public void Render_Seq_CustomWidth()
        => Assert.Equal("0007", Render("{seq:0000}", Ctx(sequence: 7)));

    [Fact]
    public void Render_Seq_SingleDigitWidth()
        => Assert.Equal("7", Render("{seq:0}", Ctx(sequence: 7)));

    [Fact]
    public void Render_NameAuto_WithAutoName_UsesIt()
        => Assert.Equal("项目A", Render("{name:auto}", Ctx(autoName: "项目A")));

    [Fact]
    public void Render_NameAuto_WithoutAutoName_FallsBackToName()
        => Assert.Equal("a", Render("{name:auto}"));

    [Fact]
    public void Render_NameAuto_EmptyAutoName_FallsBackToName()
        => Assert.Equal("a", Render("{name:auto}", Ctx(autoName: "")));

    [Fact]
    public void Render_CombinedPlaceholders()
        => Assert.Equal(
            "报告-a_2026-11-06_001.txt",
            Render("{parent}-{name}_{date}_{seq}{ext}"));

    [Fact]
    public void Render_SeparatorInTemplate_IsRejectedBecauseResultIsBaseName()
    {
        // 模板渲染的是「主名」，不是路径 ⇒ 写 {parent}\{name} 会被判非法字符（不猜用户想建子目录）
        Assert.False(FileNameTemplate.TryRender(
            @"{parent}\{name}", Ctx(), out _, out var error));
        Assert.Equal(FileNameTemplateErrorKind.InvalidChar, error!.Kind);
    }

    // ════════════════════════════════════════════════════════════════
    //  大小写与转义
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("{NAME}")]
    [InlineData("{Name}")]
    [InlineData("{nAmE}")]
    public void Render_PlaceholderIsCaseInsensitive(string template)
        => Assert.Equal("a", Render(template));

    [Fact]
    public void Render_UpperCaseSeqWithWidth_Works()
        => Assert.Equal("0007", Render("{SEQ:0000}", Ctx(sequence: 7)));

    [Fact]
    public void Render_DoubleBraceEscapesToLiteralBrace()
        => Assert.Equal("{name}", Render("{{name}}"));

    [Fact]
    public void Render_LoneClosingBrace_IsLiteral()
        => Assert.Equal("a}", Render("{name}}"));

    [Fact]
    public void Render_BothEscapes()
        => Assert.Equal("{a}", Render("{{{name}}}"));

    // ════════════════════════════════════════════════════════════════
    //  语法错误
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("{foo}", "foo")]
    [InlineData("{seq:abc}", "seq:abc")]
    [InlineData("{name:xxx}", "name:xxx")]
    [InlineData("{seq:}", "seq:")]
    [InlineData("{seq:1234567890}", "seq:1234567890")]
    public void TryRender_UnknownPlaceholder_ReportsToken(string template, string token)
    {
        Assert.False(FileNameTemplate.TryRender(template, Ctx(), out var baseName, out var error));
        Assert.Null(baseName);
        Assert.NotNull(error);
        Assert.Equal(FileNameTemplateErrorKind.UnknownPlaceholder, error!.Kind);
        Assert.Equal(token, error.Token);
    }

    [Theory]
    [InlineData("report_{name")]
    [InlineData("{")]
    [InlineData("{a{b}")]
    public void TryRender_Unterminated_ReportsError(string template)
    {
        Assert.False(FileNameTemplate.TryRender(template, Ctx(), out var baseName, out var error));
        Assert.Null(baseName);
        Assert.NotNull(error);
        Assert.Equal(FileNameTemplateErrorKind.Unterminated, error!.Kind);
    }

    [Fact]
    public void TryValidateSyntax_ValidTemplate_ReturnsTrue()
        => Assert.True(FileNameTemplate.TryValidateSyntax("{name}_{date}_{seq:0000}", out var error));

    [Fact]
    public void TryValidateSyntax_UnknownPlaceholder_ReturnsFalse()
    {
        Assert.False(FileNameTemplate.TryValidateSyntax("{foo}", out var error));
        Assert.Equal(FileNameTemplateErrorKind.UnknownPlaceholder, error!.Kind);
    }

    [Fact]
    public void TryValidateSyntax_DoesNotRender()
    {
        // 语法合法但结果非法（保留设备名）⇒ 语法校验仍应通过
        Assert.True(FileNameTemplate.TryValidateSyntax("CON", out _));
    }

    // ════════════════════════════════════════════════════════════════
    //  结果校验
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Render_EmptyResult_WhenSourceNameIsEmpty()
    {
        // 源是盘符根 ⇒ {name} 为空 ⇒ 结果空
        Assert.False(FileNameTemplate.TryRender("{name}", Ctx(@"C:\"), out var baseName, out var error));
        Assert.Null(baseName);
        Assert.Equal(FileNameTemplateErrorKind.EmptyResult, error!.Kind);
    }

    [Theory]
    [InlineData(@"a/b")]
    [InlineData(@"a\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    public void Render_InvalidChar_IsRejected(string template)
    {
        Assert.False(FileNameTemplate.TryRender(template, Ctx(), out _, out var error));
        Assert.Equal(FileNameTemplateErrorKind.InvalidChar, error!.Kind);
    }

    [Theory]
    [InlineData("abc.")]
    [InlineData("abc ")]
    public void Render_TrailingDotOrSpace_IsInvalidName(string template)
    {
        Assert.False(FileNameTemplate.TryRender(template, Ctx(), out _, out var error));
        Assert.Equal(FileNameTemplateErrorKind.InvalidName, error!.Kind);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("CON.txt")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("com9.dat")]
    [InlineData("LPT1")]
    public void Render_ReservedDeviceName_IsInvalidName(string template)
    {
        Assert.False(FileNameTemplate.TryRender(template, Ctx(), out _, out var error));
        Assert.Equal(FileNameTemplateErrorKind.InvalidName, error!.Kind);
    }

    [Theory]
    [InlineData("CONSOLE")]   // 前缀命中但不是设备名
    [InlineData("COM0")]
    [InlineData("COM10")]
    [InlineData("LPT0")]
    public void Render_NotReservedDeviceName_IsAccepted(string template)
    {
        Assert.True(FileNameTemplate.TryRender(template, Ctx(), out var baseName, out _));
        Assert.Equal(template, baseName);
    }

    [Fact]
    public void Render_TooLong_IsRejected()
    {
        var template = new string('a', FileNameTemplate.MaxBaseNameLength + 1);
        Assert.False(FileNameTemplate.TryRender(template, Ctx(), out _, out var error));
        Assert.Equal(FileNameTemplateErrorKind.TooLong, error!.Kind);
    }

    [Fact]
    public void Render_ExactlyMaxLength_IsAccepted()
    {
        var template = new string('a', FileNameTemplate.MaxBaseNameLength);
        Assert.True(FileNameTemplate.TryRender(template, Ctx(), out var baseName, out _));
        Assert.Equal(FileNameTemplate.MaxBaseNameLength, baseName!.Length);
    }

    [Fact]
    public void Render_UnicodeChinese_IsAccepted()
        => Assert.Equal("报告_2026-11-06", Render("{parent}_{date}"));

    // ════════════════════════════════════════════════════════════════
    //  常量与清单
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Placeholders_MatchesSupportedSet()
    {
        // 帮助文档的占位符表以此为准，新增占位符必须同步
        Assert.Equal(8, FileNameTemplate.Placeholders.Count);
        Assert.Contains("{name}", FileNameTemplate.Placeholders);
        Assert.Contains("{name:auto}", FileNameTemplate.Placeholders);
        Assert.Contains("{seq:0000}", FileNameTemplate.Placeholders);
    }

    [Fact]
    public void DefaultExample_Renders()
        => Assert.True(FileNameTemplate.TryRender(
            FileNameTemplate.DefaultExample, Ctx(), out var baseName, out _)
            && !string.IsNullOrWhiteSpace(baseName));

    [Fact]
    public void Preset_ToString_ReturnsName()
    {
        var preset = new FileNameTemplatePreset("带日期", "{name}_{date}");
        Assert.Equal("带日期", preset.ToString());
        Assert.Equal("{name}_{date}", preset.Template);
    }
}
