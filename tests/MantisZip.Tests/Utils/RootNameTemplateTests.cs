using System;
using System.IO;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// RootNameTemplate —— 「包内根目录」加根时的根目录名占位符渲染。
/// 覆盖：5 个占位符、大小写不敏感、转义、未知/未闭合、结果类校验、
/// 多源取公共父目录名、单源取主名、与 FileNameTemplate 共用的词法一致性。
/// </summary>
public class RootNameTemplateTests
{
    private static readonly DateTime Stamp = new(2026, 11, 17, 20, 45, 30);

    private static RootNameTemplateContext Ctx(
        string[]? sourcePaths = null,
        string? archiveName = null)
        => new(sourcePaths ?? new[] { @"D:\dl\7zip" }, Stamp, archiveName);

    private static string Render(string template, RootNameTemplateContext? ctx = null)
    {
        Assert.True(
            RootNameTemplate.TryRender(template, ctx ?? Ctx(), out var rootName, out var error),
            $"渲染失败：{error}");
        Assert.Null(error);
        return rootName!;
    }

    // ════════════════════════════════════════════════════════════════
    //  未启用（模板为空）
    // ════════════════════════════════════════��═══════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void TryRender_EmptyTemplate_ReturnsFalseWithoutError(string? template)
    {
        Assert.False(RootNameTemplate.TryRender(template, Ctx(), out var rootName, out var error));
        Assert.Null(rootName);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TryValidateSyntax_EmptyTemplate_IsNotAnError(string? template)
    {
        Assert.True(RootNameTemplate.TryValidateSyntax(template, out var error));
        Assert.Null(error);
    }

    // ════════════════════════════════════════════════════════════════
    //  占位符清单
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Placeholders_HasExactlyFiveTokens()
    {
        Assert.Equal(5, RootNameTemplate.Placeholders.Count);
        Assert.Contains("{name}", RootNameTemplate.Placeholders);
        Assert.Contains("{parent}", RootNameTemplate.Placeholders);
        Assert.Contains("{date}", RootNameTemplate.Placeholders);
        Assert.Contains("{datetime}", RootNameTemplate.Placeholders);
        Assert.Contains("{archive}", RootNameTemplate.Placeholders);
    }

    [Fact]
    public void Placeholders_ExcludeFileOnlyTokens()
    {
        // 一层目录名没有扩展名，也谈不上序号 —— 列出它们只会诱导用户写出必错的模板
        Assert.DoesNotContain("{ext}", RootNameTemplate.Placeholders);
        Assert.DoesNotContain("{seq}", RootNameTemplate.Placeholders);
        Assert.DoesNotContain("{seq:0000}", RootNameTemplate.Placeholders);
        Assert.DoesNotContain("{name:auto}", RootNameTemplate.Placeholders);
    }

    [Fact]
    public void Placeholders_AllRenderWithoutError()
    {
        foreach (var token in RootNameTemplate.Placeholders)
        {
            Assert.True(
                RootNameTemplate.TryRender(token, Ctx(archiveName: "backup"), out _, out var error),
                $"占位符 {token} 渲染失败：{error}");
        }
    }

    [Fact]
    public void DefaultExample_IsRenderable()
    {
        Assert.True(RootNameTemplate.TryRender(RootNameTemplate.DefaultExample, Ctx(), out var rootName, out var error));
        Assert.Null(error);
        Assert.NotNull(rootName);
    }

    // ════════════════════════════════════════════════════════════════
    //  {name}
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Name_SingleSource_UsesSourceBaseName()
    {
        // 目录看起来像目录（磁盘上真的存在时）才按「目录名」取；
        // 这里是纯字符串语义，`archive.tar.gz` 只掉一层扩展名（与 {ext} 的取法一致）
        Assert.Equal("7zip", Render("{name}", Ctx(new[] { @"D:\dl\7zip" })));
        Assert.Equal("report", Render("{name}", Ctx(new[] { @"D:\dl\report.txt" })));
        Assert.Equal("archive.tar", Render("{name}", Ctx(new[] { @"D:\dl\archive.tar.gz" })));
    }

    [Fact]
    public void Name_MultipleSources_UsesCommonParentName()
    {
        var paths = new[] { @"D:\dl\photos\a.jpg", @"D:\dl\photos\b.jpg" };
        // 多源取公共父目录名，不随「先选谁」漂移
        Assert.Equal("photos", Render("{name}", Ctx(paths)));
    }

    [Fact]
    public void Name_MultipleSourcesWithoutCommonParent_FallsBackToFirstSource()
    {
        var paths = new[] { @"D:\dl\a.txt", @"E:\other\b.txt" };
        Assert.Equal("a", Render("{name}", Ctx(paths)));
    }

    [Fact]
    public void Name_NoSource_RendersEmptyAndIsRejected()
    {
        // 一个没有源的上下文只可能出现在极端兜底路径；渲染成空 ⇒ 结果校验必须拦下
        Assert.False(RootNameTemplate.TryRender("{name}", Ctx(Array.Empty<string>()), out var rootName, out var error));
        Assert.Null(rootName);
        Assert.NotNull(error);
        Assert.Equal(FileNameTemplateErrorKind.EmptyResult, error!.Kind);
    }

    // ════════════════════════════════════════════════════════════════
    //  {parent} / {archive} / 时间
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Parent_IsSourceParentFolderName()
    {
        // 与 FileNameTemplate 同口径：取**紧邻的上一层**，不是更外层的祖先
        Assert.Equal("dl", Render("{parent}", Ctx(new[] { @"D:\dl\7zip" })));
        Assert.Equal("报告", Render("{parent}", Ctx(new[] { @"D:\工作\报告\a.txt" })));
    }

    [Fact]
    public void Parent_OfDriveRoot_IsEmptyAndRejected()
    {
        Assert.False(
            RootNameTemplate.TryRender("{parent}", Ctx(new[] { @"D:\" }), out _, out var error));
        Assert.Equal(FileNameTemplateErrorKind.EmptyResult, error!.Kind);
    }

    [Fact]
    public void Archive_UsesProvidedBaseNameAndIsEmptyWhenMissing()
    {
        Assert.Equal("backup", Render("{archive}", Ctx(archiveName: "backup")));
        Assert.Equal("backup_2026-11-17", Render("{archive}_{date}", Ctx(archiveName: "backup")));
        Assert.False(
            RootNameTemplate.TryRender("{archive}", Ctx(), out _, out var error));
        Assert.Equal(FileNameTemplateErrorKind.EmptyResult, error!.Kind);
    }

    [Fact]
    public void DateAndDateTime_UseSharedBatchTimestamp()
    {
        Assert.Equal("2026-11-17", Render("{date}"));
        Assert.Equal("2026-11-17_204530", Render("{datetime}"));
        // 与文件名模板同一口径：日期不带分隔符的写法已被 v0.5.7 淘汰
        Assert.Equal("7zip_2026-11-17", Render("{name}_{date}"));
    }

    // ════════════════════════════════════════════════════════════════
    //  大小写 / 转义 / 语法错误
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("{NAME}", "7zip")]
    [InlineData("{Name}", "7zip")]
    [InlineData("{nAmE}", "7zip")]
    [InlineData("{ARCHIVE}", "backup")]
    public void Token_CaseInsensitive(string template, string expected)
    {
        Assert.Equal(expected, Render(template, Ctx(archiveName: "backup")));
    }

    [Fact]
    public void Braces_AreEscapable()
    {
        Assert.Equal("{name}", Render("{{name}}"));
        Assert.Equal("7zip}", Render("{name}}"));
        Assert.Equal("{7zip}", Render("{{{name}}}"));
    }

    [Theory]
    [InlineData("{foo}")]
    [InlineData("{ext}")]
    [InlineData("{seq}")]
    [InlineData("{seq:0000}")]
    [InlineData("{name:auto}")]
    [InlineData("root_{name:xxx}")]
    public void UnknownPlaceholder_IsRejectedWithToken(string template)
    {
        Assert.False(RootNameTemplate.TryRender(template, Ctx(), out _, out var error));
        Assert.NotNull(error);
        Assert.Equal(FileNameTemplateErrorKind.UnknownPlaceholder, error!.Kind);
        Assert.NotNull(error.Token);
    }

    [Theory]
    [InlineData("root_{name")]
    [InlineData("{")]
    [InlineData("{a{b}")]
    public void Unterminated_IsRejected(string template)
    {
        Assert.False(RootNameTemplate.TryRender(template, Ctx(), out _, out var error));
        Assert.Equal(FileNameTemplateErrorKind.Unterminated, error!.Kind);
    }

    [Fact]
    public void TryValidateSyntax_ReportsSameSyntaxErrorsAsRender()
    {
        Assert.False(RootNameTemplate.TryValidateSyntax("{foo}", out var error));
        Assert.Equal(FileNameTemplateErrorKind.UnknownPlaceholder, error!.Kind);
        Assert.False(RootNameTemplate.TryValidateSyntax("root_{name", out error));
        Assert.Equal(FileNameTemplateErrorKind.Unterminated, error!.Kind);
        Assert.True(RootNameTemplate.TryValidateSyntax("{name}_{date}", out error));
        Assert.Null(error);
    }

    // ════════════════════════════════════════════════════════════════
    //  结果校验：单段目录名
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    [InlineData("a\"b")]
    public void Result_WithPathSeparatorOrWildcard_IsRejected(string template)
    {
        // 根目录名必须是单段：含 / 或 \ 一律拒绝，不猜测用户想建多级目录
        Assert.False(RootNameTemplate.TryRender(template, Ctx(), out _, out var error));
        Assert.Equal(FileNameTemplateErrorKind.InvalidChar, error!.Kind);
    }

    [Theory]
    [InlineData("abc.")]
    [InlineData("abc ")]
    public void Result_TrailingDotOrSpace_IsRejected(string template)
    {
        Assert.False(RootNameTemplate.TryRender(template, Ctx(), out _, out var error));
        Assert.Equal(FileNameTemplateErrorKind.InvalidName, error!.Kind);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9.dat")]
    public void Result_ReservedDeviceName_IsRejected(string template)
    {
        Assert.False(RootNameTemplate.TryRender(template, Ctx(), out _, out var error));
        Assert.Equal(FileNameTemplateErrorKind.InvalidName, error!.Kind);
    }

    [Theory]
    [InlineData("CONSOLE")]
    [InlineData("COM0")]
    [InlineData("COM10")]
    [InlineData("LPT0")]
    public void Result_NonReservedName_IsAccepted(string template)
    {
        Assert.True(RootNameTemplate.TryRender(template, Ctx(), out _, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void Result_LongerThanLimit_IsRejected()
    {
        var template = new string('a', RootNameTemplate.MaxNameLength + 1);
        Assert.False(RootNameTemplate.TryRender(template, Ctx(), out _, out var error));
        Assert.Equal(FileNameTemplateErrorKind.TooLong, error!.Kind);
    }

    [Fact]
    public void Result_ExactlyAtLimit_IsAccepted()
    {
        var template = new string('a', RootNameTemplate.MaxNameLength);
        Assert.True(RootNameTemplate.TryRender(template, Ctx(), out var rootName, out var error));
        Assert.Null(error);
        Assert.Equal(RootNameTemplate.MaxNameLength, rootName!.Length);
    }

    [Fact]
    public void Result_NonAsciiNames_AreAccepted()
    {
        Assert.Equal("照片_2026-11-17", Render("照片_{date}"));
    }

    // ════════════════════════════════════════════════════════════════
    //  与 FileNameTemplate 的词法一致性（共用 TemplateLexing）
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedLexing_SameSyntaxRulesAsFileNameTemplate()
    {
        // 两套模板共用 TemplateLexing；一旦有人偷偷复制一份，这里会先红
        Assert.False(FileNameTemplate.TryRender("{foo}", FileNameCtx(), out _, out var fileNameError));
        Assert.False(RootNameTemplate.TryRender("{foo}", Ctx(), out _, out var rootError));
        Assert.Equal(fileNameError!.Kind, rootError!.Kind);

        Assert.False(FileNameTemplate.TryRender("x_{name", FileNameCtx(), out _, out fileNameError));
        Assert.False(RootNameTemplate.TryRender("x_{name", Ctx(), out _, out rootError));
        Assert.NotNull(fileNameError);
        Assert.NotNull(rootError);
        Assert.Equal(fileNameError!.Kind, rootError!.Kind);

        Assert.Equal(FileNameTemplate.MaxBaseNameLength, RootNameTemplate.MaxNameLength);
    }

    private static FileNameTemplateContext FileNameCtx()
        => new(@"D:\dl\report.txt", 1, Stamp);
}
