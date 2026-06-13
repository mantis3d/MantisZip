using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// CSV 文件解析工具。支持引号包裹的字段和转义引号。
/// </summary>
public static class CsvParser
{
    /// <summary>
    /// 解析一行 CSV，处理引号包裹的字段和转义引号。
    /// </summary>
    public static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            if (line[i] == '"')
            {
                // 引号包裹的字段
                i++;
                var sb = new StringBuilder();
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        // 转义的引号 ""
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                        }
                        else
                        {
                            i++; // 跳过闭合引号
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(line[i]);
                        i++;
                    }
                }
                fields.Add(sb.ToString());
                // 跳过逗号
                if (i < line.Length && line[i] == ',')
                    i++;
            }
            else
            {
                // 普通字段（到逗号或行尾）
                var sb = new StringBuilder();
                while (i < line.Length && line[i] != ',')
                {
                    sb.Append(line[i]);
                    i++;
                }
                fields.Add(sb.ToString());
                if (i < line.Length && line[i] == ',')
                    i++;
            }
        }
        return fields.ToArray();
    }

    /// <summary>
    /// 确保列名合法且唯一：空值替换为"列N"，同名追加后缀。
    /// </summary>
    public static string[] MakeUniqueColumnNames(string[] rawHeaders)
    {
        var result = new string[rawHeaders.Length];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rawHeaders.Length; i++)
        {
            var name = string.IsNullOrEmpty(rawHeaders[i]) ? $"列{i + 1}" : rawHeaders[i];
            // 同名冲突 → 追加 _2, _3...
            if (used.Contains(name))
            {
                int suffix = 2;
                while (used.Contains($"{name}_{suffix}"))
                    suffix++;
                name = $"{name}_{suffix}";
            }
            used.Add(name);
            result[i] = name;
        }
        return result;
    }
}
