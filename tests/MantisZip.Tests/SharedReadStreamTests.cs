using System;
using System.IO;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests;

/// <summary>
/// SharedReadStream —— 压缩源文件共享读的回归测试。
/// 模拟 Word/Excel 场景：编辑器以 FileAccess.ReadWrite + FileShare.Read 持有源文件
/// （允许别人读、禁止别人写删）。锁定两个契约：
/// 1. File.OpenRead（隐含 FileShare.Read）在此场景必然抛 IOException —— 即旧 bug 的根源；
/// 2. SharedReadStream.OpenRead（FileShare.ReadWrite|Delete）可正常读取完整内容。
/// </summary>
public class SharedReadStreamTests : IDisposable
{
    private readonly string _path;

    public SharedReadStreamTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"mz-shared-read-{Guid.NewGuid():N}.txt");
        File.WriteAllText(_path, "hello mantiszip");
    }

    [Fact]
    public void OpenRead_FileHeldWithWriteAccessAndShareRead_ReadsFullContent()
    {
        // 模拟 Word：读写权限持有，共享模式只允许别人读
        using var holder = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        using var stream = SharedReadStream.OpenRead(_path);
        using var reader = new StreamReader(stream);
        Assert.Equal("hello mantiszip", reader.ReadToEnd());
    }

    [Fact]
    public void FileOpenRead_SameScenario_ThrowsIOException()
    {
        using var holder = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        // 旧行为对照：File.OpenRead 隐含 FileShare.Read，与持有者的写权限冲突
        Assert.ThrowsAny<IOException>(() => File.OpenRead(_path));
    }

    public void Dispose() => TryDelete();

    private void TryDelete()
    {
        try { File.Delete(_path); } catch (IOException) { }
    }
}
