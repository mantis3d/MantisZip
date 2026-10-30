using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// <see cref="TruncatingWriteStream"/> 单元测试：写满 capacity 后必须**静默丢弃**剩余字节、
/// 绝不抛异常（抛异常会让底层 7z 解压中断在半个条目上）。
/// </summary>
public class TruncatingWriteStreamTests
{
    [Fact]
    public void Write_WithinCapacity_KeepsAllBytes()
    {
        using var sink = new TruncatingWriteStream(16);

        sink.Write([1, 2, 3, 4], 0, 4);
        sink.WriteByte(5);

        Assert.Equal(5, sink.WrittenBytes);
        Assert.Equal(0L, sink.DiscardedBytes);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, sink.ToArray());
        Assert.True(sink.CanWrite);
        Assert.False(sink.CanRead);
        Assert.False(sink.CanSeek);
    }

    [Fact]
    public void Write_BeyondCapacity_KeepsFirstCapacityBytes_AndDiscardsRestWithoutThrowing()
    {
        using var sink = new TruncatingWriteStream(4);

        // 两次写：前 4 字节保留，其余丢弃；不抛异常
        sink.Write([1, 2, 3], 0, 3);
        sink.Write([4, 5, 6, 7], 0, 4);

        Assert.Equal(4, sink.WrittenBytes);
        // 第一写 3 字节全部保留；第二写的 4 字节只保留 1 字节，丢弃其余 3 字节
        Assert.Equal(3L, sink.DiscardedBytes);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, sink.ToArray());
    }

    [Fact]
    public void Write_WithOffset_OnlyCopiesRequestedSlice()
    {
        using var sink = new TruncatingWriteStream(8);
        byte[] buffer = [9, 9, 1, 2, 3, 9];

        sink.Write(buffer, offset: 2, count: 3);

        Assert.Equal(new byte[] { 1, 2, 3 }, sink.ToArray());
    }

    [Fact]
    public void Write_ZeroCapacity_DiscardsEverything()
    {
        using var sink = new TruncatingWriteStream(0);

        sink.Write([1, 2, 3], 0, 3);
        sink.WriteByte(4);

        Assert.Equal(0, sink.WrittenBytes);
        Assert.Equal(4L, sink.DiscardedBytes);
        Assert.Empty(sink.ToArray());
    }

    [Fact]
    public void ToArray_ReturnsIndependentCopy()
    {
        using var sink = new TruncatingWriteStream(8);
        sink.Write([1, 2], 0, 2);

        var first = sink.ToArray();
        first[0] = 99;

        Assert.Equal(new byte[] { 1, 2 }, sink.ToArray());
    }

    [Fact]
    public void UnsupportedMembers_ThrowNotSupported()
    {
        using var sink = new TruncatingWriteStream(8);

        Assert.Throws<NotSupportedException>(() => sink.Length);
        Assert.Throws<NotSupportedException>(() => sink.Position);
        Assert.Throws<NotSupportedException>(() => sink.Read([0], 0, 1));
        Assert.Throws<NotSupportedException>(() => sink.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => sink.SetLength(1));

        // Flush/Dispose 必须安静成功（SharpSevenZip 会调用 Flush）
        sink.Flush();
    }
}
