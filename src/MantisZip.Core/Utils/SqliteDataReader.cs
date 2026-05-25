using System.Data;
using Microsoft.Data.Sqlite;
using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// SQLite 数据库表格数据读取器。实现 <see cref="ITableDataProvider"/> 接口。
/// 用 Microsoft.Data.Sqlite 读取出数据库中每个用户表的行数据，返回 DataTable。
/// 将来可提取到独立类库 <c>MantisZip.Preview.Sqlite</c>。
/// </summary>
public class SqliteDataReader : ITableDataProvider
{
    public string FormatName => "SQLite";

    public IEnumerable<string> SupportedExtensions => new[] { ".db", ".sqlite", ".sqlite3", ".db3" };

    public async Task<TableQueryResult?> QueryAsync(string filePath, int maxRows = 100, int maxCols = 100, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var result = new TableQueryResult();
            var connString = new SqliteConnectionStringBuilder
            {
                DataSource = filePath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString();

            await using var conn = new SqliteConnection(connString);
            await conn.OpenAsync(ct);

            // 获取所有用户表名
            var tableNames = new List<string>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    tableNames.Add(reader.GetString(0));
            }

            foreach (var tableName in tableNames)
            {
                ct.ThrowIfCancellationRequested();

                var dt = new DataTable(tableName);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT * FROM \"{tableName.Replace("\"", "\"\"")}\" LIMIT {maxRows}";

                await using var reader = await cmd.ExecuteReaderAsync(ct);

                // 读取列信息（限制列数）
                var schemaTable = await reader.GetColumnSchemaAsync(ct);
                int colCount = Math.Min(schemaTable.Count, maxCols);
                for (int i = 0; i < colCount; i++)
                {
                    var colName = schemaTable[i].ColumnName ?? $"列{i + 1}";
                    // 处理重复列名
                    if (dt.Columns.Contains(colName))
                    {
                        int suffix = 2;
                        while (dt.Columns.Contains($"{colName}_{suffix}")) suffix++;
                        colName = $"{colName}_{suffix}";
                    }
                    dt.Columns.Add(colName, typeof(string));
                }

                // 读取行数据
                while (await reader.ReadAsync(ct))
                {
                    var row = dt.NewRow();
                    for (int i = 0; i < colCount; i++)
                    {
                        var val = reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString();
                        row[i] = val ?? "";
                    }
                    dt.Rows.Add(row);
                }

                result.Tables.Add(new TableData { Name = tableName, Data = dt });
            }

            return result;
        }
        catch (Exception ex)
        {
            CoreLog.Info($"SqliteDataReader.QueryAsync failed for {filePath}: {ex.Message}");
            return null;
        }
    }
}
