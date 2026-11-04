using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// FileConflictHelper —— 路径安全与冲突处理的单元测试。
/// 重点覆盖 Zip Slip 防护（GetSafePath/SanitizeEntryPath）和文件名净化。
/// </summary>
public class FileConflictHelperTests
{
    // ════════════════════════════════════════════════════════════════
    //  GetSafePath — Zip Slip 防护
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetSafePath_NormalPath_ReturnsCombinedPath()
    {
        var result = FileConflictHelper.GetSafePath(@"C:\dest", "file.txt");
        Assert.Equal(Path.GetFullPath(@"C:\dest\file.txt"), result);
    }

    [Fact]
    public void GetSafePath_SubdirectoryPath_ReturnsCombinedPath()
    {
        var result = FileConflictHelper.GetSafePath(@"C:\dest", "subdir/file.txt");
        Assert.Equal(Path.GetFullPath(@"C:\dest\subdir\file.txt"), result);
    }

    [Fact]
    public void GetSafePath_TraversalAttack_ThrowsInvalidOperationException()
    {
        // Zip Slip: ../ 试图逃逸目标目录
        Assert.Throws<InvalidOperationException>(() =>
            FileConflictHelper.GetSafePath(@"C:\dest", "../evil.txt"));
    }

    [Fact]
    public void GetSafePath_NestedTraversal_ThrowsInvalidOperationException()
    {
        // 多层穿越
        Assert.Throws<InvalidOperationException>(() =>
            FileConflictHelper.GetSafePath(@"C:\dest", "sub/../../evil.txt"));
    }

    [Fact]
    public void GetSafePath_AbsolutePath_ThrowsInvalidOperationException()
    {
        // 绝对路径注入
        Assert.Throws<InvalidOperationException>(() =>
            FileConflictHelper.GetSafePath(@"C:\dest", @"C:\Windows\System32\evil.txt"));
    }

    [Fact]
    public void GetSafePath_BackslashTraversal_ThrowsInvalidOperationException()
    {
        // 反斜杠穿越
        Assert.Throws<InvalidOperationException>(() =>
            FileConflictHelper.GetSafePath(@"C:\dest", @"..\evil.txt"));
    }

    [Fact]
    public void GetSafePath_DotDotOnly_ThrowsInvalidOperationException()
    {
        // 纯 ".."
        Assert.Throws<InvalidOperationException>(() =>
            FileConflictHelper.GetSafePath(@"C:\dest", ".."));
    }

    [Fact]
    public void GetSafePath_EmptyEntryName_ReturnsDestDir()
    {
        // 空条目名 → 组合后就是目标目录本身
        var result = FileConflictHelper.GetSafePath(@"C:\dest", "");
        Assert.Equal(Path.GetFullPath(@"C:\dest"), result);
    }

    // ════════════════════════════════════════════════════════════════
    //  SanitizeEntryPath — 条目路径净化
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SanitizeEntryPath_NormalPath_Unchanged()
    {
        Assert.Equal("docs/file.txt", FileConflictHelper.SanitizeEntryPath("docs/file.txt"));
    }

    [Fact]
    public void SanitizeEntryPath_Backslash_ConvertedToSlash()
    {
        Assert.Equal("docs/file.txt", FileConflictHelper.SanitizeEntryPath(@"docs\file.txt"));
    }

    [Fact]
    public void SanitizeEntryPath_TraversalComponents_Dropped()
    {
        // ../ 被丢弃，保留正常组件
        Assert.Equal("docs/evil.txt", FileConflictHelper.SanitizeEntryPath("docs/../../evil.txt"));
    }

    [Fact]
    public void SanitizeEntryPath_PureTraversal_DropsAll()
    {
        // 纯穿越路径净化后回到根级文件名
        Assert.Equal("evil.txt", FileConflictHelper.SanitizeEntryPath("../../evil.txt"));
    }

    [Fact]
    public void SanitizeEntryPath_DotComponents_Dropped()
    {
        // . 组件被丢弃
        Assert.Equal("docs/file.txt", FileConflictHelper.SanitizeEntryPath("docs/./file.txt"));
    }

    [Fact]
    public void SanitizeEntryPath_InvalidFileNameChars_Dropped()
    {
        // 包含非法字符的组件被丢弃
        Assert.Equal("bad.txt", FileConflictHelper.SanitizeEntryPath("bad*name/bad.txt"));
    }

    [Fact]
    public void SanitizeEntryPath_MixedComponents_KeepsValid()
    {
        // 混合有效/无效组件：bad* 被丢弃（* 是非法字符）
        Assert.Equal("good/file.txt", FileConflictHelper.SanitizeEntryPath("good/bad*/file.txt"));
    }

    [Fact]
    public void SanitizeEntryPath_AllInvalid_ThrowsInvalidOperationException()
    {
        // 所有组件都被净化掉 → 抛异常
        Assert.Throws<InvalidOperationException>(() =>
            FileConflictHelper.SanitizeEntryPath("../.."));
    }

    [Fact]
    public void SanitizeEntryPath_LeadingSlash_Trimmed()
    {
        // 前导斜杠被 Normalize 处理
        Assert.Equal("file.txt", FileConflictHelper.SanitizeEntryPath("/file.txt"));
    }

    // ════════════════════════════════════════════════════════════════
    //  ResolveByAction — 冲突策略
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolvePath_FileNotExist_ReturnsSamePath()
    {
        // 文件不存在 → 直接返回路径（无需冲突处理）
        var testFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.txt");
        var result = FileConflictHelper.ResolvePath(testFile, null);
        Assert.Equal(testFile, result);
    }

    // ════════════════════════════════════════════════════════════════
    //  ConflictActionProvider — 运行期动态策略源（v0.5.7）
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolvePath_ProviderOverridesStaticAction()
    {
        // 静态 = 覆盖（会返回原路径），provider = 跳过（返回 null）⇒ provider 优先
        using var temp = new TempFile();
        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Overwrite,
            ConflictActionProvider = () => FileConflictAction.Skip,
        };

        Assert.Null(FileConflictHelper.ResolvePath(temp.Path, options));
    }

    [Fact]
    public async Task ResolvePathAsync_ProviderOverridesStaticAction()
    {
        using var temp = new TempFile();
        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Overwrite,
            ConflictActionProvider = () => FileConflictAction.Skip,
        };

        Assert.Null(await FileConflictHelper.ResolvePathAsync(temp.Path, options));
    }

    [Fact]
    public void ResolvePath_ProviderReadPerConflict_ChangeTakesEffectImmediately()
    {
        // 模拟进度窗口运行中改下拉：同一个 options，下一次冲突就按新策略走
        using var temp = new TempFile();
        var action = FileConflictAction.Skip;
        var options = new ArchiveOptions { ConflictActionProvider = () => action };

        Assert.Null(FileConflictHelper.ResolvePath(temp.Path, options));

        action = FileConflictAction.Overwrite;
        Assert.Equal(temp.Path, FileConflictHelper.ResolvePath(temp.Path, options));
    }

    [Fact]
    public void ResolvePath_ProviderNull_FallsBackToStaticAction()
    {
        // provider 为 null ⇒ 与引入该属性前逐字节一致
        using var temp = new TempFile();
        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Skip,
            ConflictActionProvider = null,
        };

        Assert.Null(FileConflictHelper.ResolvePath(temp.Path, options));
    }

    [Fact]
    public void ResolvePath_ProviderRename_ReturnsUniquePath()
    {
        using var temp = new TempFile();
        var options = new ArchiveOptions
        {
            ConflictActionProvider = () => FileConflictAction.Rename,
        };

        var resolved = FileConflictHelper.ResolvePath(temp.Path, options);

        Assert.NotNull(resolved);
        Assert.NotEqual(temp.Path, resolved);
        Assert.Equal(Path.GetDirectoryName(temp.Path), Path.GetDirectoryName(resolved));
    }

    [Fact]
    public async Task ResolvePathAsync_ProviderAsk_InvokesAsyncResolver()
    {
        // provider 返回 Ask ⇒ 走询问回调（进度窗口在运行中切回「询问」时就是这样）
        using var temp = new TempFile();
        var asked = 0;
        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Overwrite,
            ConflictActionProvider = () => FileConflictAction.Ask,
            ConflictResolverAsync = _ =>
            {
                asked++;
                return Task.FromResult(FileConflictAction.Skip);
            },
        };

        Assert.Null(await FileConflictHelper.ResolvePathAsync(temp.Path, options));
        Assert.Equal(1, asked);
    }

    [Fact]
    public void ResolvePath_ProviderAskWithoutResolver_DoesNotThrow()
    {
        // 兜底：Ask 但没装 resolver ⇒ 既有语义（按 action 落到覆盖），不得抛异常
        using var temp = new TempFile();
        var options = new ArchiveOptions
        {
            ConflictActionProvider = () => FileConflictAction.Ask,
        };

        Assert.Equal(temp.Path, FileConflictHelper.ResolvePath(temp.Path, options));
    }

    /// <summary>临时文件夹具：构造即创建，Dispose 时删除。</summary>
    private sealed class TempFile : IDisposable
    {
        public TempFile()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mz_conflict_{Guid.NewGuid():N}.txt");
            File.WriteAllText(Path, "existing");
        }

        public string Path { get; }

        public void Dispose()
        {
            try { File.Delete(Path); } catch (IOException) { /* 清理失败不影响断言 */ }
        }
    }
}
