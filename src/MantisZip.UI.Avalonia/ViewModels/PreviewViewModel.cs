using System.Collections.ObjectModel;
using System.Data;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using Ude;

namespace MantisZip.UI.Avalonia.ViewModels;

public partial class PreviewViewModel : ObservableObject
{
    [ObservableProperty]
    private PreviewType _previewType = PreviewType.None;

    [ObservableProperty]
    private string _textContent = string.Empty;

    [ObservableProperty]
    private string _headerText = string.Empty;

    [ObservableProperty]
    private bool _isPreviewVisible;

    [ObservableProperty]
    private ObservableCollection<FormatMetadataItem> _formatMetadata = [];

    [ObservableProperty]
    private string _previewHeaderText = string.Empty;

    // ── Toolbar ──

    [ObservableProperty]
    private bool _isToolbarVisible;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private int _fontSize = 13;

    public bool HasZoomControls => PreviewType is PreviewType.Image or PreviewType.Gif;
    public bool HasFontSizeControls => PreviewType == PreviewType.Text;

    // Computed visibility per preview type
    public bool IsTextVisible => PreviewType == PreviewType.Text;
    public bool IsCsvVisible => PreviewType == PreviewType.Csv;
    public bool IsPeVisible => PreviewType == PreviewType.Pe;
    public bool IsUnsupportedVisible => PreviewType == PreviewType.Unsupported || PreviewType == PreviewType.None;

    public bool IsImageVisible => PreviewType == PreviewType.Image;
    public bool IsGifVisible => PreviewType == PreviewType.Gif;
    public bool IsSvgVisible => PreviewType == PreviewType.Svg;
    public bool IsFontVisible => PreviewType == PreviewType.Font;
    public bool IsAudioVisible => PreviewType == PreviewType.Audio;
    public bool IsSqliteVisible => PreviewType == PreviewType.Sqlite;
    public bool IsIsoVisible => PreviewType == PreviewType.Iso;
    public bool IsTorrentVisible => PreviewType == PreviewType.Torrent;
    public bool IsOfficeVisible => PreviewType == PreviewType.Office;
    public bool IsVideoVisible => PreviewType == PreviewType.Video;

    partial void OnPreviewTypeChanged(PreviewType value)
    {
        OnPropertyChanged(nameof(IsTextVisible));
        OnPropertyChanged(nameof(IsCsvVisible));
        OnPropertyChanged(nameof(IsPeVisible));
        OnPropertyChanged(nameof(IsImageVisible));
        OnPropertyChanged(nameof(IsGifVisible));
        OnPropertyChanged(nameof(IsSvgVisible));
        OnPropertyChanged(nameof(IsFontVisible));
        OnPropertyChanged(nameof(IsAudioVisible));
        OnPropertyChanged(nameof(IsSqliteVisible));
        OnPropertyChanged(nameof(IsIsoVisible));
        OnPropertyChanged(nameof(IsTorrentVisible));
        OnPropertyChanged(nameof(IsOfficeVisible));
        OnPropertyChanged(nameof(IsVideoVisible));
        OnPropertyChanged(nameof(IsUnsupportedVisible));
        OnPropertyChanged(nameof(HasZoomControls));
        OnPropertyChanged(nameof(HasFontSizeControls));
    }

    // ── CSV ──

    // DataView 实现了 IEnumerable，可绑定到 ItemsControl
    private DataTable? _csvDataTable;

    [ObservableProperty]
    private System.Data.DataView? _csvData;

    // ── PE ──

    [ObservableProperty]
    private string _peTitle = string.Empty;

    [ObservableProperty]
    private string _peSubtitle = string.Empty;

    public ObservableCollection<PeMetadataItem> PeMetadata { get; } = [];

    // ── Toolbar commands ──

    [RelayCommand]
    private void ZoomIn()
    {
        ZoomLevel = Math.Min(ZoomLevel + 0.25, 5.0);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        ZoomLevel = Math.Max(ZoomLevel - 0.25, 0.1);
    }

    [RelayCommand]
    private void ZoomFit()
    {
        ZoomLevel = 1.0;
    }

    [RelayCommand]
    private void IncreaseFontSize()
    {
        FontSize = Math.Min(FontSize + 2, 48);
    }

    [RelayCommand]
    private void DecreaseFontSize()
    {
        FontSize = Math.Max(FontSize - 2, 8);
    }

    /// <summary>
    /// 显示文本预览。
    /// </summary>
    public void ShowText(string filePath)
    {
        var content = DetectAndReadText(filePath);
        TextContent = content;
        PreviewType = PreviewType.Text;
        IsPreviewVisible = true;
        IsToolbarVisible = true;
    }

    /// <summary>
    /// 显示 CSV 表格预览。
    /// </summary>
    public void ShowCsv(string filePath)
    {
        var table = ParseCsv(filePath);
        _csvDataTable = table;
        CsvData = table?.DefaultView;  // DataView 可绑定到 ItemsControl
        PreviewType = PreviewType.Csv;
        IsPreviewVisible = true;
        IsToolbarVisible = true;
    }

    /// <summary>
    /// 显示 PE 元数据预览。
    /// </summary>
    public void ShowPe(string filePath)
    {
        var info = PeParser.Parse(filePath);
        if (info == null)
        {
            ShowUnsupported("无法解析 PE 文件");
            return;
        }

        PeTitle = info.ProductName ?? info.AdditionalInfo ?? Path.GetFileName(filePath);
        PeSubtitle = $"架构: {info.Architecture ?? "未知"} | 子系统: {info.Subsystem ?? "未知"}";
        PeMetadata.Clear();

        AddPeMeta("产品名称", info.ProductName);
        AddPeMeta("公司", info.CompanyName);
        AddPeMeta("文件版本", info.FileVersion);
        AddPeMeta("产品版本", info.ProductVersion);
        AddPeMeta("说明", info.AdditionalInfo);

        PreviewType = PreviewType.Pe;
        IsPreviewVisible = true;
        IsToolbarVisible = true;
    }

    /// <summary>
    /// 显示暂不支持预览提示。
    /// </summary>
    public void ShowUnsupported(string? message = null)
    {
        TextContent = message ?? "暂不支持预览此文件格式";
        PreviewType = PreviewType.Unsupported;
        IsPreviewVisible = true;
    }

    public void Clear()
    {
        PreviewType = PreviewType.None;
        TextContent = string.Empty;
        HeaderText = string.Empty;
        PeTitle = string.Empty;
        PeSubtitle = string.Empty;
        PeMetadata.Clear();
        CsvData = null;
        FormatMetadata.Clear();
        PreviewHeaderText = string.Empty;
        IsPreviewVisible = false;
        IsToolbarVisible = false;
        ZoomLevel = 1.0;
        FontSize = 13;
    }

    private void AddPeMeta(string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            PeMetadata.Add(new PeMetadataItem { Key = key, Value = value });
        }
    }

    /// <summary>
    /// 简单 CSV 解析：首行表头，逗号分隔，限制 100 行 × 100 列。
    /// </summary>
    private static DataTable ParseCsv(string filePath)
    {
        var table = new DataTable();
        var lines = File.ReadLines(filePath).Take(101).ToList();

        if (lines.Count == 0) return table;

        // 首行作为列名
        var headers = SplitCsvLine(lines[0]);
        foreach (var h in headers.Take(100))
        {
            table.Columns.Add(string.IsNullOrWhiteSpace(h) ? $"列{table.Columns.Count + 1}" : h.Trim());
        }

        // 数据行
        foreach (var line in lines.Skip(1).Take(100))
        {
            var values = SplitCsvLine(line);
            var row = table.NewRow();
            for (int i = 0; i < Math.Min(values.Count, table.Columns.Count); i++)
            {
                row[i] = values[i].Trim();
            }
            table.Rows.Add(row);
        }

        return table;
    }

    /// <summary>
    /// 简单 CSV 行解析（支持引号包裹的字段）。
    /// </summary>
    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    /// <summary>
    /// 使用 Ude.NetStandard 检测文本编码并读取内容。
    /// </summary>
    private static string DetectAndReadText(string filePath)
    {
        byte[] header;
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            var len = (int)Math.Min(fs.Length, 4096);
            header = new byte[len];
            fs.ReadExactly(header, 0, len);
        }

        var detector = new CharsetDetector();
        detector.Feed(header, 0, header.Length);
        detector.DataEnd();

        var detected = detector.Charset;
        var confidence = detector.Confidence;

        if (confidence >= 0.5 && !string.IsNullOrEmpty(detected))
        {
            try
            {
                var enc = Encoding.GetEncoding(detected);
                return File.ReadAllText(filePath, enc);
            }
            catch { }
        }

        try
        {
            var utf8 = File.ReadAllText(filePath, Encoding.UTF8);
            if (!utf8.Contains('\uFFFD'))
                return utf8;
        }
        catch { }

        try
        {
            return File.ReadAllText(filePath, Encoding.GetEncoding("gbk"));
        }
        catch
        {
            return File.ReadAllText(filePath, Encoding.UTF8);
        }
    }
}

/// <summary>
/// PE 元数据的键值对模型。
/// </summary>
public class PeMetadataItem
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
