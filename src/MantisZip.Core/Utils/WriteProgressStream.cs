using System;
using System.IO;

namespace MantisZip.Core.Utils;

/// <summary>
/// 包装一个 Stream，在每次 Write 时通过回调报告已写入的字节数。
/// 用于 SharpSevenZipExtractor.ExtractFile 等一次性写入整个条目的场景，
/// 以便在解压大文件时获得逐块进度更新。
/// 仅支持写入，不支持读取/查找。
/// </summary>
internal sealed class WriteProgressStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<long> _onWrite;
    private long _written;

    public WriteProgressStream(Stream inner, Action<long> onWrite)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _onWrite = onWrite ?? throw new ArgumentNullException(nameof(onWrite));
    }

    /// <summary>当前已写入的字节数。</summary>
    public long BytesWritten => _written;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _written;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        _written += count;
        _onWrite(_written);
    }

    public override void WriteByte(byte value)
    {
        _inner.WriteByte(value);
        _written++;
        _onWrite(_written);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
