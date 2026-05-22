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

            var root = ParseDictionary(data, ref pos);
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
            string? name = info.TryGetValue("name", out var n) ? n as string : null;
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

                        if (fileDict.TryGetValue("path", out var pathObj) && pathObj is List<object> pathParts)
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
            string? comment = root.TryGetValue("comment", out var c) ? c as string : null;
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
        catch { return null; }
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

    private static Dictionary<string, object>? ParseDictionary(byte[] data, ref int pos)
    {
        if (data[pos] != (byte)'d') return null;
        pos++; // skip 'd'

        var dict = new Dictionary<string, object>();
        while (pos < data.Length && data[pos] != (byte)'e')
        {
            // Key
            if (data[pos] < (byte)'0' || data[pos] > (byte)'9') return null;
            string key = ParseString(data, ref pos);
            if (key == null) return null;

            // Value
            object? value = ParseValue(data, ref pos);
            if (value == null) return null;

            dict[key] = value;
        }
        if (pos < data.Length && data[pos] == (byte)'e')
            pos++; // skip 'e'

        return dict;
    }

    private static List<object>? ParseList(byte[] data, ref int pos)
    {
        if (data[pos] != (byte)'l') return null;
        pos++; // skip 'l'

        var list = new List<object>();
        while (pos < data.Length && data[pos] != (byte)'e')
        {
            object? value = ParseValue(data, ref pos);
            if (value == null) return null;
            list.Add(value);
        }
        if (pos < data.Length && data[pos] == (byte)'e')
            pos++; // skip 'e'

        return list;
    }

    private static object? ParseValue(byte[] data, ref int pos)
    {
        if (pos >= data.Length) return null;

        return data[pos] switch
        {
            (byte)'d' => ParseDictionary(data, ref pos),
            (byte)'l' => ParseList(data, ref pos),
            (byte)'i' => ParseInteger(data, ref pos),
            >= (byte)'0' and <= (byte)'9' => ParseString(data, ref pos),
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

    private static string ParseString(byte[] data, ref int pos)
    {
        int colonIdx = data.AsSpan(pos).IndexOf((byte)':');
        if (colonIdx <= 0) return "";

        string lenStr = Encoding.ASCII.GetString(data, pos, colonIdx);
        if (!int.TryParse(lenStr, out int len) || len < 0)
            return "";

        pos += colonIdx + 1;
        if (pos + len > data.Length)
            return "";

        // 种子文件字符串使用 UTF-8 编码（兼容纯 ASCII 键和中文文件名）
        string value = Encoding.UTF8.GetString(data, pos, len);
        pos += len;
        return value;
    }
}
