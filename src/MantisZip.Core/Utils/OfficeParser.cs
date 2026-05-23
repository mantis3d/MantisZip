using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace MantisZip.Core.Utils;

/// <summary>
/// Office 文档元数据解析器（.docx / .xlsx / .pptx）。
/// 这些格式本质上是 ZIP 包，内部含有 XML 元数据文件。
/// </summary>
public static class OfficeParser
{
    /// <summary>解析 Office 文档，返回格式信息；解析失败返回 null。</summary>
    public static FileFormatInfo? Parse(string filePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            string? title = null, author = null, subject = null;
            DateTime? created = null, modified = null;
            int pageCount = 0;

            // ── docProps/core.xml ──
            var coreEntry = archive.GetEntry("docProps/core.xml");
            if (coreEntry != null)
            {
                using var coreStream = coreEntry.Open();
                var coreDoc = XDocument.Load(coreStream);
                XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
                XNamespace dc = "http://purl.org/dc/elements/1.1/";
                XNamespace dcterms = "http://purl.org/dc/terms/";

                title = coreDoc.Root?.Element(dc + "title")?.Value;
                author = coreDoc.Root?.Element(dc + "creator")?.Value;
                subject = coreDoc.Root?.Element(dc + "subject")?.Value;

                var createdStr = coreDoc.Root?.Element(dcterms + "created")?.Value;
                if (DateTime.TryParse(createdStr, out var dt)) created = dt;

                var modifiedStr = coreDoc.Root?.Element(dcterms + "modified")?.Value ?? 
                                   coreDoc.Root?.Element(cp + "modified")?.Value;
                if (DateTime.TryParse(modifiedStr, out var dt2)) modified = dt2;
            }

            string formatName;
            switch (ext)
            {
                case ".docx":
                    formatName = "Word 文档";
                    pageCount = CountDocxContent(archive);
                    break;
                case ".xlsx":
                    formatName = "Excel 工作表";
                    pageCount = CountXlsxSheets(archive);
                    break;
                case ".pptx":
                    formatName = "PowerPoint 演示文稿";
                    pageCount = CountPptxSlides(archive);
                    break;
                default:
                    return null;
            }

            return new FileFormatInfo
            {
                Format = ext switch
                {
                    ".docx" => FileFormat.Docx,
                    ".xlsx" => FileFormat.Xlsx,
                    ".pptx" => FileFormat.Pptx,
                    _ => FileFormat.Unknown,
                },
                DisplayName = formatName,
                Extension = ext,
                FileSize = new FileInfo(filePath).Length,
                Title = title,
                Author = author,
                Subject = subject,
                PageCount = pageCount > 0 ? pageCount : null,
                CreationDate = created,
                ModifiedDate = modified,
            };
        }
        catch (Exception ex) { CoreLog.Info($"OfficeParser.Parse failed: {ex.Message}"); return null; }
    }

    /// <summary>粗略估计 docx 的页数：统计段落数 / 40（平均每页约 40 段）</summary>
    private static int CountDocxContent(ZipArchive archive)
    {
        var entry = archive.GetEntry("word/document.xml");
        if (entry == null) return 0;

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        int paraCount = doc.Descendants(w + "p").Count();
        return Math.Max(1, (int)Math.Ceiling(paraCount / 40.0));
    }

    /// <summary>统计 xlsx 的工作表数</summary>
    private static int CountXlsxSheets(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/workbook.xml");
        if (entry == null) return 0;

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return doc.Descendants(s + "sheet").Count();
    }

    /// <summary>统计 pptx 的幻灯片数</summary>
    private static int CountPptxSlides(ZipArchive archive)
    {
        int count = 0;
        // 遍历 ppt/slides/slide*.xml
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                count++;
        }
        return count;
    }
}
