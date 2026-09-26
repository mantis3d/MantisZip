using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// BitTorrent 种子文件解析器。
/// 解析 Bencode 编码的 .torrent 文件，提取元数据并计算 InfoHash / Magnet 链接。
/// </summary>
public static class TorrentParser
{
    /// <summary>
    /// 解析 .torrent 文件，返回格式信息；解析失败返回 null。
    /// </summary>
    public static FileFormatInfo? Parse(string filePath)
    {
        try
        {
            byte[] data = File.ReadAllBytes(filePath);
            int pos = 0;

            // 必须是 dictionary
            if (data[pos] != (byte)'d') return null;

            // ── 探测种子声明的字符串解码编码（encoding 字段）──
            // BitComet 1.x 等老工具生成 GBK 编码的中文种子（声明 encoding=GBK），
            // 若一律按 UTF-8 解码，普通 path/name 字段的中文会变成乱码。
            Encoding decodeEncoding = DetectDecodingEncoding(data);

            var root = ParseDictionary(data, ref pos, decodeEncoding);
            if (root == null) return null;

            // ── announce (tracker) ──
            string? announce = root.TryGetValue("announce", out var ann) ? ann as string : null;

            // ── announce-list (multi-tracker) ──
            int trackerCount = 0;
            if (root.TryGetValue("announce-list", out var annList) && annList is List<object> listOfLists)
            {
                trackerCount = listOfLists.Count;
            }
            else if (announce != null)
            {
                trackerCount = 1;
            }

            // ── info dict ──
            if (!root.TryGetValue("info", out var infoObj) || infoObj is not Dictionary<string, object> info)
                return null;

            // InfoHash: SHA1 of the bencoded info value
            // Scan from position 1 (after root 'd') to find "info" key's value position
            int infoValStart = FindKeyValue(data, "info", 1);
            if (infoValStart < 0) return null;

            // Find the matching 'e' that closes the info dict
            int infoValEnd = SkipBencodeValue(data, infoValStart);
            if (infoValEnd < 0) return null;

            byte[] infoRaw = data[infoValStart..infoValEnd];
            string infoHash;
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(infoRaw);
                infoHash = BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
            }

            // ── info 字段 ──
            // 优先读取 .utf-8 后缀字段（BEP 兼容惯例，值恒为 UTF-8，不受 encoding 字段影响）
            string? name = info.TryGetValue("name.utf-8", out var n8) && n8 is string n8s ? n8s
                : info.TryGetValue("name", out var n) ? n as string : null;
            long? pieceLength = info.TryGetValue("piece length", out var pl) ? (long?)Convert.ToInt64(pl) : null;

            // pieces hash (binary)
            int pieceCount = 0;
            if (info.TryGetValue("pieces", out var pObj))
            {
                if (pObj is byte[] pieces)
                    pieceCount = pieces.Length / 20;
                else if (pObj is string piecesStr)
                    pieceCount = piecesStr.Length / 20;
            }

            // files
            long totalSize = 0;
            int fileCount = 1;
            var fileEntries = new List<(string Path, long Size)>();
            if (info.TryGetValue("files", out var filesObj) && filesObj is List<object> files)
            {
                fileCount = files.Count;
                foreach (var fileObj in files)
                {
                    if (fileObj is Dictionary<string, object> fileDict)
                    {
                        long fileLen = fileDict.TryGetValue("length", out var len) ? Convert.ToInt64(len) : 0;
                        totalSize += fileLen;

                        // path.utf-8 优先（BitComet/qBittorrent 等新旧客户端皆提供，值恒为 UTF-8）
                        List<object>? pathParts = fileDict.TryGetValue("path.utf-8", out var pu) && pu is List<object> puList ? puList
                            : fileDict.TryGetValue("path", out var p) && p is List<object> pList ? pList : null;
                        if (pathParts != null)
                        {
                            var parts = pathParts.Select(p => p?.ToString() ?? "").ToArray();
                            fileEntries.Add((string.Join("/", parts), fileLen));
                        }
                    }
                }
            }
            else if (info.TryGetValue("length", out var singleLen))
            {
                totalSize = Convert.ToInt64(singleLen);
                if (name != null)
                    fileEntries.Add((name, totalSize));
            }

            // ── comment / created by ──
            string? comment = root.TryGetValue("comment.utf-8", out var c8) && c8 is string c8s ? c8s
                : root.TryGetValue("comment", out var c) ? c as string : null;
            string? createdBy = root.TryGetValue("created by", out var cb) ? cb as string : null;
            long? creationDate = root.TryGetValue("creation date", out var cd) ? (long?)Convert.ToInt64(cd) : null;

            // ── is private ──
            bool isPrivate = false;
            if (info.TryGetValue("private", out var priv))
                isPrivate = Convert.ToInt64(priv) == 1;

            // ── Magnet 链接 ──
            var magnet = new StringBuilder();
            magnet.Append("magnet:?xt=urn:btih:").Append(infoHash.ToLowerInvariant());
            if (!string.IsNullOrEmpty(name))
                magnet.Append("&dn=").Append(Uri.EscapeDataString(name));
            if (!string.IsNullOrEmpty(announce))
                magnet.Append("&tr=").Append(Uri.EscapeDataString(announce));

            string? trackerUrl = announce;
            if (string.IsNullOrEmpty(trackerUrl) && trackerCount > 0 && root.TryGetValue("announce-list", out var al) && al is List<object> alList && alList.Count > 0 && alList[0] is List<object> firstTier && firstTier.Count > 0)
                trackerUrl = firstTier[0] as string;

            return new FileFormatInfo
            {
                Format = FileFormat.Torrent,
                DisplayName = "BitTorrent 种子",
                Extension = Path.GetExtension(filePath),
                FileSize = new FileInfo(filePath).Length,
                TorrentFileName = name,
                TorrentTotalSize = totalSize,
                PieceSize = pieceLength,
                PieceCount = pieceCount,
                InfoHashV1 = infoHash,
                MagnetLink = magnet.ToString(),
                TrackerUrl = trackerUrl,
                TrackerCount = trackerCount,
                IsPrivate = isPrivate,
                CreatedBy = createdBy,
                FileCount = fileCount,
                CreationDate = creationDate.HasValue ? DateTimeOffset.FromUnixTimeSeconds(creationDate.Value).DateTime : null,
                AdditionalInfo = comment,
                TorrentFileEntries = fileEntries,
            };
        }
        catch (Exception ex) { CoreLog.Info($"TorrentParser.Parse failed: {ex.Message}"); return null; }
    }

    /// <summary>
    /// 在 Bencoded 字典的原始字节中查找指定 key 对应的 value 起始位置。
    /// 从 startPos 开始逐项扫描，跳过已解析的 key-value 对。
    /// </summary>
    private static int FindKeyValue(byte[] data, string key, int startPos)
    {
        int i = startPos;
        while (i < data.Length - 2)
        {
            // 字典结束
            if (data[i] == (byte)'e') return -1;

            // key 必须是 string，以数字开头
            if (data[i] < (byte)'0' || data[i] > (byte)'9')
            {
                i++;
                continue;
            }

            int colonIdx = data.AsSpan(i).IndexOf((byte)':');
            if (colonIdx <= 0) { i++; continue; }

            string lenStr = Encoding.ASCII.GetString(data, i, colonIdx);
            if (!int.TryParse(lenStr, out int keyLen) || keyLen < 0)
            {
                i += colonIdx + 1;
                continue;
            }

            int keyStart = i + colonIdx + 1;
            if (keyStart + keyLen > data.Length) return -1;

            string candidate = Encoding.ASCII.GetString(data, keyStart, keyLen);
            int valStart = keyStart + keyLen; // value 起始位置

            if (candidate == key)
                return valStart;

            // 跳过 value 继续扫描
            int next = SkipBencodeValue(data, valStart);
            if (next < 0) return -1;
            i = next;
        }
        return -1;
    }

    /// <summary>
    /// 从 pos 开始跳过任意一个 Bencoded value（string/int/list/dict），
    /// 返回 value 结束后的下一个位置。
    /// </summary>
    private static int SkipBencodeValue(byte[] data, int pos)
    {
        if (pos >= data.Length) return -1;

        byte b = data[pos];
        switch (b)
        {
            case (byte)'i': // integer: i<number>e
            {
                int end = data.AsSpan(pos).IndexOf((byte)'e');
                if (end < 0) return -1;
                return pos + end + 1;
            }
            case (byte)'d': // dictionary: d<items>e
            case (byte)'l': // list: l<items>e
            {
                int depth = 1;
                int i = pos + 1;
                while (i < data.Length && depth > 0)
                {
                    byte c = data[i];
                    if (c == (byte)'d' || c == (byte)'l') depth++;
                    else if (c == (byte)'e') depth--;
                    else if (c == (byte)'i')
                    {
                        i++;
                        while (i < data.Length && data[i] != (byte)'e') i++;
                    }
                    else if (c >= (byte)'0' && c <= (byte)'9')
                    {
                        int sc = data.AsSpan(i).IndexOf((byte)':');
                        if (sc > 0 && int.TryParse(Encoding.ASCII.GetString(data, i, sc), out int sLen) && sLen >= 0)
                            i += sc + sLen;
                    }
                    i++;
                }
                return depth == 0 ? i : -1;
            }
            default: // string
            {
                if (b < (byte)'0' || b > (byte)'9') return -1;
                int colonIdx = data.AsSpan(pos).IndexOf((byte)':');
                if (colonIdx <= 0) return -1;
                if (!int.TryParse(Encoding.ASCII.GetString(data, pos, colonIdx), out int len) || len < 0)
                    return -1;
                return pos + colonIdx + 1 + len;
            }
        }
    }

    private static Dictionary<string, object>? ParseDictionary(byte[] data, ref int pos, Encoding decodeEncoding)
    {
        if (data[pos] != (byte)'d') return null;
        pos++; // skip 'd'

        var dict = new Dictionary<string, object>();
        while (pos < data.Length && data[pos] != (byte)'e')
        {
            // Key
            if (data[pos] < (byte)'0' || data[pos] > (byte)'9') return null;
            // key 总是 ASCII（用 UTF-8 解码安全）
            string key = ParseString(data, ref pos, Encoding.UTF8);
            if (key == null) return null;

            // ── Value ──
            // 后缀 .utf-8 的字段值恒为 UTF-8（BEP 惯例，与 encoding 字段无关）；
            // 其余字符串按种子声明的编码（默认 UTF-8）解码。
            Encoding valueEncoding = key.EndsWith(".utf-8", StringComparison.Ordinal) ? Encoding.UTF8 : decodeEncoding;
            object? value = ParseValue(data, ref pos, valueEncoding);
            if (value == null) return null;

            dict[key] = value;
        }
        if (pos < data.Length && data[pos] == (byte)'e')
            pos++; // skip 'e'

        return dict;
    }

    private static List<object>? ParseList(byte[] data, ref int pos, Encoding decodeEncoding)
    {
        if (data[pos] != (byte)'l') return null;
        pos++; // skip 'l'

        var list = new List<object>();
        while (pos < data.Length && data[pos] != (byte)'e')
        {
            object? value = ParseValue(data, ref pos, decodeEncoding);
            if (value == null) return null;
            list.Add(value);
        }
        if (pos < data.Length && data[pos] == (byte)'e')
            pos++; // skip 'e'

        return list;
    }

    private static object? ParseValue(byte[] data, ref int pos, Encoding decodeEncoding)
    {
        if (pos >= data.Length) return null;

        return data[pos] switch
        {
            (byte)'d' => ParseDictionary(data, ref pos, decodeEncoding),
            (byte)'l' => ParseList(data, ref pos, decodeEncoding),
            (byte)'i' => ParseInteger(data, ref pos),
            >= (byte)'0' and <= (byte)'9' => ParseString(data, ref pos, decodeEncoding),
            _ => null,
        };
    }

    private static long ParseInteger(byte[] data, ref int pos)
    {
        if (data[pos] != (byte)'i') return 0;
        pos++; // skip 'i'

        int end = data.AsSpan(pos).IndexOf((byte)'e');
        if (end < 0) return 0;

        string numStr = Encoding.ASCII.GetString(data, pos, end);
        pos += end + 1; // skip past 'e'

        if (long.TryParse(numStr, out long result))
            return result;
        return 0;
    }

    private static string ParseString(byte[] data, ref int pos, Encoding decodeEncoding)
    {
        int colonIdx = data.AsSpan(pos).IndexOf((byte)':');
        if (colonIdx <= 0) return "";

        string lenStr = Encoding.ASCII.GetString(data, pos, colonIdx);
        if (!int.TryParse(lenStr, out int len) || len < 0)
            return "";

        pos += colonIdx + 1;
        if (pos + len > data.Length)
            return "";

        // 字符串按种子声明的编码解码（默认 UTF-8；GBK 种子中文文件名依赖此参数）
        string value = decodeEncoding.GetString(data, pos, len);
        pos += len;
        return value;
    }

    /// <summary>
    /// 读取 root dict 的 encoding 字段，决定普通字符串的解码编码（默认 UTF-8）。
    /// BitComet 1.x 等老工具生成的中文种子声明 encoding=GBK（BEP 3 允许非 UTF-8 编码），
    /// 此时若仍按 UTF-8 解码，path/name 等字段的中文会变成 U+FFFD 乱码。
    /// </summary>
    private static Encoding DetectDecodingEncoding(byte[] data)
    {
        int valStart = FindKeyValue(data, "encoding", 1);
        if (valStart < 0) return Encoding.UTF8;

        int colonIdx = data.AsSpan(valStart).IndexOf((byte)':');
        if (colonIdx <= 0) return Encoding.UTF8;
        if (!int.TryParse(Encoding.ASCII.GetString(data, valStart, colonIdx), out int len) || len <= 0 || len > 32)
            return Encoding.UTF8;

        int valPos = valStart + colonIdx + 1;
        if (valPos + len > data.Length) return Encoding.UTF8;

        string encodingName = Encoding.ASCII.GetString(data, valPos, len);
        string lower = encodingName.ToLowerInvariant();

        // UTF-8 及 ASCII 变体直接返回
        if (lower is "utf-8" or "utf8" or "utf" or "ascii") return Encoding.UTF8;

        try
        {
            // GBK/GB2312 等代码页需要 CodePagesEncodingProvider（注册幂等，可重复调用）
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(encodingName);
        }
        catch (Exception ex)
        {
            CoreLog.Info($"TorrentParser: unknown torrent encoding '{encodingName}', fallback to UTF-8: {ex.Message}");
            return Encoding.UTF8;
        }
    }
}
