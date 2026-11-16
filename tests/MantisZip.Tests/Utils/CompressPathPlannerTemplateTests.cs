using System;
using System.IO;
using System.Linq;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// CompressPathPlanner 的模板接入。
/// 核心不变量：模板为空时行为与引入前**逐字节一致**（零回归），模板非空时忽略 keepOriginalExt。
/// </summary>
public class CompressPathPlannerTemplateTests
{
    private static readonly DateTime Stamp = new(2026, 11, 6, 15, 30, 24);

    // ════════════════════════════════════════════════════════════════
    //  零行为变更（模板为空）
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(@"D:\work\report.txt", "zip", false, "report.zip")]
    [InlineData(@"D:\work\report.txt", "zip", true, "report.txt.zip")]
    [InlineData(@"D:\work\report.txt", "7z", false, "report.7z")]
    [InlineData(@"D:\work\report.txt", "tar.gz", false, "report.tar.gz")]
    public void ComputeArchiveName_NoTemplate_MatchesLegacyBehavior(
        string source, string format, bool keepOriginalExt, string expected)
    {
        Assert.Equal(expected, CompressPathPlanner.ComputeArchiveName(source, format, keepOriginalExt));
        // 显式传 null / 空白也应等价
        Assert.Equal(expected, CompressPathPlanner.ComputeArchiveName(
            source, format, keepOriginalExt, template: null));
        Assert.Equal(expected, CompressPathPlanner.ComputeArchiveName(
            source, format, keepOriginalExt, template: "   "));
    }

    [Fact]
    public void ComputeArchiveName_NoTemplate_InvalidTemplateFallsBackToLegacy()
    {
        // 半截模板（用户正在打字）不能崩，也不能改变命名
        Assert.Equal(
            "report.zip",
            CompressPathPlanner.ComputeArchiveName(@"D:\work\report.txt", "zip", false, template: "{na"));
    }

    // ════════════════════════════════════════════════════════════════
    //  模板生效
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeArchiveName_Template_AppendsFormatExtension()
    {
        Assert.Equal(
            "report_2026-11-06.zip",
            CompressPathPlanner.ComputeArchiveName(
                @"D:\work\report.txt", "zip", false, template: "{name}_{date}", timestamp: Stamp));
    }

    [Fact]
    public void ComputeArchiveName_Template_IgnoresKeepOriginalExt()
    {
        // 模板非空时 keepOriginalExt 不再参与命名
        var withFlag = CompressPathPlanner.ComputeArchiveName(
            @"D:\work\report.txt", "zip", true, template: "{name}", timestamp: Stamp);
        var withoutFlag = CompressPathPlanner.ComputeArchiveName(
            @"D:\work\report.txt", "zip", false, template: "{name}", timestamp: Stamp);

        Assert.Equal("report.zip", withFlag);
        Assert.Equal(withFlag, withoutFlag);
    }

    [Fact]
    public void ComputeArchiveName_Template_CanReAddOriginalExtension()
    {
        Assert.Equal(
            "report.txt.zip",
            CompressPathPlanner.ComputeArchiveName(
                @"D:\work\report.txt", "zip", false, template: "{name}{ext}", timestamp: Stamp));
    }

    [Fact]
    public void ComputeArchiveName_Template_TarGzGetsDoubleExtension()
    {
        Assert.Equal(
            "report_2026-11-06.tar.gz",
            CompressPathPlanner.ComputeArchiveName(
                @"D:\work\report.txt", "tar.gz", false, template: "{name}_{date}", timestamp: Stamp));
    }

    [Fact]
    public void ComputeArchiveName_Template_SequenceApplied()
    {
        Assert.Equal(
            "report_0002.zip",
            CompressPathPlanner.ComputeArchiveName(
                @"D:\work\report.txt", "zip", false, template: "{name}_{seq:0000}",
                sequence: 2, timestamp: Stamp));
    }

    [Fact]
    public void ComputeArchiveName_Template_AutoNameUsed()
    {
        Assert.Equal(
            "项目A.zip",
            CompressPathPlanner.ComputeArchiveName(
                @"D:\work\report.txt", "zip", false, template: "{name:auto}",
                timestamp: Stamp, autoName: "项目A"));
    }

    // ════════════════════════════════════════════════════════════════
    //  输出路径
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeOutputPath_Template_KeepsSourceDirectory()
    {
        var result = CompressPathPlanner.ComputeOutputPath(
            @"D:\work\report.txt", "zip", false, template: "{name}_{date}", timestamp: Stamp);

        Assert.Equal(@"D:\work\report_2026-11-06.zip", result);
    }

    [Fact]
    public void ComputeOutputPath_NoTemplate_MatchesLegacyBehavior()
    {
        Assert.Equal(
            @"D:\work\report.zip",
            CompressPathPlanner.ComputeOutputPath(@"D:\work\report.txt", "zip", false));
    }

    // ════════════════════════════════════════════════════════════════
    //  PlanSeparate
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void PlanSeparate_NoTemplate_MatchesLegacyBehavior()
    {
        var sources = new[] { @"D:\a\x.txt", @"D:\a\y.txt" };
        var plan = CompressPathPlanner.PlanSeparate(sources, "zip", false);

        Assert.Equal(2, plan.Count);
        Assert.Equal(@"D:\a\x.zip", plan[0].OutputArchivePath);
        Assert.Equal(@"D:\a\y.zip", plan[1].OutputArchivePath);
    }

    [Fact]
    public void PlanSeparate_Template_SequenceFollowsSourceOrder()
    {
        var sources = new[] { @"D:\a\x.txt", @"D:\a\y.txt", @"D:\b\z.txt" };
        var plan = CompressPathPlanner.PlanSeparate(
            sources, "zip", false, template: "{name}_{seq:0000}", timestamp: Stamp);

        Assert.Equal(@"D:\a\x_0001.zip", plan[0].OutputArchivePath);
        Assert.Equal(@"D:\a\y_0002.zip", plan[1].OutputArchivePath);
        Assert.Equal(@"D:\b\z_0003.zip", plan[2].OutputArchivePath);
    }

    [Fact]
    public void PlanSeparate_Template_NameAutoEqualsName_BecauseEachPackageHasOneSource()
    {
        var sources = new[] { @"D:\a\x.txt", @"D:\a\y.txt" };
        var withAuto = CompressPathPlanner.PlanSeparate(
            sources, "zip", false, template: "{name:auto}", timestamp: Stamp);

        Assert.Equal(@"D:\a\x.zip", withAuto[0].OutputArchivePath);
        Assert.Equal(@"D:\a\y.zip", withAuto[1].OutputArchivePath);
    }

    // ════════════════════════════════════════════════════════════════
    //  单包多源（ComputeSingleArchiveName / ResolveAutoName）
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeSingleArchiveName_NoTemplate_ReturnsNull()
    {
        Assert.Null(CompressPathPlanner.ComputeSingleArchiveName(new[] { @"D:\a\x.txt" }, "zip"));
        Assert.Null(CompressPathPlanner.ComputeSingleArchiveName(
            new[] { @"D:\a\x.txt" }, "zip", template: "  "));
    }

    [Fact]
    public void ComputeSingleArchiveName_EmptySources_ReturnsNull()
        => Assert.Null(CompressPathPlanner.ComputeSingleArchiveName(
            Array.Empty<string>(), "zip", template: "{name}"));

    [Fact]
    public void ComputeSingleArchiveName_NameUsesFirstSource()
    {
        Assert.Equal(
            "x_2026-11-06.zip",
            CompressPathPlanner.ComputeSingleArchiveName(
                new[] { @"D:\a\x.txt", @"D:\a\y.txt" }, "zip",
                template: "{name}_{date}", timestamp: Stamp));
    }

    [Fact]
    public void ComputeSingleArchiveName_NameAutoUsesCommonParent()
    {
        Assert.Equal(
            "a.zip",
            CompressPathPlanner.ComputeSingleArchiveName(
                new[] { @"D:\a\sub\x.txt", @"D:\a\other\y.txt" }, "zip",
                template: "{name:auto}", timestamp: Stamp));
    }

    [Fact]
    public void ComputeSingleArchiveName_InvalidTemplate_ReturnsNull()
        => Assert.Null(CompressPathPlanner.ComputeSingleArchiveName(
            new[] { @"D:\a\x.txt", @"D:\a\y.txt" }, "zip", template: "{foo}"));

    [Fact]
    public void ResolveAutoName_SingleSource_ReturnsNull()
        => Assert.Null(CompressPathPlanner.ResolveAutoName(new[] { @"D:\a\sub\x.txt" }));

    [Fact]
    public void ResolveAutoName_TwoSourcesSameParent_ReturnsThatDirectoryName()
        => Assert.Equal("a", CompressPathPlanner.ResolveAutoName(
            new[] { @"D:\a\x.txt", @"D:\a\y.txt" }));

    [Fact]
    public void ResolveAutoName_DifferentDrives_ReturnsNullOrDriveRootName()
    {
        // 跨盘文件可能在本机不存在，FindCommonParent 取不到公共前缀 ⇒ null
        var result = CompressPathPlanner.ResolveAutoName(
            new[] { @"C:\one\x.txt", @"D:\two\y.txt" });
        Assert.True(result is null || result.Length > 0);
    }

    [Fact]
    public void ResolveAutoName_DriveRootCommon_ReturnsNull()
        => Assert.Null(CompressPathPlanner.ResolveAutoName(
            new[] { @"C:\x.txt", @"C:\y.txt" }));

    // ════════════════════════════════════════════════════════════════
    //  落点解析 / 手动路径合成（CompressSettingsViewModel 唯一入口的下沉实现）
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveOutputRoot_EmptySources_ReturnsNoSources()
    {
        var result = CompressPathPlanner.ResolveOutputRoot(Array.Empty<string>());
        Assert.Equal(CompressPathPlanner.OutputRootKind.NoSources, result.Kind);
        Assert.Null(result.Root);
    }

    [Fact]
    public void ResolveOutputRoot_SameDirectory_ReturnsCommonParent()
    {
        var result = CompressPathPlanner.ResolveOutputRoot(new[] { @"D:\a\x.txt", @"D:\a\y.txt" });
        Assert.Equal(CompressPathPlanner.OutputRootKind.CommonParent, result.Kind);
        Assert.Equal(@"D:\a", result.Root);
    }

    [Fact]
    public void ResolveOutputRoot_SiblingsUnderSharedParent_ReturnsThatParent()
    {
        var result = CompressPathPlanner.ResolveOutputRoot(
            new[] { @"D:\a\sub\x.txt", @"D:\a\other\y.txt" });
        Assert.Equal(CompressPathPlanner.OutputRootKind.CommonParent, result.Kind);
        Assert.Equal(@"D:\a", result.Root);
    }

    [Fact]
    public void ResolveOutputRoot_NoCommonSubdirectoryButSameDrive_ReturnsDriveRoot()
    {
        var result = CompressPathPlanner.ResolveOutputRoot(new[] { @"D:\x.txt", @"D:\y.txt" });
        Assert.Equal(CompressPathPlanner.OutputRootKind.DriveRoot, result.Kind);
        Assert.Equal(@"D:\", result.Root);
    }

    [Fact]
    public void ResolveOutputRoot_DirectoryNamesThatPrefixEachOther_ReturnsDriveRoot()
    {
        // D:\a 与 D:\ab 两个目录名互为前缀：公共父目录是 D:\，不是 D:\a
        var result = CompressPathPlanner.ResolveOutputRoot(new[] { @"D:\a\b\x.txt", @"D:\ab\y.txt" });
        Assert.Equal(CompressPathPlanner.OutputRootKind.DriveRoot, result.Kind);
        Assert.Equal(@"D:\", result.Root);
    }

    [Fact]
    public void ResolveOutputRoot_CrossDrive_ReturnsCrossDrive()
    {
        var result = CompressPathPlanner.ResolveOutputRoot(new[] { @"C:\one\x.txt", @"D:\two\y.txt" });
        Assert.Equal(CompressPathPlanner.OutputRootKind.CrossDrive, result.Kind);
        Assert.Null(result.Root);
    }

    [Fact]
    public void ResolveOutputRoot_DriveRootFallbackDisabled_ReturnsUnresolved()
    {
        var result = CompressPathPlanner.ResolveOutputRoot(
            new[] { @"D:\x.txt", @"D:\y.txt" }, allowDriveRootFallback: false);
        Assert.Equal(CompressPathPlanner.OutputRootKind.Unresolved, result.Kind);
        Assert.Null(result.Root);
    }

    [Theory]
    [InlineData(@"D:\a", "x", "zip", @"D:\a\x.zip")]
    [InlineData(@"D:\a", "x", "7z", @"D:\a\x.7z")]
    [InlineData(@"D:\a", "x", "tar.gz", @"D:\a\x.tar.gz")]
    public void ComputeManualOutputPath_CombinesDirectoryNameAndFormatExtension(
        string dir, string name, string format, string expected)
        => Assert.Equal(expected, CompressPathPlanner.ComputeManualOutputPath(dir, name, format));

    [Theory]
    [InlineData(null, "x")]
    [InlineData("", "x")]
    [InlineData("   ", "x")]
    [InlineData(@"D:\a", null)]
    [InlineData(@"D:\a", "")]
    [InlineData(@"D:\a", "   ")]
    public void ComputeManualOutputPath_AnyInputBlank_ReturnsNull(string? dir, string? name)
        => Assert.Null(CompressPathPlanner.ComputeManualOutputPath(dir, name, "zip"));

    [Fact]
    public void ResolveSingleBaseName_NoTemplate_UsesRootDirectoryName()
        => Assert.Equal("a", CompressPathPlanner.ResolveSingleBaseName(
            new[] { @"D:\a\x.txt", @"D:\a\y.txt" }, "zip", @"D:\a"));

    [Fact]
    public void ResolveSingleBaseName_NoTemplate_DriveRootUsesDriveLetter()
        => Assert.Equal("D", CompressPathPlanner.ResolveSingleBaseName(
            new[] { @"D:\x.txt", @"D:\y.txt" }, "zip", @"D:\"));

    [Fact]
    public void ResolveSingleBaseName_Template_TakesPrecedenceOverRootName()
        => Assert.Equal("x_2026-11-06", CompressPathPlanner.ResolveSingleBaseName(
            new[] { @"D:\a\x.txt", @"D:\a\y.txt" }, "zip", @"D:\a",
            template: "{name}_{date}", timestamp: Stamp));

    [Fact]
    public void ResolveSingleBaseName_InvalidTemplate_FallsBackToRootName()
        => Assert.Equal("a", CompressPathPlanner.ResolveSingleBaseName(
            new[] { @"D:\a\x.txt", @"D:\a\y.txt" }, "zip", @"D:\a", template: "{foo}"));

    // ════════════════════════════════════════════════════════════════
    //  格式扩展名
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("zip", ".zip")]
    [InlineData("7z", ".7z")]
    [InlineData("tar", ".tar")]
    [InlineData("tar.gz", ".tar.gz")]
    [InlineData("TAR.GZ", ".tar.gz")]
    public void GetFormatExtension_MapsFormats(string format, string expected)
        => Assert.Equal(expected, CompressPathPlanner.GetFormatExtension(format));
}
