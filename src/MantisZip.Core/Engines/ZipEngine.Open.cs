using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SharpSevenZip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Readers;
using SharpCompress.Writers.Zip;

namespace MantisZip.Core.Engines;

/// <summary>
/// ZipEngine 的 打开与文件名编码探测部分（自 ZipEngine.cs 拆分，纯移动）。
/// </summary>
public partial class ZipEngine
{
    /// <summary>
    /// 静态初始化：注册 GBK（CP936）代码页提供程序。
    ///
    /// .NET Core+ 默认不含 GBK 等代码页，未注册时 <c>Encoding.GetEncoding("gbk")</c> 直接抛
    /// <c>ArgumentException: 'gbk' is not a supported encoding name</c>。本引擎有 3 处会用到 GBK
    /// （打开旧式中文 ZIP、文件名编码推断、中央目录重写），异常逃到 UI 就显示成
    /// 「操作失败：'gbk' is not a supported encoding name」这种天书，把「编码探测」误导成工具故障。
    ///
    /// 因此由 Core 自己负责（与 <c>TorrentParser</c> / <c>TextEncodingDetector</c> 一致），
    /// 不依赖宿主（App / 测试宿主 / 未来的 CLI）先注册过；注册幂等，重复调用无副作用。
    /// </summary>
    static ZipEngine()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// 使用 SharpCompress 打开 ZIP 文件，自动检测编码（UTF-8 → GBK 回退）。
    /// SharpCompress 每实例设置编码，无全局副作用。
    /// 
    /// 回退逻辑：只有 ZIP 条目未设置 UTF-8 标志（bit 11）且内容含高位字符时，
    /// 才尝试 GBK 回退。如果 bit 11 已设置，说明条目名是 UTF-8 编码，不进行回退。
    /// </summary>
    internal static IArchive OpenArchiveWithEncodingFallback(string archivePath, string? password = null)
    {
        // 使用 FileShare.Delete 允许在 archive 仍持有流时删除原文件
        var fs = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var options = new ReaderOptions { Password = password ?? string.Empty };

        IArchive OpenWithGbk()
        {
            fs.Dispose();
            var fs2 = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            // GBK 代码页已由本类静态构造器提前注册（见类顶部），此处直接取用
            var gbkOptions = new ReaderOptions
            {
                Password = password ?? string.Empty,
                ArchiveEncoding = new SharpCompress.Common.ArchiveEncoding
                {
                    Default = Encoding.GetEncoding("gbk")
                }
            };
            // 严格 ZIP 解析（不用 ArchiveFactory.OpenArchive 魔数嗅探——
            // 损坏/全零文件会被误判为 Tar 静默返回 0 条目，吞掉损坏信号）
            return ZipArchive.OpenArchive(fs2, gbkOptions);
        }

        IArchive archive;
        try
        {
            archive = ZipArchive.OpenArchive(fs, options);
        }
        catch (Exception ex)
        {
            CoreLog.Trace("OpenArchiveWithEncodingFallback: failed to open archive: {0}", ex.Message);
            fs.Dispose();
            throw;
        }

        try
        {
            var hasHighAscii = archive.Entries.Any(e =>
                !string.IsNullOrEmpty(e.Key) && e.Key.Any(c => c > 127));

            if (hasHighAscii)
            {
                // 检查 ZIP 中央目录中是否有条目设置了 UTF-8 标志（bit 11）。
                // 如果有，说明条目名本就是 UTF-8 编码，不应回退到 GBK。
                if (ZipHasUtf8Flag(archivePath))
                {
                    CoreLog.Info("OpenArchiveWithEncodingFallback: ZIP has UTF-8 flag (bit 11), keeping UTF-8 codec");
                    return archive;
                }

                // 即使没有 bit 11，如果解码后的文件名看起来像合法的 CJK 文本（而非
                // GBK→UTF-8 误解码的拉丁字符），也保留 UTF-8 解码结果。
                if (LooksLikeValidCjk(archive))
                {
                    CoreLog.Info("OpenArchiveWithEncodingFallback: decoded names appear as valid CJK, keeping UTF-8 codec");
                    return archive;
                }

                CoreLog.Info("OpenArchiveWithEncodingFallback: detected high-ASCII entry names without UTF-8 flag, retrying with GBK");
                archive.Dispose();
                return OpenWithGbk();
            }

            CoreLog.Info("OpenArchiveWithEncodingFallback: entries appear ASCII, keeping default codec");
            return archive;
        }
        catch (Exception ex)
        {
            CoreLog.Trace("OpenArchiveWithEncodingFallback: encoding detection failed, falling back to GBK: {0}", ex.Message);
            archive.Dispose();
            return OpenWithGbk();
        }
    }

    /// <summary>
    /// 解析条目文件名编码。<b>显式设置优先</b>——用户的「ZIP 文件名编码」设置必须被尊重，
    /// 不能被包内启发式覆盖（该缺陷曾导致拖拽添加中文文件时编码被猜错）。
    /// 仅当调用方未指定（null/空）时，才按包内是否已有条目置 UTF-8 标志（bit 11）来推断。
    /// </summary>
    private static Encoding ResolveFileNameEncoding(string? fileNameEncoding, string archivePath)
    {
        if (!string.IsNullOrWhiteSpace(fileNameEncoding))
        {
            var resolved = fileNameEncoding.ToLowerInvariant() switch
            {
                "gbk" => Encoding.GetEncoding("GBK"),
                "default" => Encoding.Default,
                _ => Encoding.UTF8,
            };
            CoreLog.Info($"AddToArchiveAsync: using explicit FileNameEncoding '{fileNameEncoding}' -> {resolved.WebName}");
            return resolved;
        }

        var inferred = ZipHasUtf8Flag(archivePath) ? Encoding.UTF8 : Encoding.GetEncoding("gbk");
        CoreLog.Info($"AddToArchiveAsync: FileNameEncoding not set, inferred from bit 11 -> {inferred.WebName}");
        return inferred;
    }

    /// <summary>
    /// 读取 ZIP 文件的中央目录，检查是否有任何条目设置了 UTF-8 文件名标志（通用位标志 bit 11 = 0x0800）。
    /// </summary>
    private static bool ZipHasUtf8Flag(string archivePath)
    {
        try
        {
            using var fs = File.OpenRead(archivePath);
            if (fs.Length < 22) return false;

            // 在文件末尾搜索 EOCD（End of Central Directory）签名 0x06054b50
            long eocdPos = -1;
            // EOCD 最小固定长度 22，最大长度 65557（含注释）
            long searchStart = Math.Max(0, fs.Length - 65557);
            byte[] sig = [0x50, 0x4b, 0x05, 0x06]; // little-endian 0x06054b50

            fs.Seek(searchStart, SeekOrigin.Begin);
            byte[] buf = new byte[fs.Length - searchStart];
            int read = fs.Read(buf, 0, buf.Length);

            for (int i = read - 22; i >= 0; i--)
            {
                if (buf[i] == sig[0] && buf[i + 1] == sig[1] &&
                    buf[i + 2] == sig[2] && buf[i + 3] == sig[3])
                {
                    eocdPos = searchStart + i;
                    break;
                }
            }

            if (eocdPos < 0) return false;

            // 解析 EOCD：偏移 16 处为中央目录偏移量（4 bytes）
            uint centralDirOffset = BitConverter.ToUInt32(buf, (int)(eocdPos - searchStart + 16));
            uint centralDirSize = BitConverter.ToUInt32(buf, (int)(eocdPos - searchStart + 12));

            // 遍历中央目录条目
            fs.Seek(centralDirOffset, SeekOrigin.Begin);
            var reader = new BinaryReader(fs, Encoding.UTF8);
            long endPos = centralDirOffset + centralDirSize;

            while (fs.Position < endPos)
            {
                uint sig2 = reader.ReadUInt32();
                if (sig2 != 0x02014b50) break;

                // 跳过版本信息（4 bytes）
                reader.ReadBytes(4);
                // 通用位标志（2 bytes），偏移 8
                ushort flags = reader.ReadUInt16();

                if ((flags & 0x0800) != 0)
                    return true;

                // 跳过剩余的固定头部到达可变长度字段
                // 已读：签名(4) + 版本(2+2) + 标志(2) = 10
                // 再跳：压缩方法(2) + 时间(2) + 日期(2) + CRC(4) + 压缩大小(4) + 未压缩大小(4) = 18
                reader.ReadBytes(18);
                ushort nameLen = reader.ReadUInt16();  // 偏移 28
                ushort extraLen = reader.ReadUInt16(); // 偏移 30
                ushort commentLen = reader.ReadUInt16(); // 偏移 32
                reader.ReadBytes(12); // 磁盘号(2) + 内部属性(2) + 外部属性(4) + 本地偏移(4) = 12

                // 跳过可变长度字段
                reader.ReadBytes(nameLen + extraLen + commentLen);
            }

            return false;
        }
        catch (Exception ex)
        {
            CoreLog.Trace("ZipHasUtf8Flag: failed to read central directory: {0}", ex.Message);
            // 出错时回退到旧行为（回退 GBK）
            return false;
        }
    }

    /// <summary>
    /// 检查已用 UTF-8 解码的条目名是否看起来像合法的 CJK 文本。
    /// GBK→UTF-8 误解码会产生拉丁字符（如 é ① À 等），而合法 CJK 在 U+4E00+ 范围。
    /// </summary>
    private static bool LooksLikeValidCjk(IArchive archive)
    {
        int totalHigh = 0;
        int cjkCount = 0;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Key)) continue;
            foreach (char c in entry.Key)
            {
                if (c <= 127) continue;
                totalHigh++;
                // CJK 及相关字符范围
                if ((c >= 0x4E00 && c <= 0x9FFF) ||      // CJK Unified Ideographs
                    (c >= 0x3400 && c <= 0x4DBF) ||      // CJK Extension A
                    (c >= 0x2B740 && c <= 0x2B81F) ||    // CJK Extension C
                    (c >= 0xF900 && c <= 0xFAFF) ||      // CJK Compatibility Ideographs
                    (c >= 0x3000 && c <= 0x303F) ||      // CJK Symbols and Punctuation
                    (c >= 0xFF00 && c <= 0xFFEF))        // Halfwidth and Fullwidth Forms
                {
                    cjkCount++;
                }
            }
        }

        return totalHigh > 0 && cjkCount >= totalHigh * 0.5;
    }

}
