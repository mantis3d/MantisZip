using System.Text;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using SkiaSharp;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 预览「内存优先」接缝回归测试（切片 2）：<see cref="PreviewViewModel"/> 的字节版入口
/// 与取数层的内存/落盘分流必须严格对应，否则调用方会拿到「既无字节也无临时文件」的空结果。
///
/// <para>
/// 覆盖：图片字节版可解码 / 需真实路径的类型必须返回 <c>false</c> 让调用方回落、
/// 文本字节版与路径版等价、PDF 字节版能开文档并渲染第 1 页。
/// 图片解码用真实 Skia（<c>UseHeadlessDrawing = false</c>），故统一用 <c>[AvaloniaFact]</c>。
/// </para>
/// </summary>
public class PreviewMemoryWiringTests
{
    /// <summary>内存版 PNG → 返回 true，且真的把位图挂到了 PreviewImage 上。</summary>
    [AvaloniaFact]
    public void ShowImage_WithPngBytes_ReturnsTrueAndRenders()
    {
        var vm = new PreviewViewModel();
        var png = CreatePngBytes(8, 6);

        var handled = vm.ShowImage(png, "a.png");

        Assert.True(handled);
        Assert.NotNull(vm.PreviewImage);
        Assert.Equal(PreviewType.Image, vm.PreviewType);
        Assert.Equal(8, vm.ImageWidth);
        Assert.Equal(6, vm.ImageHeight);
    }

    /// <summary>需要真实路径的类型（.gif 走 GifDecoder path 版）→ 必须返回 false，调用方回落落盘。</summary>
    [AvaloniaFact]
    public void ShowImage_WithRealPathOnlyExtension_ReturnsFalse()
    {
        // 用一个 VM 连续跑完，顺带证明「拒绝」不依赖任何先前状态
        var vm = new PreviewViewModel();
        foreach (var entryName in new[] { "a.gif", "a.ico", "a.svg", "a.tga", "a.avif" })
        {
            var handled = vm.ShowImage(new byte[] { 1, 2, 3, 4 }, entryName);

            Assert.False(handled);
        }
    }

    /// <summary>文本字节版：不落盘也能正确解码（内容与路径版一致）。</summary>
    [AvaloniaFact]
    public void ShowText_WithUtf8Bytes_SetsTextContent()
    {
        var vm = new PreviewViewModel();
        // 纯 ASCII 载荷：避开编码探测的启发式分支，断言「字节 → 文本」这条链路本身
        var payload = "hello memory preview\nsecond line";

        vm.ShowText(Encoding.UTF8.GetBytes(payload), "a.txt");

        Assert.Equal(PreviewType.Text, vm.PreviewType);
        Assert.Equal(payload, vm.TextContent);
        Assert.True(vm.IsPreviewVisible);
    }

    /// <summary>CSV 字节版：能解析出行列（与路径版同一套 RebuildCsv 路径）。</summary>
    [AvaloniaFact]
    public void ShowCsv_WithUtf8Bytes_BuildsTable()
    {
        var vm = new PreviewViewModel();
        var payload = "name,size\nalpha,1\nbeta,2\n";

        vm.ShowCsv(Encoding.UTF8.GetBytes(payload), "a.csv");

        Assert.Equal(PreviewType.Csv, vm.PreviewType);
        Assert.NotNull(vm.CsvData);
        Assert.True(vm.CsvData!.Count >= 2);
    }

    /// <summary>Markdown 字节版：走同一套 AST → 控件树构建，类型落到 Markdown。</summary>
    [AvaloniaFact]
    public void ShowMarkdownPreview_WithUtf8Bytes_SetsMarkdownType()
    {
        var vm = new PreviewViewModel();

        vm.ShowMarkdownPreview(Encoding.UTF8.GetBytes("# 标题\n\n正文 **粗体**\n"), "a.md");

        Assert.Equal(PreviewType.Markdown, vm.PreviewType);
        Assert.True(vm.IsPreviewVisible);
    }

    /// <summary>
    /// PDF 字节版：用字节构造的最小 PDF 必须能打开并渲染第 1 页（页数 ≥ 1），
    /// 且 <c>_pdfDocument</c> 由 VM 自己持有（翻页不抛 ObjectDisposedException）。
    /// </summary>
    [AvaloniaFact]
    public async Task ShowPdfAsync_WithBytes_LoadsFirstPage()
    {
        var vm = new PreviewViewModel();
        var pdfBytes = CreateMinimalPdfBytes();

        await vm.ShowPdfAsync(pdfBytes, "doc.pdf");

        Assert.Equal(PreviewType.Pdf, vm.PreviewType);
        Assert.Equal(1, vm.PdfCurrentPage);
        Assert.StartsWith("1 / ", vm.PdfPageInfo);
        Assert.True(vm.IsPdfVisible);
    }

    // ── 测试辅助 ──

    /// <summary>现场用 Skia 编一张 PNG（不依赖仓库里的图片样本）。</summary>
    private static byte[] CreatePngBytes(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// 现场生成 1 页的最小 PDF（用 PdfPig 自己的 writer，避免手写 xref 偏移量出错）。
    /// </summary>
    private static byte[] CreateMinimalPdfBytes()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4, isPortrait: true);
        return builder.Build();
    }
}
