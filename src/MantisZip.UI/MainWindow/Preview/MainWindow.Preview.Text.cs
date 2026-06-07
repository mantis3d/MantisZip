using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using Ude;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

public partial class MainWindow
{
    // ═══════════════════════════════════════════
    //  字号（仅文本预览使用）
    // ═══════════════════════════════════════════

    private const int TextFontSizeStep = 2;
    private static readonly int[] TextFontSizes = { 8, 10, 12, 14, 16, 18, 20, 24, 28, 32, 36, 42, 48, 56, 64, 72 };

    private void ChangeTextFontSize(int delta)
    {
        int current = (int)PreviewTextBox.FontSize;
        int idx = Array.IndexOf(TextFontSizes, current);
        if (idx < 0) { idx = Array.FindIndex(TextFontSizes, s => s >= current); if (idx < 0) idx = TextFontSizes.Length - 1; }
        int newIdx = Math.Clamp(idx + delta, 0, TextFontSizes.Length - 1);
        if (newIdx != idx) PreviewTextBox.FontSize = TextFontSizes[newIdx];
    }

    // ── Text Encoding Detection ──

    /// <summary>
    /// 用 Ude.NetStandard 检测文件编码并读取全文。
    /// 支持 GBK、Shift-JIS、Big5、EUC-KR、UTF-8 等数十种编码。
    /// 置信度不足时退化为 UTF-8 → GBK 回退。
    /// </summary>
    private static string DetectAndReadText(string filePath)
    {
        // 读取文件头供编码检测（不需要全文）
        byte[] header;
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            int len = (int)Math.Min(fs.Length, 4096);
            header = new byte[len];
            fs.ReadExactly(header, 0, len);
        }

        var detector = new CharsetDetector();
        detector.Feed(header, 0, header.Length);
        detector.DataEnd();

        string detected = detector.Charset;
        double confidence = detector.Confidence;

        App.LogDebug("DetectAndReadText: detected={0}, confidence={1:P1}", detected, confidence);

        // 置信度 >= 50% 且编码名有效 → 用检测到的编码读取
        if (confidence >= 0.5 && !string.IsNullOrEmpty(detected))
        {
            try
            {
                // Ude 返回的编码名与 .NET 兼容（如 "GB-18030"、"Shift_JIS"）
                var enc = Encoding.GetEncoding(detected);
                return File.ReadAllText(filePath, enc);
            }
            catch (Exception ex)
            {
                App.LogDebug("DetectAndReadText: detected encoding {0} failed: {1}", detected, ex.Message);
                // 降级到回退逻辑
            }
        }

        // 回退：UTF-8 → 系统默认 ANSI 编码
        try
        {
            var utf8 = File.ReadAllText(filePath, Encoding.UTF8);
            if (!utf8.Contains('\uFFFD'))
                return utf8;
            App.LogDebug("DetectAndReadText: UTF8 fallback produced replacement chars, trying system default encoding");
        }
        catch (Exception utfEx)
        {
            App.LogDebug("DetectAndReadText: UTF8 fallback failed: {0}", utfEx.Message);
        }

        // 使用系统默认 ANSI 编码（中文=GBK，日文=Shift-JIS，等），不做硬编码假设
        var systemEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        return File.ReadAllText(filePath, systemEncoding);
    }

    // ── Text Preview ──

    private void ShowTextPreview(string filePath, string extension, ArchiveItem item)
    {
        try
        {
            // 用 Ude.NetStandard 检测文本编码（支持 GBK、Shift-JIS、Big5、EUC-KR 等数十种）
            string content = DetectAndReadText(filePath);

            PreviewTextBox.Text = content;
            PreviewTextBox.FontSize = AppSettings.Instance.TextPreviewFontSize;
            HideAllPreviewControls();
            PreviewTextBox.Visibility = Visibility.Visible;
            SetPreviewInfo(item, L.TF(L.Preview_TextInfo, content.Length));
            PreviewHeader.Text = L.TF(L.Preview_TextHeader, Path.GetFileName(filePath), content.Length);
            ShowPreviewPanel();

            SetToolbar(
                new[] {
                    new ToolbarButton { Text = "A−", Tooltip = L.T(L.Preview_FontDecrease), OnClick = () => ChangeTextFontSize(-TextFontSizeStep) },
                    new ToolbarButton { Text = "A+", Tooltip = L.T(L.Preview_FontIncrease), OnClick = () => ChangeTextFontSize(TextFontSizeStep) }
                },
                Array.Empty<ToolbarButton>()
            );
        }
        catch (Exception textEx)
        {
            App.LogDebug("ShowTextPreview: failed: {0}", textEx.Message);
            ShowUnsupportedPreview(null, L.T(L.Preview_TextFailed));
        }
    }

    // ── Table Preview ──

    /// <summary>
    /// 共享方法：用 DataGrid 展示表格数据（行列上限：100 行 × 100 列）。
    /// CSV / SQLite / Office 等格式的表格预览共用此方法。
    /// </summary>
    private void ShowTablePreview(System.Data.DataTable table, ArchiveItem item, string title, string? info = null)
    {
        PreviewCsvGrid.ItemsSource = table.DefaultView;
        HideAllPreviewControls();
        PreviewCsvGrid.Visibility = Visibility.Visible;
        SetPreviewInfo(item, info);
        PreviewHeader.Text = title;
        ShowPreviewPanel();
    }

    /// <summary>
    /// 共享方法：用 TabControl 展示多个表格（SQLite 多表）。
    /// 每个标签页内嵌一个 DataGrid。
    /// </summary>
    private void ShowMultiTablePreview(List<TableData> tables, ArchiveItem item, string title, string? info = null)
    {
        PreviewTabularContainer.Children.Clear();

        var tabControl = new TabControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        foreach (var table in tables)
        {
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = true,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = true,
                CanUserResizeColumns = true,
                CanUserSortColumns = true,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeaderWidth = 0,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontSize = 12,
                MinColumnWidth = 60,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ItemsSource = table.Data.DefaultView,
            };

            var tabItem = new TabItem
            {
                Header = table.Name,
                Content = dataGrid,
            };
            tabControl.Items.Add(tabItem);
        }

        PreviewTabularContainer.Children.Add(tabControl);

        HideAllPreviewControls();
        PreviewTabularContainer.Visibility = Visibility.Visible;
        SetPreviewInfo(item, info);
        PreviewHeader.Text = title;
        ShowPreviewPanel();
    }

    // ── CSV Preview ──

    private void ShowCsvPreview(string filePath, ArchiveItem item)
    {
        try
        {
            int maxRows = AppSettings.Instance.MaxTablePreviewRows;
            int maxCols = AppSettings.Instance.MaxTablePreviewCols;
            var lines = new List<string[]>();
            int totalRows = 0;
            string[] headers;

            using (var reader = new StreamReader(filePath, Encoding.UTF8))
            {
                // 读取首行作为列标题
                var headerLine = reader.ReadLine();
                if (headerLine == null)
                {
                    ShowUnsupportedPreview(item, "CSV 文件为空");
                    return;
                }

                headers = ParseCsvLine(headerLine);
                // 截断到 maxCols
                if (headers.Length > maxCols)
                    headers = headers.Take(maxCols).ToArray();
                // 空列名自动生成编号，保证唯一（避免 DataGrid 绑定失败）
                headers = MakeUniqueColumnNames(headers);
                lines.Add(headers);

                // 读取数据行（限制行数）
                string? dataLine;
                while ((dataLine = reader.ReadLine()) != null && lines.Count <= maxRows + 1)
                {
                    var fields = ParseCsvLine(dataLine);
                    if (fields.Length > maxCols)
                        fields = fields.Take(maxCols).ToArray();
                    lines.Add(fields);
                    totalRows++;
                }

                // 如果截断，继续数完以显示准确总数
                while (reader.ReadLine() != null)
                    totalRows++;
            }

            var colCount = headers.Length;
            int displayCount = lines.Count - 1;
            bool rowsTruncated = totalRows > displayCount;

            // 用 DataTable 填充（比 ExpandoObject 更可靠）
            var table = new System.Data.DataTable();
            foreach (var h in headers)
                table.Columns.Add(h, typeof(string));

            for (int i = 1; i < lines.Count; i++)
            {
                var row = lines[i];
                int shownCols = Math.Min(headers.Length, row.Length);
                var dataRow = table.NewRow();
                for (int col = 0; col < shownCols; col++)
                    dataRow[col] = row[col];
                table.Rows.Add(dataRow);
            }

            var info = rowsTruncated
                ? L.TF(L.Preview_CsvInfoTruncated, displayCount, colCount, totalRows)
                : L.TF(L.Preview_CsvInfo, displayCount, colCount);
            var title = L.TF(L.Preview_CsvHeader, Path.GetFileName(filePath), displayCount, colCount);
            ShowTablePreview(table, item, title, info);
        }
        catch (Exception csvEx)
        {
            App.LogDebug("ShowCsvPreview: failed: {0}", csvEx.Message);
            ShowUnsupportedPreview(null, L.T(L.Preview_CsvFailed));
        }
    }

    /// <summary>
    /// 确保列名合法且唯一：空值替换为"列N"，同名追加后缀。
    /// </summary>
    private static string[] MakeUniqueColumnNames(string[] rawHeaders)
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

    /// <summary>
    /// 解析一行 CSV，处理引号包裹的字段和转义引号。
    /// </summary>
    private static string[] ParseCsvLine(string line)
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
}
