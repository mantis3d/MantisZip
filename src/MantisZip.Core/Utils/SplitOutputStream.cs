using System.IO;

namespace MantisZip.Core.Utils;

/// <summary>
/// 分卷输出流：将数据写入多个按序号命名的分卷文件。
/// 输出文件格式：{basename}.{ext}.001, {basename}.{ext}.002, ...
/// 单个文件达到 SplitSize 字节后自动切换到下一个分卷。
/// </summary>
internal class SplitOutputStream : Stream
{
    private readonly string _basePath;
    private readonly long _partSize;
    private int _partNumber;
    private FileStream? _currentPart;
    private long _currentPartLength;
    private bool _isDisposed;

    /// <summary>
    /// 创建分卷输出流。
    /// </summary>
    /// <param name="basePath">输出基准路径（如 "C:\out\archive.zip"）。实际文件名为 archive.zip.001, archive.zip.002 ...</param>
    /// <param name="partSize">每个分卷的最大字节数。</param>
    public SplitOutputStream(string basePath, long partSize)
    {
        _basePath = Path.GetFullPath(basePath);
        _partSize = partSize;
        _partNumber = 0;
        OpenNextPart();
    }

    private void OpenNextPart()
    {
        _currentPart?.Flush();
        _currentPart?.Dispose();
        _partNumber++;
        var partPath = GetPartPath(_partNumber);
        var dir = Path.GetDirectoryName(partPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        _currentPart = File.Create(partPath);
        _currentPartLength = 0;
    }

    private string GetPartPath(int partNumber)
    {
        var dir = Path.GetDirectoryName(_basePath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(_basePath);
        var ext = Path.GetExtension(_basePath);
        return Path.Combine(dir, $"{name}{ext}.{partNumber:D3}");
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            var space = _partSize - _currentPartLength;
            if (space <= 0)
            {
                OpenNextPart();
                space = _partSize;
            }
            var chunk = (int)Math.Min(count, space);
            _currentPart!.Write(buffer, offset, chunk);
            _currentPartLength += chunk;
            offset += chunk;
            count -= chunk;
        }
    }

    public override void Flush()
    {
        _currentPart?.Flush();
    }

    protected override void Dispose(bool disposing)
    {
        if (!_isDisposed && disposing)
        {
            _isDisposed = true;
            _currentPart?.Dispose();
            _currentPart = null;
        }
        base.Dispose(disposing);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
