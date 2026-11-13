using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using MantisZip.Tests.Fixtures;
using Xunit;

namespace MantisZip.Tests.Engines;

public class SmartExtractTests : IDisposable
{
    private readonly ZipEngine _engine = new();
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    public SmartExtractTests()
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

    // ===== ArchiveStructureAnalyzer.HasSingleRootDirectory =====

    [Fact]
    public void HasSingleRootDirectory_SingleRootFolder_ReturnsTrue()
    {
        var items = new List<ArchiveItem>
        {
            new() { FullPath = "dir/file1.txt", Size = 10 },
            new() { FullPath = "dir/sub/file2.txt", Size = 20 },
        };

        Assert.True(ArchiveStructureAnalyzer.HasSingleRootDirectory(items));
    }

    [Fact]
    public void HasSingleRootDirectory_MultipleRootEntries_ReturnsFalse()
    {
        var items = new List<ArchiveItem>
        {
            new() { FullPath = "file1.txt", Size = 10 },
            new() { FullPath = "dir/file2.txt", Size = 20 },
        };

        Assert.False(ArchiveStructureAnalyzer.HasSingleRootDirectory(items));
    }

    [Fact]
    public void HasSingleRootDirectory_EmptyArchive_ReturnsTrue()
    {
        var items = new List<ArchiveItem>();

        Assert.True(ArchiveStructureAnalyzer.HasSingleRootDirectory(items));
    }

    [Fact]
    public void HasSingleRootDirectory_SingleFileAtRoot_ReturnsFalse()
    {
        var items = new List<ArchiveItem>
        {
            new() { FullPath = "readme.txt", Size = 42 },
        };

        Assert.False(ArchiveStructureAnalyzer.HasSingleRootDirectory(items));
    }

    [Fact]
    public void HasSingleRootDirectory_MixedFileAndFolderAtRoot_ReturnsFalse()
    {
        var items = new List<ArchiveItem>
        {
            new() { FullPath = "file.txt", Size = 10 },
            new() { FullPath = "folder/file.txt", Size = 10 },
        };

        Assert.False(ArchiveStructureAnalyzer.HasSingleRootDirectory(items));
    }

    [Fact]
    public void HasSingleRootDirectory_OnlyDirectories_ReturnsTrue()
    {
        var items = new List<ArchiveItem>
        {
            new() { FullPath = "dir1/", IsDirectory = true, Size = 0 },
            new() { FullPath = "dir2/", IsDirectory = true, Size = 0 },
        };

        Assert.True(ArchiveStructureAnalyzer.HasSingleRootDirectory(items));
    }

    [Fact]
    public void HasSingleRootDirectory_SingleRootWithSubfolder_ReturnsTrue()
    {
        var items = new List<ArchiveItem>
        {
            new() { FullPath = "project/src/main.cs", Size = 100 },
            new() { FullPath = "project/README.md", Size = 50 },
            new() { FullPath = "project/.gitignore", Size = 20 },
        };

        Assert.True(ArchiveStructureAnalyzer.HasSingleRootDirectory(items));
    }

    [Fact]
    public void HasSingleRootDirectory_TwoRootDirectories_ReturnsFalse()
    {
        var items = new List<ArchiveItem>
        {
            new() { FullPath = "docs/intro.md", Size = 30 },
            new() { FullPath = "src/lib.rs", Size = 200 },
        };

        Assert.False(ArchiveStructureAnalyzer.HasSingleRootDirectory(items));
    }

    // ===== End-to-end with real ZIP archives =====

    [Fact]
    public async Task SingleRootZipArchive_HasSingleRootDirectory_ReturnsTrue()
    {
        // Create a ZIP with structure: rootdir/file.txt, rootdir/sub/other.txt
        var archive = TrackFile(CreateZipWithSingleRoot("rootdir"));
        var items = await _engine.ListEntriesAsync(archive);

        Assert.True(ArchiveStructureAnalyzer.HasSingleRootDirectory(items));
    }

    [Fact]
    public async Task MultiRootZipArchive_HasSingleRootDirectory_ReturnsFalse()
    {
        // Create a ZIP with structure: file.txt, subdir/other.txt
        var archive = TrackFile(CreateZipWithMultipleRoots());
        var items = await _engine.ListEntriesAsync(archive);

        Assert.False(ArchiveStructureAnalyzer.HasSingleRootDirectory(items));
    }

    /// <summary>
    /// 智能解压两档 × 单根包：加根档不动、去根档剥掉那一层。
    /// <para>
    /// 这里断言的是 <see cref="ArchiveRootPolicy"/> 产出的**指令**，不是把落点公式抄一遍再比
    /// ——旧用例就是那样写的，等于在测测试自己的算术（无论产品代码改不改都会绿）。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(RootDirectoryMode.SmartWrap, false)]
    [InlineData(RootDirectoryMode.SmartStrip, true)]
    public async Task SingleRootArchive_SmartExtract_ProducesExpectedInstruction(
        RootDirectoryMode mode, bool expectStrip)
    {
        var archive = TrackFile(CreateZipWithSingleRoot("mydata"));
        var items = await _engine.ListEntriesAsync(archive);

        var adjustment = ArchiveRootPolicy.Resolve(mode, ArchiveStructureAnalyzer.HasSingleRootDirectory(items));

        Assert.Equal(expectStrip, adjustment.Strip);
        Assert.False(adjustment.Wrap);   // 单根包永远不会被加根（只有一个源目录，套了就是两层）
    }

    /// <summary>
    /// 智能解压两档 × 多根包：加根档补一层、去根档什么都不做（= 原地平铺）。
    /// </summary>
    [Theory]
    [InlineData(RootDirectoryMode.SmartWrap, true)]
    [InlineData(RootDirectoryMode.SmartStrip, false)]
    public async Task MultiRootArchive_SmartExtract_ProducesExpectedInstruction(
        RootDirectoryMode mode, bool expectWrap)
    {
        var archive = TrackFile(CreateZipWithMultipleRoots());
        var items = await _engine.ListEntriesAsync(archive);

        var adjustment = ArchiveRootPolicy.Resolve(mode, ArchiveStructureAnalyzer.HasSingleRootDirectory(items));

        Assert.Equal(expectWrap, adjustment.Wrap);
        Assert.False(adjustment.Strip);  // 没有唯一根可剥
    }

    [Fact]
    public async Task EncryptedSingleRootZip_SmartDest_ReturnsParentDirWithPassword()
    {
        var archive = TrackFile(CreateEncryptedZipWithSingleRoot("secretdir", "opensesame"));
        var parentDir = Path.GetDirectoryName(archive)!;

        // List entries without password (SharpZipLib can enumerate without decrypting)
        var items = await _engine.ListEntriesAsync(archive);

        Assert.True(ArchiveStructureAnalyzer.HasSingleRootDirectory(items));

        var dest = ArchiveStructureAnalyzer.HasSingleRootDirectory(items)
            ? parentDir
            : Path.Combine(parentDir, Path.GetFileNameWithoutExtension(archive));

        Assert.Equal(parentDir, dest);

        // Verify extraction works with correct password
        var extractDest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(archive, extractDest, "opensesame");

        Assert.True(File.Exists(Path.Combine(extractDest, "secretdir", "secret.txt")));
    }

    // ===== Helpers =====

    private string CreateZipWithSingleRoot(string rootDir)
    {
        var path = Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.zip");
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        using var zipStream = new ZipOutputStream(fs);
        zipStream.SetLevel(9);

        var entry1 = new ZipEntry($"{rootDir}/file.txt");
        zipStream.PutNextEntry(entry1);
        var data1 = Encoding.UTF8.GetBytes("hello");
        zipStream.Write(data1, 0, data1.Length);
        zipStream.CloseEntry();

        var entry2 = new ZipEntry($"{rootDir}/sub/other.txt");
        zipStream.PutNextEntry(entry2);
        var data2 = Encoding.UTF8.GetBytes("world");
        zipStream.Write(data2, 0, data2.Length);
        zipStream.CloseEntry();

        return path;
    }

    private string CreateZipWithMultipleRoots()
    {
        var path = Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.zip");
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        using var zipStream = new ZipOutputStream(fs);
        zipStream.SetLevel(9);

        var entry1 = new ZipEntry("file.txt");
        zipStream.PutNextEntry(entry1);
        var data1 = Encoding.UTF8.GetBytes("root");
        zipStream.Write(data1, 0, data1.Length);
        zipStream.CloseEntry();

        var entry2 = new ZipEntry("subdir/other.txt");
        zipStream.PutNextEntry(entry2);
        var data2 = Encoding.UTF8.GetBytes("nested");
        zipStream.Write(data2, 0, data2.Length);
        zipStream.CloseEntry();

        return path;
    }

    private string CreateEncryptedZipWithSingleRoot(string rootDir, string password)
    {
        var path = Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.zip");
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        using var zipStream = new ZipOutputStream(fs);
        zipStream.SetLevel(9);
        zipStream.Password = password;

        var entry = new ZipEntry($"{rootDir}/secret.txt");
        entry.IsCrypted = true;
        zipStream.PutNextEntry(entry);
        var data = Encoding.UTF8.GetBytes("secret data");
        zipStream.Write(data, 0, data.Length);
        zipStream.CloseEntry();

        return path;
    }
}
