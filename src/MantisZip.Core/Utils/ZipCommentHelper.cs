using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// ZIP EOCD 注释原地读写工具。
/// 直接操作 ZIP 文件末尾的 End of Central Directory 注释字段，
/// 无需重新压缩整个压缩包。
/// </summary>
public static class ZipCommentHelper
{
    /// <summary>EOCD 签名</summary>
    private const uint EocdSignature = 0x06054b50;

    /// <summary>EOCD 固定字段大小（签名后到注释长度字段结束）</summary>
    private const int EocdFixedSize = 22;

    /// <summary>读取 ZIP 文件注释。无注释或非 ZIP 文件返回 null。</summary>
    public static string? ReadComment(string archivePath)
    {
        try
        {
            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ReadCommentFromStream(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CoreLog.Trace("ZipCommentHelper.ReadComment: IO error reading '{0}': {1}", archivePath, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 写入/更新 ZIP 文件注释。
    /// comment 为 null 或空字符串时清空注释。
    /// </summary>
    public static void WriteComment(string archivePath, string? comment)
    {
        var commentBytes = string.IsNullOrEmpty(comment)
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(comment);

        if (commentBytes.Length > ushort.MaxValue)
            throw new ArgumentException($"ZIP comment exceeds maximum length of {ushort.MaxValue} bytes (UTF-8).");

        // 打开文件流（读写）
        using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // 定位 EOCD 记录
        var eocdOffset = FindEocdOffset(fs);
        if (eocdOffset < 0)
            throw new InvalidDataException("ZIP EOCD signature not found. File may be corrupted or not a valid ZIP.");

        // 读取旧的注释长度，确定文件尾部结构
        fs.Seek(eocdOffset + 20, SeekOrigin.Begin);
        var oldCommentLenBytes = new byte[2];
        fs.ReadExactly(oldCommentLenBytes, 0, 2);
        var oldCommentLen = oldCommentLenBytes[0] | (oldCommentLenBytes[1] << 8);

        var oldTotalLen = EocdFixedSize + oldCommentLen;
        var newTotalLen = EocdFixedSize + commentBytes.Length;

        // 计算 EOCD 起始位置在文件中的偏移
        var eocdStartInFile = fs.Length - oldTotalLen;
        // 验证位置一致
        if (eocdStartInFile != eocdOffset)
        {
            // 容错：如果计算出来的位置不一致，以搜索到的为准
            CoreLog.Trace("ZipCommentHelper: EOCD position mismatch (expected {0}, actual {1}), using searched offset.",
                eocdStartInFile, eocdOffset);
        }

        if (commentBytes.Length <= oldCommentLen)
        {
            // 新注释不比旧的长 → 直接覆写，多余字节清空
            fs.Seek(eocdOffset + 20, SeekOrigin.Begin);

            // 写入注释长度
            fs.WriteByte((byte)(commentBytes.Length & 0xFF));
            fs.WriteByte((byte)((commentBytes.Length >> 8) & 0xFF));

            // 写入注释内容
            if (commentBytes.Length > 0)
                fs.Write(commentBytes, 0, commentBytes.Length);

            // 清空剩余旧注释字节（用空格填充）
            var padding = oldCommentLen - commentBytes.Length;
            for (int i = 0; i < padding; i++)
                fs.WriteByte(0x20); // 空格
        }
        else
        {
            // 新注释比旧的长 → 重写整个 EOCD 块
            // 先读取完整的 EOCD 记录（不含注释）
            fs.Seek(eocdOffset, SeekOrigin.Begin);
            var eocdHeader = new byte[EocdFixedSize];
            fs.ReadExactly(eocdHeader, 0, EocdFixedSize);

            // 写入新的注释长度
            eocdHeader[20] = (byte)(commentBytes.Length & 0xFF);
            eocdHeader[21] = (byte)((commentBytes.Length >> 8) & 0xFF);

            // 截断文件到 EOCD 起始位置
            fs.SetLength(eocdOffset);

            // 重写 EOCD 头 + 新注释
            fs.Seek(eocdOffset, SeekOrigin.Begin);
            fs.Write(eocdHeader, 0, EocdFixedSize);
            if (commentBytes.Length > 0)
                fs.Write(commentBytes, 0, commentBytes.Length);
        }
    }

    /// <summary>
    /// 从已打开的 FileStream 中读取 ZIP 注释。
    /// 不关闭流。
    /// </summary>
    private static string? ReadCommentFromStream(FileStream fs)
    {
        // 文件太小，不可能是有效的 ZIP
        if (fs.Length < EocdFixedSize)
            return null;

        var eocdOffset = FindEocdOffset(fs);
        if (eocdOffset < 0)
            return null;

        // 读取注释长度（偏移 20 处，2 字节小端）
        fs.Seek(eocdOffset + 20, SeekOrigin.Begin);
        var lenBytes = new byte[2];
        fs.ReadExactly(lenBytes, 0, 2);
        var commentLen = lenBytes[0] | (lenBytes[1] << 8);

        if (commentLen == 0)
            return null;

        // 读取注释内容
        var commentBytes = new byte[commentLen];
        fs.ReadExactly(commentBytes, 0, commentLen);

        // 移除尾部空格/填充
        var trimmed = TrimTrailingSpaces(commentBytes);
        if (trimmed.Length == 0)
            return null;

        return Encoding.UTF8.GetString(trimmed);
    }

    /// <summary>
    /// 从文件末尾向前搜索 EOCD 签名（取最后一个匹配）。
    /// 返回 EOCD 记录起始偏移，未找到返回 -1。
    /// </summary>
    private static long FindEocdOffset(FileStream fs)
    {
        var length = fs.Length;
        if (length < EocdFixedSize)
            return -1;

        // ZIP 规范：从文件末尾向前最多搜索 65557 + 22 字节
        // （EOCD 最大注释长度 65535 + 固定头 22）
        var searchStart = Math.Max(0, length - ushort.MaxValue - EocdFixedSize);
        var searchLen = (int)(length - searchStart);
        if (searchLen <= 0) return -1;

        fs.Seek(searchStart, SeekOrigin.Begin);
        var buffer = new byte[searchLen];
        fs.ReadExactly(buffer, 0, searchLen);

        // 从后向前找最后一个 EOCD 签名
        var sigBytes = new byte[] { 0x50, 0x4B, 0x05, 0x06 };
        for (var i = searchLen - 4; i >= 0; i--)
        {
            if (buffer[i] == 0x50 && buffer[i + 1] == 0x4B &&
                buffer[i + 2] == 0x05 && buffer[i + 3] == 0x06)
            {
                return searchStart + i;
            }
        }

        return -1;
    }

    /// <summary>移除字节数组尾部的空格（0x20）和空字符（0x00）</summary>
    private static byte[] TrimTrailingSpaces(byte[] bytes)
    {
        var end = bytes.Length;
        while (end > 0 && (bytes[end - 1] == 0x20 || bytes[end - 1] == 0x00))
            end--;

        if (end == bytes.Length)
            return bytes;

        var result = new byte[end];
        Array.Copy(bytes, result, end);
        return result;
    }
}
