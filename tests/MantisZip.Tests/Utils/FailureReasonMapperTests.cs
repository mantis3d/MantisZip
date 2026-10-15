using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// 失败原因分类器（spec §5）：异常 → EntryFailureReason 三分类。
/// 同一异常类型在三引擎埋点与 ReadErrorHandler 预检的映射必须一致。
/// </summary>
public class FailureReasonMapperTests
{
    [Fact]
    public void UnauthorizedAccess_MapsToPermissionDenied()
        => Assert.Equal(EntryFailureReason.PermissionDenied,
            FailureReasonMapper.FromException(new UnauthorizedAccessException("denied")));

    [Fact]
    public void Io_MapsToIoFailed()
        => Assert.Equal(EntryFailureReason.IoFailed,
            FailureReasonMapper.FromException(new IOException("locked")));

    [Fact]
    public void Unknown_MapsToOther()
        => Assert.Equal(EntryFailureReason.Other,
            FailureReasonMapper.FromException(new InvalidOperationException("crc")));
}
