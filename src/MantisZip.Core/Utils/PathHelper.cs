using System.IO;

namespace MantisZip.Core.Utils;

/// <summary>
/// 文件路径工具方法
/// </summary>
public static class PathHelper
{
    /// <summary>
    /// 自动生成唯一的文件名：文件已存在时加 (1),(2)... 后缀。
    /// 正确处理 .tar.gz 等双扩展名。
    /// </summary>
    public static string GetUniquePath(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        string name, ext;

        // 特别处理 .tar.gz 双扩展名
        if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            var withoutExt = path[..^7]; // 去掉 .tar.gz
            name = Path.GetFileName(withoutExt);
            ext = ".tar.gz";
        }
        else
        {
            name = Path.GetFileNameWithoutExtension(path);
            ext = Path.GetExtension(path);
        }

        for (int i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
        return path; // 999 个名字全被占用了，直接覆盖原文件
    }
}
