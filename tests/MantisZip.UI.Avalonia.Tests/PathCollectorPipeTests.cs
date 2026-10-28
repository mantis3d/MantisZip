using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// IPC 多实例路径收集的管道往返测试。
/// 覆盖：后到实例写入的路径被合并进首实例列表、重复路径不重复追加、
/// 不存在的路径被丢弃（避免把无效路径带进设置窗）。
/// </summary>
public sealed class PathCollectorPipeTests : IDisposable
{
    private readonly string _tempDir;

    public PathCollectorPipeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MantisZipPathCollectorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // 测试清理失败不影响结论
        }
    }

    [Fact]
    public void PipeServer_MergesNewPaths_AndIgnoresDuplicatesAndMissingPaths()
    {
        var firstArchive = CreateFile("first.zip");
        var secondArchive = CreateFile("second.zip");
        var missingArchive = Path.Combine(_tempDir, "not-there.zip");

        var pipeName = "MantisZipTestPipe_" + Guid.NewGuid().ToString("N");
        var allPaths = new List<string> { firstArchive };
        using var cts = new CancellationTokenSource();
        using var ready = new ManualResetEventSlim(false);

        try
        {
            App.StartPipeServer(allPaths, cts.Token, pipeName, ready);
            Assert.True(
                ready.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
                "管道服务器未在 5 秒内就绪");

            // 模拟两个后到实例：一个重复传首实例已有的包，一个带新包 + 一个不存在的路径
            App.SendPathsThroughPipe(new List<string> { firstArchive }, pipeName);
            App.SendPathsThroughPipe(new List<string> { secondArchive, missingArchive }, pipeName);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                lock (allPaths)
                {
                    if (allPaths.Count >= 2)
                        break;
                }

                Thread.Sleep(50);
            }

            lock (allPaths)
            {
                Assert.Equal(2, allPaths.Count);
                Assert.Contains(firstArchive, allPaths);
                Assert.Contains(secondArchive, allPaths);
                Assert.DoesNotContain(missingArchive, allPaths);
            }
        }
        finally
        {
            cts.Cancel();
        }
    }

    private string CreateFile(string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllBytes(path, new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        return path;
    }
}
