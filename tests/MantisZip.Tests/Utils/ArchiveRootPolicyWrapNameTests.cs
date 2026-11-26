using System;
using System.Collections.Generic;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// ArchiveRootPolicy.ResolveWrapName —— 根目录名「字面名 / 模板」两条路径的分派。
/// 关键不变量：含花括号才算模板；不含花括号的老配置仍走「净化非法字符」旧语义，
/// 否则升级后老用户的自定义根名会静默改名（甚至变红框）。
/// </summary>
public class ArchiveRootPolicyWrapNameTests
{
    private static readonly DateTime Stamp = new(2026, 11, 17, 20, 45, 30);

    // ════════════════════════════════════════════════════════════════
    //  未启用 / 留空 → 回落压缩包文件名（与引入模板前一致）
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespace_FallsBackToArchiveName(string? name)
    {
        Assert.Equal("archive", ArchiveRootPolicy.ResolveWrapName(name, null, @"D:\out\archive.7z", Stamp));
    }

    [Fact]
    public void EmptyName_HandlesDoubleExtensionLikeGetWrapName()
    {
        Assert.Equal("archive", ArchiveRootPolicy.ResolveWrapName("", null, @"D:\out\archive.tar.gz", Stamp));
    }

    // ════════════════════════════════════════════════════════════════
    //  字面名（不含花括号）→ 旧语义：净化而非报错
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void LiteralName_IsUsedAsIs()
    {
        Assert.Equal("myroot", ArchiveRootPolicy.ResolveWrapName("myroot", null, @"D:\out\archive.7z", Stamp));
    }

    [Fact]
    public void LiteralName_IsSanitizedNotRejected()
    {
        // 老配置里写 `a:b` 的用户升级后必须仍然成功，只是字符被替换成 `_`
        Assert.Equal("a_b", ArchiveRootPolicy.ResolveWrapName("a:b", null, @"D:\out\archive.7z", Stamp));
        Assert.Equal("a_b", ArchiveRootPolicy.ResolveWrapName("a/b", null, @"D:\out\archive.7z", Stamp));
        Assert.Equal("a_b_c", ArchiveRootPolicy.ResolveWrapName(@"a\b:c", null, null, Stamp));
    }

    [Fact]
    public void LiteralName_ThatLooksLikeTemplateWithoutBraces_StaysLiteral()
    {
        // 只有花括号才进入模板路径；`_date` 这种纯文本必须原样保留
        Assert.Equal("name_date", ArchiveRootPolicy.ResolveWrapName("name_date", null, null, Stamp));
    }

    // ════════════════════════════════════════════════════════════════
    //  模板（含花括号）→ 渲染
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Template_RendersNameAndDate()
    {
        var paths = new List<string> { @"D:\dl\7zip" };
        Assert.Equal("7zip_2026-11-17", ArchiveRootPolicy.ResolveWrapName("{name}_{date}", paths, null, Stamp));
    }

    [Fact]
    public void Template_UsesArchiveToken()
    {
        Assert.Equal("backup_backup", ArchiveRootPolicy.ResolveWrapName(
            "{archive}_backup", null, @"D:\out\backup.zip", Stamp));
    }

    [Fact]
    public void Template_MultipleSources_UsesCommonParentName()
    {
        var paths = new List<string> { @"D:\dl\photos\a.jpg", @"D:\dl\photos\b.jpg" };
        Assert.Equal("photos_2026-11-17",
            ArchiveRootPolicy.ResolveWrapName("{name}_{date}", paths, null, Stamp));
    }

    [Fact]
    public void Template_DefaultTimestampIsNow_ButExplicitStampWins()
    {
        // 显式时间戳必须优先，否则「预览 23:59、执行 00:01」的坑会回来
        var result = ArchiveRootPolicy.ResolveWrapName("{date}", null, null, Stamp);
        Assert.Equal("2026-11-17", result);
    }

    [Fact]
    public void Template_ResultIsSanitizedAfterRender()
    {
        // 渲染结果本身通过 RootNameTemplate 校验（不含非法字符），这里确认最终那一步净化不会丢字
        Assert.Equal("照片_2026-11-17",
            ArchiveRootPolicy.ResolveWrapName("照片_{date}", null, null, Stamp));
    }

    // ════════════════════════════════════════════════════════════════
    //  模板渲染失败 → 静默回落压缩包名（不抛、不阻塞压缩）
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UnknownPlaceholder_FallsBackToArchiveName()
    {
        // 引擎侧不允许抛：对话框已经拦过一次，这里只是最后一道保险
        Assert.Equal("archive", ArchiveRootPolicy.ResolveWrapName("{foo}", null, @"D:\out\archive.7z", Stamp));
    }

    [Fact]
    public void EmptyRenderingResult_FallsBackToArchiveName()
    {
        // 没给压缩包路径时 {archive} 渲染成空串 ⇒ 结果校验按 EmptyResult 拦下
        Assert.Equal("archive", ArchiveRootPolicy.ResolveWrapName("{archive}", null, null, Stamp));
    }

    [Fact]
    public void InvalidResult_FallsBackToArchiveName()
    {
        // 源主名恰好是保留设备名 CON ⇒ 渲染结果按 InvalidName 拦下，回落压缩包名
        var paths = new List<string> { @"D:\dl\CON" };
        Assert.Equal("archive", ArchiveRootPolicy.ResolveWrapName("{name}", paths, @"D:\out\archive.7z", Stamp));
    }

    [Fact]
    public void RenderFailureWithoutArchivePath_FallsBackToArchiveKeyword()
    {
        Assert.Equal("archive", ArchiveRootPolicy.ResolveWrapName("{foo}", null, null, Stamp));
    }

    // ════════════════════════════════════════════════════════════════
    //  与 GetWrapName 的关系：字面名/回落路径完全等价
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("myroot")]
    [InlineData("a:b")]
    [InlineData(null)]
    [InlineData("")]
    public void NonTemplateInput_MatchesGetWrapNameExactly(string? name)
    {
        var expected = ArchiveRootPolicy.GetWrapName(name, @"D:\out\archive.7z");
        var actual = ArchiveRootPolicy.ResolveWrapName(name, null, @"D:\out\archive.7z", Stamp);
        Assert.Equal(expected, actual);
    }
}
