using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// ArchiveRootPolicy —— 「包内根目录」四态的统一决策与条目名改写。
/// 四态是互斥单选，①恒不动、②只在有唯一根时剥、③只在没有唯一根时加、④无条件加。
/// </summary>
public class ArchiveRootPolicyTests
{
    // ── Resolve：4 态 × hasSingleRoot 两值 = 8 组合 ────────────────────────

    [Fact]
    public void Resolve_Original_WithSingleRoot_DoesNothing()
    {
        var adj = ArchiveRootPolicy.Resolve(RootDirectoryMode.Original, hasSingleRoot: true);
        Assert.False(adj.Strip);
        Assert.False(adj.Wrap);
        Assert.False(adj.IsActive);
    }

    [Fact]
    public void Resolve_Original_WithoutSingleRoot_DoesNothing()
    {
        var adj = ArchiveRootPolicy.Resolve(RootDirectoryMode.Original, hasSingleRoot: false);
        Assert.False(adj.IsActive);
    }

    [Fact]
    public void Resolve_SmartStrip_WithSingleRoot_Strips()
    {
        var adj = ArchiveRootPolicy.Resolve(RootDirectoryMode.SmartStrip, hasSingleRoot: true);
        Assert.True(adj.Strip);
        Assert.False(adj.Wrap);
    }

    [Fact]
    public void Resolve_SmartStrip_WithoutSingleRoot_DoesNothing()
    {
        // 散装包/多源：没有可剥的那一层，保持原样（不能误剥顶层）
        var adj = ArchiveRootPolicy.Resolve(RootDirectoryMode.SmartStrip, hasSingleRoot: false);
        Assert.False(adj.IsActive);
    }

    [Fact]
    public void Resolve_SmartWrap_WithSingleRoot_DoesNothing()
    {
        // 这就是「智能」与「始终」的唯一区别
        var adj = ArchiveRootPolicy.Resolve(RootDirectoryMode.SmartWrap, hasSingleRoot: true);
        Assert.False(adj.IsActive);
    }

    [Fact]
    public void Resolve_SmartWrap_WithoutSingleRoot_Wraps()
    {
        var adj = ArchiveRootPolicy.Resolve(RootDirectoryMode.SmartWrap, hasSingleRoot: false);
        Assert.False(adj.Strip);
        Assert.True(adj.Wrap);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resolve_AlwaysWrap_AlwaysWraps(bool hasSingleRoot)
    {
        var adj = ArchiveRootPolicy.Resolve(RootDirectoryMode.AlwaysWrap, hasSingleRoot);
        Assert.False(adj.Strip);
        Assert.True(adj.Wrap);
    }

    [Fact]
    public void Resolve_NeverStripsAndWrapsTogether()
    {
        // ②③ 的条件互斥，故 Strip 与 Wrap 永不同时为 true（否则条目名会被剥完又套回去）
        foreach (var mode in Enum.GetValues<RootDirectoryMode>())
        {
            foreach (var hasRoot in new[] { true, false })
            {
                var adj = ArchiveRootPolicy.Resolve(mode, hasRoot);
                Assert.False(adj.Strip && adj.Wrap, $"{mode}/{hasRoot} 同时剥与加");
            }
        }
    }

    // ── 枚举值是持久化契约 ────────────────────────────────────────────────

    [Fact]
    public void RootDirectoryMode_HasStableNumericValues()
    {
        // AppSettings 按 int 存，改值会让老用户的设置在升级后语义漂移
        Assert.Equal(0, (int)RootDirectoryMode.Original);
        Assert.Equal(1, (int)RootDirectoryMode.SmartStrip);
        Assert.Equal(2, (int)RootDirectoryMode.SmartWrap);
        Assert.Equal(3, (int)RootDirectoryMode.AlwaysWrap);
    }

    // ── Apply：条目名改写 ────────────────────────────────────────────────

    [Theory]
    // 无调整：原样返回
    [InlineData("7zip/a.txt", null, null, "7zip/a.txt")]
    [InlineData("a.txt", null, null, "a.txt")]
    // 剥根：子项去掉前缀，根目录条目本身被丢弃
    [InlineData("7zip/a.txt", "7zip", null, "a.txt")]
    [InlineData("7zip/sub/b.txt", "7zip", null, "sub/b.txt")]
    [InlineData("7zip/", "7zip", null, null)]
    [InlineData("7zip", "7zip", null, null)]
    // 剥根：不在该前缀下的条目原样保留
    [InlineData("other/c.txt", "7zip", null, "other/c.txt")]
    // 加根：所有条目统一加前缀
    [InlineData("7zip/a.txt", null, "root", "root/7zip/a.txt")]
    [InlineData("a.txt", null, "root", "root/a.txt")]
    // 反斜杠输入统一成正斜杠
    [InlineData(@"7zip\a.txt", "7zip", null, "a.txt")]
    public void Apply_RewritesRelativePath(string input, string? stripRoot, string? wrapRoot, string? expected)
    {
        Assert.Equal(expected, ArchiveRootPolicy.Apply(input, stripRoot, wrapRoot));
    }

    [Fact]
    public void Apply_StripIsCaseInsensitive()
    {
        // Windows 上 7ZIP/ 与 7zip/ 是同一层，不能因为大小写不同就漏剥
        Assert.Equal("a.txt", ArchiveRootPolicy.Apply("7ZIP/a.txt", "7zip", null));
    }

    [Fact]
    public void Apply_EmptyPath_ReturnsNull()
    {
        Assert.Null(ArchiveRootPolicy.Apply(null, null, null));
        Assert.Null(ArchiveRootPolicy.Apply("", null, null));
        Assert.Null(ArchiveRootPolicy.Apply("   ", null, null));
    }

    [Fact]
    public void Apply_WithNoRoots_KeepsOriginalSeparators()
    {
        // 「原样」模式必须逐字节还原调用方给的分隔符：FileScanner 在 Windows 上产出 '\'，
        // 归一成 '/' 会让播种 Key 与引擎上报的 EntryKey 错位（真机上表现为进度行永远不终结）。
        Assert.Equal(@"7zip\a.txt", ArchiveRootPolicy.Apply(@"7zip\a.txt", null, null));
        Assert.Equal(@"7zip\a.txt", ArchiveRootPolicy.Apply(@"7zip\a.txt", RootAdjustment.None, "7zip", "root"));
    }

    [Fact]
    public void Apply_RewritesSeparatorsOnlyWhenRootsAreApplied()
    {
        Assert.Equal("a.txt", ArchiveRootPolicy.Apply(@"7zip\a.txt", "7zip", null));
        Assert.Equal("root/7zip/a.txt", ArchiveRootPolicy.Apply(@"7zip\a.txt", null, "root"));
    }

    [Fact]
    public void Apply_WithAdjustmentInactive_IgnoresRoots()
    {
        // 传了根名但调整未激活时不该改任何东西（防调用方忘记先 Resolve）
        var result = ArchiveRootPolicy.Apply("7zip/a.txt", RootAdjustment.None, "7zip", "root");
        Assert.Equal("7zip/a.txt", result);
    }

    [Fact]
    public void Apply_WithAdjustmentActive_AppliesRoots()
    {
        var adj = ArchiveRootPolicy.Resolve(RootDirectoryMode.SmartStrip, hasSingleRoot: true);
        Assert.Equal("a.txt", ArchiveRootPolicy.Apply("7zip/a.txt", adj, "7zip", "root"));
    }

    // ── HasSingleSourceDirectory ────────────────────────────────────────

    [Fact]
    public void HasSingleSourceDirectory_OneDirectory_IsTrue()
    {
        var dir = Directory.CreateTempSubdirectory("mz-root-").FullName;
        try
        {
            Assert.True(ArchiveRootPolicy.HasSingleSourceDirectory(new[] { dir }));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void HasSingleSourceDirectory_MultipleDirectories_IsFalse()
    {
        var a = Directory.CreateTempSubdirectory("mz-root-a-").FullName;
        var b = Directory.CreateTempSubdirectory("mz-root-b-").FullName;
        try
        {
            Assert.False(ArchiveRootPolicy.HasSingleSourceDirectory(new[] { a, b }));
        }
        finally
        {
            Directory.Delete(a, recursive: true);
            Directory.Delete(b, recursive: true);
        }
    }

    [Fact]
    public void HasSingleSourceDirectory_MixedSource_IsFalse()
    {
        var dir = Directory.CreateTempSubdirectory("mz-root-").FullName;
        var file = Path.Combine(dir, "a.txt");
        File.WriteAllText(file, "x");
        try
        {
            Assert.False(ArchiveRootPolicy.HasSingleSourceDirectory(new[] { dir, file }));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void HasSingleSourceDirectory_SingleFile_IsFalse()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.False(ArchiveRootPolicy.HasSingleSourceDirectory(new[] { file }));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void HasSingleSourceDirectory_Null_IsFalse()
    {
        Assert.False(ArchiveRootPolicy.HasSingleSourceDirectory(null));
    }

    [Fact]
    public void HasSingleSourceDirectory_EmptyList_IsFalse()
    {
        Assert.False(ArchiveRootPolicy.HasSingleSourceDirectory(Array.Empty<string>()));
    }

    [Fact]
    public void HasSingleSourceDirectory_TrailingSeparator_StillTrue()
    {
        var dir = Directory.CreateTempSubdirectory("mz-root-").FullName;
        try
        {
            Assert.True(ArchiveRootPolicy.HasSingleSourceDirectory(new[] { dir + Path.DirectorySeparatorChar }));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── GetSourceStripName ──────────────────────────────────────────────

    [Fact]
    public void GetSourceStripName_ReturnsLeafDirectoryName()
    {
        Assert.Equal("7zip", ArchiveRootPolicy.GetSourceStripName(@"D:\data\7zip"));
        Assert.Equal("7zip", ArchiveRootPolicy.GetSourceStripName(@"D:\data\7zip\"));
        Assert.Equal("7zip", ArchiveRootPolicy.GetSourceStripName(@"D:\data\7zip/"));
    }

    // ── GetWrapName ─────────────────────────────────────────────────────

    [Fact]
    public void GetWrapName_PrefersExplicitName()
    {
        Assert.Equal("myroot", ArchiveRootPolicy.GetWrapName("myroot", @"D:\out\archive.7z"));
    }

    [Fact]
    public void GetWrapName_FallsBackToArchiveNameWithoutExtension()
    {
        Assert.Equal("archive", ArchiveRootPolicy.GetWrapName(null, @"D:\out\archive.7z"));
        Assert.Equal("archive", ArchiveRootPolicy.GetWrapName("", @"D:\out\archive.7z"));
        Assert.Equal("archive", ArchiveRootPolicy.GetWrapName("   ", @"D:\out\archive.7z"));
    }

    [Fact]
    public void GetWrapName_HandlesDoubleExtension()
    {
        Assert.Equal("archive", ArchiveRootPolicy.GetWrapName(null, @"D:\out\archive.tar.gz"));
    }

    [Fact]
    public void GetWrapName_NothingUsable_FallsBackToArchive()
    {
        Assert.Equal("archive", ArchiveRootPolicy.GetWrapName(null));
        Assert.Equal("archive", ArchiveRootPolicy.GetWrapName(null, ""));
    }

    [Fact]
    public void GetWrapName_SanitizesInvalidCharacters()
    {
        // 压缩包名里出现非法字符时不该让整个压缩失败，替换成下划线
        Assert.Equal("a_b", ArchiveRootPolicy.GetWrapName("a:b"));
        Assert.Equal("a_b", ArchiveRootPolicy.GetWrapName("a/b"));
        Assert.Equal("a_b_c", ArchiveRootPolicy.GetWrapName(@"a\b:c"));
    }

    // ── 分卷包根名：先归到基卷再取，别把格式名带进目录里 ──

    [Theory]
    // 7z / zip 分卷：pack.7z.001 → pack（而不是 pack.7z）
    [InlineData(@"D:\out\pack.7z.001", "pack")]
    [InlineData(@"D:\out\pack.zip.001", "pack")]
    // rar 两种卷名
    [InlineData(@"D:\out\pack.part2.rar", "pack")]
    [InlineData(@"D:\out\pack.r01", "pack")]
    // 普通包与双扩展名照旧
    [InlineData(@"D:\out\archive.7z", "archive")]
    [InlineData(@"D:\out\archive.tar.gz", "archive")]
    public void GetWrapName_VolumeSet_UsesBaseNameWithoutFormat(string archivePath, string expected)
        => Assert.Equal(expected, ArchiveRootPolicy.GetWrapName(null, archivePath));

    [Fact]
    public void GetWrapName_VolumeSet_TemplateFallbackAlsoUsesBaseName()
    {
        // 根名模板渲染失败时的回落同样要归卷，否则 {archive} 与回落值会不一致
        var name = ArchiveRootPolicy.ResolveWrapName("x_{unknown}/y", fallbackArchivePath: @"D:\out\pack.7z.001");
        Assert.Equal("pack", name);
    }

    [Fact]
    public void GetArchiveBaseName_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal("", ArchiveRootPolicy.GetArchiveBaseName(null));
        Assert.Equal("", ArchiveRootPolicy.GetArchiveBaseName("   "));
    }

    // ===== ResolveSourceRoots（新建 / 追加 / 扫描器共用的唯一判定）=====

    [Fact]
    public void ResolveSourceRoots_Original_DoesNothing()
        => Assert.Equal((null, null), ArchiveRootPolicy.ResolveSourceRoots(RootDirectoryMode.Original, null));

    [Fact]
    public void ResolveSourceRoots_SmartStrip_SingleDirectory_StripsThatDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var (strip, wrap) = ArchiveRootPolicy.ResolveSourceRoots(RootDirectoryMode.SmartStrip, [dir]);
            Assert.Equal(Path.GetFileName(dir), strip);
            Assert.Null(wrap);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveSourceRoots_AlwaysWrap_ResolvesNameFromArchive()
    {
        // 没有显式根名时由包名回落，且与 GetWrapName 同源
        var (strip, wrap) = ArchiveRootPolicy.ResolveSourceRoots(
            RootDirectoryMode.AlwaysWrap, null, wrapRootTemplate: null, archivePath: @"D:\out\pack.7z.001");
        Assert.Null(strip);
        Assert.Equal("pack", wrap);
    }

    [Fact]
    public void ResolveSourceRoots_SmartWrap_WithSingleDirectory_DoesNotWrap()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal((null, null), ArchiveRootPolicy.ResolveSourceRoots(
                RootDirectoryMode.SmartWrap, [dir], "wrap", @"D:\out\pack.zip"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ===== IsWrapMode（会加根的两档）=====

    /// <summary>
    /// 会加根的档位只有 ③④：根名输入框是否显示、根名要不要随载荷走、执行侧要不要解析根名
    /// 都问这一个判定。②智能去根虽然也「动根」，但不加根，故不算。
    /// </summary>
    [Theory]
    [InlineData(RootDirectoryMode.Original, false)]
    [InlineData(RootDirectoryMode.SmartStrip, false)]
    [InlineData(RootDirectoryMode.SmartWrap, true)]
    [InlineData(RootDirectoryMode.AlwaysWrap, true)]
    [InlineData((RootDirectoryMode)99, false)]
    public void IsWrapMode_OnlyWrapModes_ReturnTrue(RootDirectoryMode mode, bool expected)
        => Assert.Equal(expected, ArchiveRootPolicy.IsWrapMode(mode));

    // ===== NormalizeSmartExtractMode（智能解压两档）=====

    [Theory]
    [InlineData(RootDirectoryMode.SmartWrap, RootDirectoryMode.SmartWrap)]
    [InlineData(RootDirectoryMode.SmartStrip, RootDirectoryMode.SmartStrip)]
    // 两档之外的任何值都回落默认档：Original / AlwaysWrap / 越界数字
    [InlineData(RootDirectoryMode.Original, RootDirectoryMode.SmartWrap)]
    [InlineData(RootDirectoryMode.AlwaysWrap, RootDirectoryMode.SmartWrap)]
    [InlineData((RootDirectoryMode)99, RootDirectoryMode.SmartWrap)]
    [InlineData((RootDirectoryMode)(-1), RootDirectoryMode.SmartWrap)]
    public void NormalizeSmartExtractMode_OnlyKeepsSmartStrip(RootDirectoryMode input, RootDirectoryMode expected)
        => Assert.Equal(expected, ArchiveRootPolicy.NormalizeSmartExtractMode(input));

    /// <summary>
    /// 两档 × 三种包型 → 真正的指令。这是智能解压落点的唯一事实源，
    /// 测试它而不是在测试里把 dest 公式抄一遍（旧用例就是那么写的，等于测自己的算术）。
    /// </summary>
    [Theory]
    // 智能加根：单根不动（复用包自带根）、散装/多根补一层
    [InlineData(RootDirectoryMode.SmartWrap, true, false, false)]
    [InlineData(RootDirectoryMode.SmartWrap, false, false, true)]
    // 智能去根：单根剥、散装/多根不动（= 原地平铺）
    [InlineData(RootDirectoryMode.SmartStrip, true, true, false)]
    [InlineData(RootDirectoryMode.SmartStrip, false, false, false)]
    public void SmartExtract_TwoModes_ProduceExpectedAdjustment(
        RootDirectoryMode mode, bool hasSingleRoot, bool expectStrip, bool expectWrap)
    {
        var normalized = ArchiveRootPolicy.NormalizeSmartExtractMode(mode);
        var adjustment = ArchiveRootPolicy.Resolve(normalized, hasSingleRoot);

        Assert.Equal(expectStrip, adjustment.Strip);
        Assert.Equal(expectWrap, adjustment.Wrap);
    }
}
