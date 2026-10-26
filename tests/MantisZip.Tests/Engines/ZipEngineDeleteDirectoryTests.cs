using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using SharpSevenZip;
using Xunit;

namespace MantisZip.Tests.Engines;

/// <summary>
/// 删除目录（递归语义）专项测试。
/// <para>
/// 回归背景（插队项 delete-directory-entries）：列表里的目录行 <c>FullPath</c> 不带尾斜杠（如 "docs"），
/// 而包内条目是 "docs/…"；删除原先按条目精确比较 → 零命中，且 copy-mode 不校验存在性，
/// 表现为「删除目录静默无效、进度条照样走满 100%」。
/// </para>
/// <para>
/// 本组用例锁定三件事：①目录行形态（无尾斜杠）与包内形态（带尾斜杠）都能删掉整个目录；
/// ②前缀相同的兄弟目录（ab/ vs a/）不能被误删；③目标不存在时必须抛
/// <see cref="ArchiveEntryNotFoundException"/> 且压缩包字节不变（不能静默成功）。
/// </para>
/// </summary>
public class ZipEngineDeleteDirectoryTests : IDisposable
{
    private readonly ZipEngine _engine = new();
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    public ZipEngineDeleteDirectoryTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles.Where(File.Exists))
            try { File.Delete(f); } catch { }
        foreach (var d in _tempDirs.Where(Directory.Exists))
            try { Directory.Delete(d, true); } catch { }
    }

    private string TrackFile(string path) { _tempFiles.Add(path); return path; }
    private string TrackDir(string path) { _tempDirs.Add(path); return path; }

    /// <summary>
    /// 按「名称 / 内容」清单建 ZIP：<paramref name="entries"/> 的内容为 null 表示显式目录条目
    /// （名以 "/" 结尾、零字节，与 WinRAR / 7-Zip 打出的包一致）。
    /// </summary>
    private string CreateZip(params (string Name, string? Content)[] entries)
    {
        var path = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_dir.zip"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var fs = File.Create(path);
        using var zipStream = new ZipOutputStream(fs);
        zipStream.SetLevel(9);

        foreach (var (name, content) in entries)
        {
            zipStream.PutNextEntry(new ZipEntry(name));
            if (content is not null)
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                zipStream.Write(bytes, 0, bytes.Length);
            }
            zipStream.CloseEntry();
        }

        return path;
    }

    /// <summary>列出包内条目全路径（排序后便于整体比较）。</summary>
    private async Task<string[]> ListPathsAsync(string archive)
    {
        var entries = await _engine.ListEntriesAsync(archive);
        return entries.Select(e => e.FullPath).OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    // ===== 目录行形态：无尾斜杠 =====

    [Fact]
    public async Task DirectoryPath_DeletesExplicitDirectoryEntryAndChildren()
    {
        var archive = CreateZip(
            ("docs/", null), ("docs/a.txt", "A"), ("docs/b.txt", "B"), ("keep.txt", "K"));

        // UI 目录行传的就是 FullPath："docs"（不带尾斜杠）
        await _engine.DeleteEntriesAsync(archive, ["docs"]);

        Assert.Equal(["keep.txt"], await ListPathsAsync(archive));
    }

    [Fact]
    public async Task DirectoryPathWithTrailingSlash_DeletesChildren()
    {
        var archive = CreateZip(("docs/", null), ("docs/a.txt", "A"), ("keep.txt", "K"));

        // 包内条目形态：带尾斜杠也必须命中同一批条目（原 TrimEndSeparator 的 >3 守卫会漏掉短路径）
        await _engine.DeleteEntriesAsync(archive, ["docs/"]);

        Assert.Equal(["keep.txt"], await ListPathsAsync(archive));
    }

    [Fact]
    public async Task ImplicitDirectory_DeletesAllChildren_WithoutExplicitDirEntries()
    {
        // 没有任何显式目录条目（部分工具打出的包就是这样），只有后代文件
        var archive = CreateZip(
            ("docs/a.txt", "A"), ("docs/deep/b.txt", "B"), ("keep.txt", "K"));

        await _engine.DeleteEntriesAsync(archive, ["docs"]);

        Assert.Equal(["keep.txt"], await ListPathsAsync(archive));
    }

    [Fact]
    public async Task DirectoryAndFile_DeletedInOneCall()
    {
        var archive = CreateZip(
            ("docs/", null), ("docs/a.txt", "A"), ("hello.txt", "H"), ("keep.txt", "K"));

        await _engine.DeleteEntriesAsync(archive, ["docs", "hello.txt"]);

        Assert.Equal(["keep.txt"], await ListPathsAsync(archive));
    }

    [Fact]
    public async Task DirectoryPath_IsCaseInsensitive()
    {
        var archive = CreateZip(("Docs/A.txt", "A"), ("keep.txt", "K"));

        await _engine.DeleteEntriesAsync(archive, ["docs"]);

        Assert.Equal(["keep.txt"], await ListPathsAsync(archive));
    }

    // ===== 前缀边界：不能误删兄弟目录 =====

    [Fact]
    public async Task PrefixLookalikeDirectory_IsNotDeleted()
    {
        var archive = CreateZip(("a/x.txt", "X"), ("ab/y.txt", "Y"));

        await _engine.DeleteEntriesAsync(archive, ["a"]);

        // 只剩 ab/y.txt：StartsWith 式实现会把 ab/ 一起删掉，本用例是它的护栏
        Assert.Equal(["ab/y.txt"], await ListPathsAsync(archive));
    }

    // ===== 目标不存在：必须报错且不改动压缩包 =====

    [Fact]
    public async Task NonExistentDirectory_Throws_AndLeavesArchiveUntouched()
    {
        var archive = CreateZip(("docs/a.txt", "A"), ("keep.txt", "K"));
        var before = await File.ReadAllBytesAsync(archive);

        var ex = await Assert.ThrowsAsync<ArchiveEntryNotFoundException>(() =>
            _engine.DeleteEntriesAsync(archive, ["nope"]));

        Assert.Equal("nope", ex.EntryPath);
        // 派生自 FileNotFoundException：只捕获基类的既有消费者不会漏
        Assert.IsAssignableFrom<FileNotFoundException>(ex);
        Assert.Equal(before, await File.ReadAllBytesAsync(archive));
    }

    [Fact]
    public async Task PartiallyMissingTargets_Throws_AndLeavesArchiveUntouched()
    {
        var archive = CreateZip(("docs/a.txt", "A"), ("keep.txt", "K"));
        var before = await File.ReadAllBytesAsync(archive);

        // 一个存在 + 一个不存在 → 整体失败（不做「能删的先删」的半成品）
        var ex = await Assert.ThrowsAsync<ArchiveEntryNotFoundException>(() =>
            _engine.DeleteEntriesAsync(archive, ["docs", "nope"]));

        Assert.Equal("nope", ex.EntryPath);
        Assert.Equal(before, await File.ReadAllBytesAsync(archive));
    }

    // ===== 加密包（走 legacy 重写路径）同样要带递归语义 =====

    [Fact]
    public async Task EncryptedArchive_DirectoryDelete_WithPassword()
    {
        var archive = TrackFile(Path.Combine(
            Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}_encdir.zip"));
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);

        using (var fs = File.Create(archive))
        using (var zipStream = new ZipOutputStream(fs))
        {
            zipStream.SetLevel(9);
            zipStream.Password = "test123";
            foreach (var (name, text) in new[] { ("docs/a.txt", "A"), ("keep.txt", "K") })
            {
                var entry = new ZipEntry(name) { IsCrypted = true };
                zipStream.PutNextEntry(entry);
                var bytes = Encoding.UTF8.GetBytes(text);
                zipStream.Write(bytes, 0, bytes.Length);
                zipStream.CloseEntry();
            }
        }

        await _engine.DeleteEntriesAsync(archive, ["docs"], password: "test123");

        Assert.Equal(["keep.txt"], await ListPathsAsync(archive));
    }

    // ===== 7z：整包解压 + 原样重打包路径（CI 的 --filter 会排除，本地回归用） =====

    private static bool Is7zDllAvailable() => File.Exists(SevenZipEngine.SevenZipDllPath);

    [Fact]
    public async Task SevenZip_DirectoryPath_DeletesDirectoryAndChildren()
    {
        if (!Is7zDllAvailable()) return;

        var root = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(Path.Combine(src, "docs"));
        await File.WriteAllTextAsync(Path.Combine(src, "docs", "a.txt"), "A");
        await File.WriteAllTextAsync(Path.Combine(src, "keep.txt"), "K");
        var archive = Path.Combine(root, "del.7z");

        var engine = new SevenZipEngine();
        await engine.CompressAsync([src], archive, new ArchiveOptions { SevenZipMultithreaded = false });

        await engine.DeleteEntriesAsync(archive, ["src/docs"], password: null, progress: null,
            cancellationToken: CancellationToken.None,
            options: new ArchiveOptions { SevenZipMultithreaded = false });

        var entries = await engine.ListEntriesAsync(archive);
        Assert.DoesNotContain(entries, e =>
            e.FullPath.StartsWith("src/docs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, e =>
            e.FullPath.EndsWith("keep.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SevenZip_NonExistentDirectory_ThrowsArchiveEntryNotFoundException()
    {
        if (!Is7zDllAvailable()) return;

        var root = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(src);
        await File.WriteAllTextAsync(Path.Combine(src, "keep.txt"), "K");
        var archive = Path.Combine(root, "missing.7z");

        var engine = new SevenZipEngine();
        await engine.CompressAsync([src], archive, new ArchiveOptions { SevenZipMultithreaded = false });

        var before = await File.ReadAllBytesAsync(archive);
        var ex = await Assert.ThrowsAsync<ArchiveEntryNotFoundException>(() =>
            engine.DeleteEntriesAsync(archive, ["src/nope"], password: null, progress: null,
                cancellationToken: CancellationToken.None,
                options: new ArchiveOptions { SevenZipMultithreaded = false }));

        Assert.Equal("src/nope", ex.EntryPath);
        Assert.Equal(before, await File.ReadAllBytesAsync(archive));
    }

    // ===== 删除后压缩包仍是健康包 / 删空即删包 =====

    /// <summary>
    /// 删除目录后包必须仍能通过自检：copy-mode 是「重写中央目录 + 拷贝数据区」，
    /// 少写/多写一条偏移都会让包进入「能列出、解不出」的状态，只有自检能发现。
    /// </summary>
    [Fact]
    public async Task DirectoryDelete_ThenArchiveStillPassesSelfTest()
    {
        var archive = CreateZip([
            ("docs/a.txt", "A"),
            ("docs/b.txt", "B"),
            ("keep.txt", "K"),
        ]);

        await _engine.DeleteEntriesAsync(archive, ["docs"]);

        Assert.Equal(["keep.txt"], await ListPathsAsync(archive));
        Assert.True(await _engine.TestArchiveAsync(archive), "删除后自检应通过");
    }

    [Fact]
    public async Task SevenZip_DirectoryDelete_ThenArchiveStillPassesSelfTest()
    {
        if (!Is7zDllAvailable()) return;

        var root = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(Path.Combine(src, "docs", "deep"));
        await File.WriteAllTextAsync(Path.Combine(src, "docs", "a.txt"), "A");
        await File.WriteAllTextAsync(Path.Combine(src, "docs", "deep", "b.txt"), "B");
        await File.WriteAllTextAsync(Path.Combine(src, "keep.txt"), "K");
        var archive = Path.Combine(root, "selftest.7z");

        var engine = new SevenZipEngine();
        await engine.CompressAsync([src], archive, new ArchiveOptions { SevenZipMultithreaded = false });
        await engine.DeleteEntriesAsync(archive, ["src/docs"], password: null, progress: null,
            cancellationToken: CancellationToken.None,
            options: new ArchiveOptions { SevenZipMultithreaded = false });

        Assert.True(await engine.TestArchiveAsync(archive), "7z 删除后自检应通过");
    }

    /// <summary>
    /// 删掉包内最后一个条目时，既有行为是**直接删除压缩包**（ZIP copy-mode 与 7z 两条路径一致），
    /// 而不是留一个 0 条目的空包。此处锁定该行为，避免日后改写成留空包却不通知调用方。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletingLastEntry_RemovesArchive(bool sevenZip)
    {
        if (sevenZip && !Is7zDllAvailable()) return;

        string archive;
        if (sevenZip)
        {
            var root = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
            var src = Path.Combine(root, "only");
            Directory.CreateDirectory(src);
            await File.WriteAllTextAsync(Path.Combine(src, "a.txt"), "A");
            archive = Path.Combine(root, "only.7z");

            var engine = new SevenZipEngine();
            await engine.CompressAsync([src], archive, new ArchiveOptions { SevenZipMultithreaded = false });
            await engine.DeleteEntriesAsync(archive, ["only"], password: null, progress: null,
                cancellationToken: CancellationToken.None,
                options: new ArchiveOptions { SevenZipMultithreaded = false });
        }
        else
        {
            archive = CreateZip([("docs/a.txt", "A")]);
            await _engine.DeleteEntriesAsync(archive, ["docs"]);
        }

        Assert.False(File.Exists(archive), "删掉最后一个条目后应删除压缩包本身");
    }
}
