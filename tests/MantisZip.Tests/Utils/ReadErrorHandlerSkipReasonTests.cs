using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// 预检剔除回调（spec §5.3）：不可读文件被 FilterUnreadableFiles 剔除时，
/// 必须通过 onSkipped 上报「文件 + 原因类别」，补上 7z/加密 ZIP 预检的静默缺口。
/// </summary>
public class ReadErrorHandlerSkipReasonTests
{
    [Fact]
    public void FilterUnreadableFiles_UnreadableFile_ReportsSkipWithReason()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mantiszip-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var lockedFile = Path.Combine(dir, "locked.bin");
        File.WriteAllBytes(lockedFile, new byte[16]);
        try
        {
            using var exclusive = new FileStream(lockedFile, FileMode.Open, FileAccess.Write, FileShare.None);
            var reported = new List<(string Path, EntryFailureReason Reason)>();
            var options = new ArchiveOptions { ErrorResolver = _ => FileErrorAction.Skip };

            var result = ReadErrorHandler.FilterUnreadableFiles(
                new[] { lockedFile }, options, CancellationToken.None,
                (path, reason) => reported.Add((path, reason)));

            Assert.Empty(result);
            var hit = Assert.Single(reported);
            Assert.Equal(lockedFile, hit.Path);
            Assert.Equal(EntryFailureReason.IoFailed, hit.Reason);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 临时目录尽力清理 */ }
        }
    }

    [Fact]
    public void FilterUnreadableFiles_ReadbleFile_NoCallback()
    {
        var file = Path.GetTempFileName();
        try
        {
            var reported = new List<(string Path, EntryFailureReason Reason)>();
            var result = ReadErrorHandler.FilterUnreadableFiles(
                new[] { file }, null, CancellationToken.None,
                (path, reason) => reported.Add((path, reason)));

            Assert.Single(result);
            Assert.Empty(reported);
        }
        finally { File.Delete(file); }
    }
}
