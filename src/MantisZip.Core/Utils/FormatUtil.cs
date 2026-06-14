using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// 通用格式化工具：文件大小、Torrent 文件树渲染、字体加载失败原因等。
/// </summary>
public static class FormatUtil
{
    /// <summary>
    /// 将字节数格式化为人类可读的大小（B/KB/MB/GB/TB）。
    /// </summary>
    public static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// 构建 Torrent 文件树文本（含目录/文件层次结构树形符号）。
    /// </summary>
    /// <param name="info">已解析的 Torrent 文件信息。</param>
    /// <returns>树形文本，每行一个条目。</returns>
    public static string BuildTorrentFileTree(FileFormatInfo info)
    {
        var sb = new StringBuilder();
        if (info.TorrentFileEntries == null || info.TorrentFileEntries.Count == 0)
        {
            sb.AppendLine($"  ({info.TorrentFileName ?? "?"})");
            return sb.ToString();
        }

        var root = new Dictionary<string, object>();
        foreach (var (path, size) in info.TorrentFileEntries)
        {
            var parts = path.Split('/');
            var current = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!current.TryGetValue(parts[i], out var child))
                {
                    var sub = new Dictionary<string, object>();
                    current[parts[i]] = sub;
                    child = sub;
                }
                current = (Dictionary<string, object>)child!;
            }
            current[parts[^1]] = size;
        }

        RenderTree(sb, root, "");
        return sb.ToString();
    }

    private static void RenderTree(StringBuilder sb, Dictionary<string, object> node, string indent)
    {
        var items = node.ToArray();
        for (int i = 0; i < items.Length; i++)
        {
            bool isLast = i == items.Length - 1;
            string prefix = isLast ? "└── " : "├── ";
            string childIndent = isLast ? "    " : "│   ";
            if (items[i].Value is Dictionary<string, object> sub)
            {
                sb.AppendLine($"{indent}{prefix}[DIR] {items[i].Key}/");
                RenderTree(sb, sub, indent + childIndent);
            }
            else
            {
                sb.AppendLine($"{indent}{prefix}{items[i].Key}  ({FormatSize((long)items[i].Value)})");
            }
        }
    }

    /// <summary>
    /// 根据字体文件扩展名推断预览加载失败的原因。
    /// </summary>
    public static string GetFontLoadFailureReason(string fontFilePath)
    {
        string ext = System.IO.Path.GetExtension(fontFilePath)?.ToLowerInvariant() ?? "";
        return ext switch
        {
            ".otf" => "CFF 轮廓格式（OpenType）",
            ".woff" or ".woff2" => "Web 字体格式",
            _ => "WPF 字体系统限制",
        };
    }
}
