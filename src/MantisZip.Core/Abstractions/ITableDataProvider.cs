using System.Data;

namespace MantisZip.Core.Abstractions;

/// <summary>
/// 表格数据提供者接口。用于读取格式化的表格数据（SQLite、CSV、Excel 等）并返回统一的 DataTable。
/// 后续可作为插件模块提取到独立类库，见 <c>.sisyphus/plans/preview-modular-providers.md</c>。
/// </summary>
public interface ITableDataProvider
{
    /// <summary>格式名称，如 "SQLite"、"CSV"</summary>
    string FormatName { get; }

    /// <summary>支持的文件扩展名集合（含点，如 ".db", ".sqlite"）</summary>
    IEnumerable<string> SupportedExtensions { get; }

    /// <summary>
    /// 从文件流中读取表格数据。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="maxRows">每个表最多读取行数</param>
    /// <param name="maxCols">每个表最多读取列数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>读取结果，包含多个表；失败返回 null</returns>
    Task<TableQueryResult?> QueryAsync(string filePath, int maxRows = 100, int maxCols = 100, CancellationToken ct = default);
}

/// <summary>
/// 表格查询结果，包含一个或多个表（如 SQLite 数据库可能有多张表）。
/// </summary>
public class TableQueryResult
{
    public List<TableData> Tables { get; init; } = new();
}

/// <summary>
/// 单个表格数据。
/// </summary>
public class TableData
{
    /// <summary>表名</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>行列数据（最大 maxRows × maxCols）</summary>
    public DataTable Data { get; init; } = new();
}
