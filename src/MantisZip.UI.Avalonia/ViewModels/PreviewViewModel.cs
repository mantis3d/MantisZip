using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
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

    /// <summary>
    /// 显示文本预览。
    /// </summary>
    public void ShowText(string filePath)
    {
        var content = DetectAndReadText(filePath);
        TextContent = content;
        PreviewType = PreviewType.Text;
        IsPreviewVisible = true;
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
        IsPreviewVisible = false;
    }

    /// <summary>
    /// 使用 Ude.NetStandard 检测文本编码并读取内容。
    /// 逻辑与 WPF 版的 DetectAndReadText 一致。
    /// </summary>
    private static string DetectAndReadText(string filePath)
    {
        // 读取文件头供编码检测
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

        // 置信度 >= 50% 且编码名有效
        if (confidence >= 0.5 && !string.IsNullOrEmpty(detected))
        {
            try
            {
                var enc = Encoding.GetEncoding(detected);
                return File.ReadAllText(filePath, enc);
            }
            catch
            {
                // 降级到回退逻辑
            }
        }

        // 回退：UTF-8 → 系统默认 ANSI
        try
        {
            var utf8 = File.ReadAllText(filePath, Encoding.UTF8);
            if (!utf8.Contains('\uFFFD'))
                return utf8;
        }
        catch
        {
            // 降级到 GBK
        }

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
