using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MantisZip.Core.Utils;

/// <summary>
/// 包装一个只读 Stream，在每次 Read/ReadAsync 时通过回调报告已读取的字节数。
/// 用于 TAR 压缩、GZ 压缩等「引擎内部循环读取源文件」的场景，
/// 以便在压缩大文件时获得基于真实读取字节的进度更新（file-count 百分比无法表达单文件内进度）。
/// 仅支持读取，不支持写入/查找。
/// </summary>
internal sealed class ReadProgressStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<long> _onRead;
    private long _read;

    public ReadProgressStream(Stream inner, Action<long> onRead)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _onRead = onRead ?? throw new ArgumentNullException(nameof(onRead));
    }

    /// <summary>当前已读取的字节数（进度用；与 Position 独立，Seek 不改动本值）。</summary>
    public long BytesRead => _read;

    public override bool CanRead => true;
    // 代理内层的可寻性：TarWriter.Write 在未显式传 size 时会检查 source.CanSeek 后读 source.Length，
    // 因此包装流必须保持可寻（否则抛 "Seekable stream is required if no size is given."）。
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.CanSeek ? _inner.Position : _read;
        set => _inner.Position = value;
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = _inner.Read(buffer, offset, count);
        if (n > 0)
        {
            _read += n;
            _onRead(_read);
        }
        return n;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var n = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        if (n > 0)
        {
            _read += n;
            _onRead(_read);
        }
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin)
        => _inner.Seek(offset, origin);

    public override void SetLength(long value)
        => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
