using System.IO;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// IO 错误码 → 可操作提示映射测试（D5）。
/// 断言映射到正确的本地化 key（左右同经 T 求值，不依赖测试输出目录是否含本地化资源）。
/// </summary>
public class IoErrorHintResolverTests
{
    /// <summary>HResult 的 setter 为 protected，经派生类设置以构造带 Win32 码的 IOException。</summary>
    private sealed class HrIOException : IOException
    {
        public HrIOException(int hr) { HResult = hr; }
    }

    [Theory]
    [InlineData(112)]                                   // ERROR_DISK_FULL
    [InlineData(39)]                                    // ERROR_HANDLE_DISK_FULL
    [InlineData(unchecked((int)0x80070070))]            // 高位置位仍按低 16 位匹配
    public void FromHResult_DiskFull_ReturnsDiskFullKey(int hr)
        => Assert.Equal(LocalizationManager.T("Error_Hint_DiskFull"), IoErrorHintResolver.FromHResult(hr));

    [Theory]
    [InlineData(32)]                                    // ERROR_SHARING_VIOLATION
    [InlineData(33)]                                    // ERROR_LOCK_VIOLATION
    public void FromHResult_FileInUse_ReturnsFileInUseKey(int hr)
        => Assert.Equal(LocalizationManager.T("Error_Hint_FileInUse"), IoErrorHintResolver.FromHResult(hr));

    [Fact]
    public void FromHResult_PathTooLong_ReturnsPathTooLongKey()
        => Assert.Equal(LocalizationManager.T("Error_Hint_PathTooLong"), IoErrorHintResolver.FromHResult(206));

    [Fact]
    public void FromHResult_UnknownCode_ReturnsNull()
        => Assert.Null(IoErrorHintResolver.FromHResult(12345));

    [Fact]
    public void TryGetHint_IoExceptionWithDiskFull_ReturnsHint()
        => Assert.Equal(LocalizationManager.T("Error_Hint_DiskFull"),
            IoErrorHintResolver.TryGetHint(new HrIOException(unchecked((int)0x80070070))));

    [Fact]
    public void TryGetHint_NonIoException_ReturnsNull()
        => Assert.Null(IoErrorHintResolver.TryGetHint(new InvalidOperationException("boom")));

    [Fact]
    public void TryGetHint_Null_ReturnsNull()
        => Assert.Null(IoErrorHintResolver.TryGetHint(null));
}
