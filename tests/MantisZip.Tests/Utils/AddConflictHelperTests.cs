using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

public class AddConflictHelperTests
{
    private static HashSet<string> Occupied(params string[] names) =>
        new(names, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task ResolveEntryNameAsync_NoConflict_ReturnsSameName()
    {
        var occupied = Occupied("hello.txt");
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "new.txt", null, null, null, DateTime.Now, 100, occupied);
        Assert.Equal("new.txt", result);
        Assert.Contains("new.txt", occupied); // 最终名加入已占用集合
    }

    [Fact]
    public async Task ResolveEntryNameAsync_Overwrite_ReturnsSameName()
    {
        var occupied = Occupied("hello.txt");
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.Overwrite },
            DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Equal("hello.txt", result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_Skip_ReturnsNull()
    {
        var occupied = Occupied("hello.txt");
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.Skip },
            DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_Rename_ReturnsUniqueName()
    {
        var occupied = Occupied("hello.txt");
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.Rename },
            DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Equal("hello (1).txt", result);
        Assert.Contains("hello (1).txt", occupied);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_Rename_PreservesDirectoryPrefix()
    {
        var occupied = Occupied("docs/hello.txt");
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "docs/hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.Rename },
            DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Equal("docs/hello (1).txt", result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_OverwriteIfOlder_NewerDiskWins()
    {
        var occupied = Occupied("hello.txt");
        // 磁盘新文件比条目新 → 覆盖（添加场景方向）
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.OverwriteIfOlder },
            DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Equal("hello.txt", result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_OverwriteIfOlder_OlderDiskSkipped()
    {
        var occupied = Occupied("hello.txt");
        // 磁盘新文件比条目旧 → 跳过
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.OverwriteIfOlder },
            DateTime.Now, 10, DateTime.Now.AddDays(-1), 20, occupied);
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_OverwriteIfSmaller_LargerDiskWins()
    {
        var occupied = Occupied("hello.txt");
        // 磁盘新文件更大 → 覆盖（"覆盖较小"：大文件覆盖小条目）
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.OverwriteIfSmaller },
            DateTime.Now, 10, DateTime.Now, 20, occupied);
        Assert.Equal("hello.txt", result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_OverwriteIfSmaller_SmallerDiskSkipped()
    {
        var occupied = Occupied("hello.txt");
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", new ArchiveOptions { ConflictAction = FileConflictAction.OverwriteIfSmaller },
            DateTime.Now, 10, DateTime.Now, 5, occupied);
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_Ask_ResolverReturnsRenameWithCustomName()
    {
        var occupied = Occupied("hello.txt");
        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = info =>
            {
                info.CustomName = "renamed.txt";
                return Task.FromResult(FileConflictAction.Rename);
            },
        };
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", options, DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Equal("renamed.txt", result);
        Assert.Contains("renamed.txt", occupied);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_Ask_CustomNamePreservesDirectoryPrefix()
    {
        var occupied = Occupied("docs/hello.txt");
        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = info =>
            {
                info.CustomName = "renamed.txt";
                return Task.FromResult(FileConflictAction.Rename);
            },
        };
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "docs/hello.txt", options, DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Equal("docs/renamed.txt", result);
    }

    [Fact]
    public async Task ResolveEntryNameAsync_Ask_ResolverReturnsSkip()
    {
        var occupied = Occupied("hello.txt");
        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = _ => Task.FromResult(FileConflictAction.Skip),
        };
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", options, DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Null(result);
    }

    [Fact]
    public void GetUniqueEntryName_TarGz_DoubleExtension()
    {
        var occupied = Occupied("docs/archive.tar.gz");
        Assert.Equal("docs/archive (1).tar.gz", AddConflictHelper.GetUniqueEntryName("docs/archive.tar.gz", occupied));
    }

    [Fact]
    public void GetUniqueEntryName_Sequential()
    {
        var occupied = Occupied("file.txt", "file (1).txt");
        Assert.Equal("file (2).txt", AddConflictHelper.GetUniqueEntryName("file.txt", occupied));
    }

    [Fact]
    public async Task ResolveEntryNameAsync_Ask_CustomNameCollides_FallsBackToUniqueName()
    {
        var occupied = Occupied("hello.txt", "my-rename.txt");
        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = info =>
            {
                info.CustomName = "my-rename.txt"; // 与已有条目冲突
                return Task.FromResult(FileConflictAction.Rename);
            },
        };
        var result = await AddConflictHelper.ResolveEntryNameAsync(
            "hello.txt", options, DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Equal("my-rename (1).txt", result);
    }

    [Fact]
    public void ResolveEntryName_Ask_CustomNameCollides_FallsBackToUniqueName()
    {
        var occupied = Occupied("hello.txt", "my-rename.txt");
        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolver = info =>
            {
                info.CustomName = "my-rename.txt";
                return FileConflictAction.Rename;
            },
        };
        var result = AddConflictHelper.ResolveEntryName(
            "hello.txt", options, DateTime.Now.AddDays(-1), 10, DateTime.Now, 20, occupied);
        Assert.Equal("my-rename (1).txt", result);
    }

    [Fact]
    public void GetUniqueEntryName_AllNamesOccupied_FallsBackToUniqueGuidName()
    {
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "file.txt" };
        for (int i = 1; i < 1000; i++)
            occupied.Add($"file ({i}).txt");
        var result = AddConflictHelper.GetUniqueEntryName("file.txt", occupied);
        Assert.DoesNotContain(result, occupied);
    }
}