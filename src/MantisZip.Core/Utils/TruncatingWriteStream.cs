using System;
using System.IO;

namespace MantisZip.Core.Utils;

/// <summary>
/// 只保留前 <see cref="Capacity"/> 字节、其余**静默丢弃**的只写流。
///
/// <para>
/// 用途：从 7z / RAR / ISO 中读取 entry 的头部字节。SharpSevenZip 的
/// <c>ExtractFile(index, stream)</c> 没有「只解前 N 字节」的入口——它必须把条目数据
/// 完整交给目标流，固实（solid）压缩块更是无法中途取消。旧实现直接写进
/// <see cref="MemoryStream"/>，于是哪怕只要 4KB head，也会先把整条目（可能是几百 MB）
/// 解压进内存。把目标流换成这个类后，底层照样跑完，但内存只驻留 <see cref="Capacity"/> 量级。
/// </para>
/// <para>
/// 关键契约：**写满之后不抛异常**。上层解压器会把「流写失败」当成解压失败，
/// 抛异常来截断就等于让 entry 没能读完、拿不到确定的结果（甚至触发 CRC 报错），
/// 所以超额数据必须被安静吞掉。
/// </para>
/// </summary>
internal sealed class TruncatingWriteStream : Stream
{
    /// <summary>最多保留的字节数（= 调用方需要的 head 大小）。</summary>
    public int Capacity { get; }

    private readonly byte[] _buffer;
    private int _written;
    private long _discardedBytes;

    /// <summary>实际已保留的字节数。</summary>
    public int WrittenBytes => _written;

    /// <summary>被丢弃的字节数（仅用于日志/诊断，不影响数据）。</summary>
    public long DiscardedBytes => _discardedBytes;

    public TruncatingWriteStream(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Capacity = capacity;
        _buffer = capacity == 0 ? [] : new byte[capacity];
    }

    /// <summary>返回实际收到的前 <see cref="Capacity"/> 字节（副本，长度 = <see cref="WrittenBytes"/>）。</summary>
    public byte[] ToArray()
    {
        if (_written == 0)
            return [];

        var result = new byte[_written];
        Array.Copy(_buffer, result, _written);
        return result;
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException("TruncatingWriteStream 不支持 Length");

    public override long Position
    {
        get => throw new NotSupportedException("TruncatingWriteStream 不支持 Position");
        set => throw new NotSupportedException("TruncatingWriteStream 不支持 Position");
    }

    public override void Write(byte[] buffer, int offset, int count)
        => Write(new ReadOnlySpan<byte>(buffer, offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        int toKeep = Math.Min(buffer.Length, Capacity - _written);
        if (toKeep > 0)
        {
            buffer[..toKeep].CopyTo(_buffer.AsSpan(_written));
            _written += toKeep;
        }

        // 超出 capacity 的部分：安静丢弃（不抛异常，保证底层解压继续跑完）
        _discardedBytes += buffer.Length - toKeep;
    }

    public override void WriteByte(byte value)
    {
        if (_written < Capacity)
        {
            _buffer[_written++] = value;
        }
        else
        {
            _discardedBytes++;
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer, offset, count);
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override void Flush()
    {
        // 无缓冲，空实现
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("TruncatingWriteStream 是只写流");

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException("TruncatingWriteStream 是只写流，不支持定位");

    public override void SetLength(long value)
        => throw new NotSupportedException("TruncatingWriteStream 是只写流，不支持 SetLength");

    protected override void Dispose(bool disposing)
    {
        // 只持有托管缓冲区，无需释放资源
        base.Dispose(disposing);
    }
}
