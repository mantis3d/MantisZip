using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using Markdig;
using Ude;
using WpfAnimatedGif;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

public partial class MainWindow
{
    #region 文件预览

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".webp"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".ini", ".cfg", ".conf", ".csv", ".xml", ".json",
        ".cs", ".csproj", ".yaml", ".yml", ".toml",
        ".sh", ".bat", ".cmd", ".ps1", ".py", ".js", ".ts", ".tsx",
        ".css", ".scss", ".less",
        ".sql", ".gitignore", ".editorconfig", ".sln", ".props", ".targets",
        ".ruleset", ".rc", ".resx", ".nuspec", ".gradle", ".dockerfile",
        ".env", ".yml", ".yaml", ".json5", ".h", ".c", ".cpp", ".hpp",
        ".swift", ".kt", ".java", ".rb", ".go", ".rs", ".php", ".vue"
    };

    private static readonly HashSet<string> HtmlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm"
    };

    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown"
    };

    private async Task ShowPreviewAsync(ArchiveItem item)
    {
        // 取消上一次正在进行的预览（避免旧预览完成后覆盖新内容）
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        try
        {
            // 清理上次预览
            ClearPreviewTemp();
            ClearPreviewContent();

            var s = AppSettings.Instance;
            var ext = Path.GetExtension(item.Name);

            // 通用文件大小上限检查
            if (item.Size > s.MaxPreviewFileSize)
            {
                var limitMb = s.MaxPreviewFileSize / (1024.0 * 1024.0);
                ShowUnsupportedPreview(item, L.TF(L.Preview_TooLarge, (double)item.Size / 1024 / 1024, limitMb));
                return;
            }

            // 先L.T(L.Pwd_ShowBtn)基本信息（文件名、L.T(L.Main_Col_Size)、L.T(L.Shell_Compress)率、日期），再异步加载内容
            ShowPreviewPanel();
            SetPreviewInfo(item);
            ShowPreviewLoading(item.NameDisplay ?? item.Name);

            if (ImageExtensions.Contains(ext))
            {
                if (!s.EnableImagePreview)
                {
                    HidePreviewLoading();
                    ShowUnsupportedPreview(item, L.T(L.Preview_ImageDisabled));
                    return;
                }

                _previewTempDir = Path.Combine(Path.GetTempPath(), L.T(L.App_MantisZipTitle), Guid.NewGuid().ToString());
                Directory.CreateDirectory(_previewTempDir);
                var tempFile = Path.Combine(_previewTempDir, Path.GetFileName(item.Name) ?? "preview" + ext);

                await Core.Utils.ArchiveEntryExtractor.ExtractEntryAsync(
                    _currentArchivePath!, item.Name, tempFile, _currentFormat, _currentPassword, ct);
                ct.ThrowIfCancellationRequested();

                await ShowImagePreviewAsync(tempFile, item);
            }
            else if (HtmlExtensions.Contains(ext) || MarkdownExtensions.Contains(ext))
            {
                if (!s.EnableTextPreview)
                {
                    ShowUnsupportedPreview(item, L.T(L.Preview_TextDisabled));
                    return;
                }

                // 检查文件大小
                if (item.Size > s.MaxTextPreviewBytes)
                {
                    var limitMb = s.MaxTextPreviewBytes / (1024.0 * 1024.0);
                    ShowUnsupportedPreview(item, L.TF(L.Preview_TooLargeText, (double)item.Size / 1024 / 1024, limitMb));
                    return;
                }

                _previewTempDir = Path.Combine(Path.GetTempPath(), L.T(L.App_MantisZipTitle), Guid.NewGuid().ToString());
                Directory.CreateDirectory(_previewTempDir);
                var tempFile = Path.Combine(_previewTempDir, Path.GetFileName(item.Name) ?? "preview.html");

                await Core.Utils.ArchiveEntryExtractor.ExtractEntryAsync(
                    _currentArchivePath!, item.Name, tempFile, _currentFormat, _currentPassword, ct);
                ct.ThrowIfCancellationRequested();

                if (MarkdownExtensions.Contains(ext))
                    ShowMarkdownPreview(tempFile, item);
                else
                    ShowHtmlPreview(tempFile, item);
            }
            else if (TextExtensions.Contains(ext))
            {
                if (!s.EnableTextPreview)
                {
                    ShowUnsupportedPreview(item, L.T(L.Preview_TextDisabled));
                    return;
                }

                // 检查文件大小
                if (item.Size > s.MaxTextPreviewBytes)
                {
                    var limitMb = s.MaxTextPreviewBytes / (1024.0 * 1024.0);
                    ShowUnsupportedPreview(item, L.TF(L.Preview_TooLargeText, (double)item.Size / 1024 / 1024, limitMb));
                    return;
                }

                _previewTempDir = Path.Combine(Path.GetTempPath(), L.T(L.App_MantisZipTitle), Guid.NewGuid().ToString());
                Directory.CreateDirectory(_previewTempDir);
                var tempFile = Path.Combine(_previewTempDir, Path.GetFileName(item.Name) ?? "preview.txt");

                await Core.Utils.ArchiveEntryExtractor.ExtractEntryAsync(
                    _currentArchivePath!, item.Name, tempFile, _currentFormat, _currentPassword, ct);
                ct.ThrowIfCancellationRequested();

                ShowTextPreview(tempFile, ext, item);
            }
            else
            {
                ShowUnsupportedPreview(item);
            }
        }
        catch (OperationCanceledException)
        {
            // 用户切换文件导致旧预览被取消，静默忽略
        }
        catch (Exception ex)
        {
            ShowUnsupportedPreview(item, L.TF(L.Preview_Failed, ex.Message));
        }
        finally
        {
            HidePreviewLoading();
        }
    }

    /// <summary>
    /// 异步加载并显示图片预览。在后台线程解码以避免卡 UI，
    /// 并限制解码尺寸 (DecodePixelWidth=1920) 减少内存开销。
    /// </summary>
    private async Task ShowImagePreviewAsync(string filePath, ArchiveItem item)
    {
        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            // GIF — 用 WpfAnimatedGif 播放动画
            if (ext == ".gif")
            {
                int gifWidth = 0, gifHeight = 0;
                await Task.Run(() =>
                {
                    var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                        new Uri(filePath),
                        System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                        System.Windows.Media.Imaging.BitmapCacheOption.None);
                    gifWidth = decoder.Frames[0].PixelWidth;
                    gifHeight = decoder.Frames[0].PixelHeight;
                });

                var gifBitmap = new System.Windows.Media.Imaging.BitmapImage(new Uri(filePath));
                ImageBehavior.SetAnimatedSource(PreviewImage, gifBitmap);
                PreviewImage.MaxWidth = gifWidth;
                PreviewImage.MaxHeight = gifHeight;
                PreviewImage.Visibility = Visibility.Visible;
                PreviewTextBox.Visibility = Visibility.Collapsed;
                PreviewFileIcon.Visibility = Visibility.Collapsed;
                PreviewUnsupported.Visibility = Visibility.Collapsed;
                PreviewHeader.Text = L.TF(L.Preview_ImageHeader, Path.GetFileName(filePath));
                SetPreviewInfo(item, L.TF(L.Preview_Dimensions, gifWidth, gifHeight));
                ShowPreviewPanel();
                return;
            }

            // 普通图片 — 后台线程解码，不阻塞 UI
            var bitmap = await Task.Run(() =>
            {
                // 先获取实际尺寸，仅对超过 1920px 的图做降采样
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                    new Uri(filePath),
                    System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                    System.Windows.Media.Imaging.BitmapCacheOption.None);
                int actualWidth = decoder.Frames[0].PixelWidth;

                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(filePath);
                // 只有大图才降采样，小图保持原生清晰度
                if (actualWidth > 1920)
                    bmp.DecodePixelWidth = 1920;
                bmp.EndInit();
                bmp.Freeze(); // 跨线程安全
                return bmp;
            });

            PreviewImage.Source = bitmap;
            PreviewImage.MaxWidth = bitmap.PixelWidth;
            PreviewImage.MaxHeight = bitmap.PixelHeight;
            PreviewImage.Visibility = Visibility.Visible;
            PreviewTextBox.Visibility = Visibility.Collapsed;
            PreviewFileIcon.Visibility = Visibility.Collapsed;
            PreviewUnsupported.Visibility = Visibility.Collapsed;
            PreviewHeader.Text = L.TF(L.Preview_ImageHeader, Path.GetFileName(filePath));

            // 图片信息
            SetPreviewInfo(item, L.TF(L.Preview_Dimensions, bitmap.PixelWidth, bitmap.PixelHeight));

            ShowPreviewPanel();
        }
        catch (Exception imgEx)
        {
            App.LogDebug("ShowImagePreviewAsync: failed: {0}", imgEx.Message);
            ShowUnsupportedPreview(null, L.T(L.Preview_ImageFailed));
        }
    }

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

    private void ShowTextPreview(string filePath, string extension, ArchiveItem item)
    {
        try
        {
            // 用 Ude.NetStandard 检测文本编码（支持 GBK、Shift-JIS、Big5、EUC-KR 等数十种）
            string content = DetectAndReadText(filePath);

            PreviewTextBox.Text = content;
            PreviewTextBox.FontSize = AppSettings.Instance.TextPreviewFontSize;
            PreviewTextBox.Visibility = Visibility.Visible;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewFileIcon.Visibility = Visibility.Collapsed;
            PreviewUnsupported.Visibility = Visibility.Collapsed;
            SetPreviewInfo(item, L.TF(L.Preview_TextInfo, content.Length));
            PreviewHeader.Text = L.TF(L.Preview_TextHeader, Path.GetFileName(filePath), content.Length);
            ShowPreviewPanel();
        }
        catch (Exception textEx)
        {
            App.LogDebug("ShowTextPreview: failed: {0}", textEx.Message);
            ShowUnsupportedPreview(null, L.T(L.Preview_TextFailed));
        }
    }

    private void ShowHtmlPreview(string filePath, ArchiveItem item)
    {
        // WebBrowser 需要绝对路径或 URL
        PreviewWebBrowser.Navigate(new Uri(filePath));
        PreviewWebBrowser.Visibility = Visibility.Visible;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewTextBox.Visibility = Visibility.Collapsed;
        PreviewFileIcon.Visibility = Visibility.Collapsed;
        PreviewUnsupported.Visibility = Visibility.Collapsed;
        SetPreviewInfo(item, L.T(L.Preview_HtmlInfo));
        PreviewHeader.Text = L.TF(L.Preview_HtmlHeader, Path.GetFileName(filePath));
        ShowPreviewPanel();
    }

    private void ShowMarkdownPreview(string filePath, ArchiveItem item)
    {
        try
        {
            var mdContent = File.ReadAllText(filePath);
            var pipeline = new MarkdownPipelineBuilder()
                .UsePipeTables()
                .UseEmphasisExtras()
                .UseTaskLists()
                .UseAutoIdentifiers()
                .UseEmojiAndSmiley()
                .Build();
            var html = Markdig.Markdown.ToHtml(mdContent, pipeline);
            // 包裹基本样式以便阅读
            var styledHtml = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'/>
<style>
  body {{ font-family: system-ui, sans-serif; font-size: 14px; line-height: 1.6; padding: 16px; color: #222; }}
  pre {{ background: #f4f4f4; padding: 12px; border-radius: 4px; overflow-x: auto; }}
  code {{ background: #f0f0f0; padding: 2px 4px; border-radius: 2px; font-family: Consolas, monospace; }}
  pre code {{ background: none; padding: 0; }}
  img {{ max-width: 100%; }}
  table {{ border-collapse: collapse; }}
  td, th {{ border: 1px solid #ccc; padding: 6px 10px; }}
</style></head>
<body>{html}</body></html>";

            var tempHtml = Path.Combine(Path.GetDirectoryName(filePath) ?? _previewTempDir!, "markdown_preview.html");
            File.WriteAllText(tempHtml, styledHtml);
            PreviewWebBrowser.Navigate(new Uri(tempHtml));
            PreviewWebBrowser.Visibility = Visibility.Visible;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewTextBox.Visibility = Visibility.Collapsed;
            PreviewFileIcon.Visibility = Visibility.Collapsed;
            PreviewUnsupported.Visibility = Visibility.Collapsed;
            SetPreviewInfo(item, "📝 Markdown");
            PreviewHeader.Text = L.TF(L.Preview_MarkdownHeader, Path.GetFileName(filePath));
            ShowPreviewPanel();
        }
        catch (Exception mdEx)
        {
            App.LogDebug("ShowMarkdownPreview: failed: {0}", mdEx.Message);
            ShowUnsupportedPreview(null, "无法解析 Markdown 文件");
        }
    }

    private void ShowUnsupportedPreview(ArchiveItem? item, string? message = null)
    {
        PreviewUnsupported.Visibility = Visibility.Collapsed; // hide text fallback
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewTextBox.Visibility = Visibility.Collapsed;
        PreviewWebBrowser.Visibility = Visibility.Collapsed;
        PreviewInfoPanel.Visibility = Visibility.Visible;

        if (item != null)
        {
            // 显示系统图标
            var ext = Path.GetExtension(item.Name);
            var icon = SystemIconHelper.GetFileIcon(ext);
            PreviewFileIcon.Source = icon;
            PreviewFileIcon.Visibility = Visibility.Visible;

            SetPreviewInfo(item, message);
            PreviewHeader.Text = $"📄 {item.Name}";
        }
        else
        {
            PreviewFileIcon.Visibility = Visibility.Collapsed;
            PreviewFormatInfo.Text = message ?? "";
            PreviewFileNameText.Text = "";
            PreviewSizeText.Text = "";
            PreviewRatioText.Text = "";
            PreviewCompressedText.Text = "";
            PreviewDateText.Text = "";
            PreviewEncryptedText.Text = "";
            PreviewInfoPanel.Visibility = Visibility.Visible;
            PreviewHeader.Text = L.T(L.Settings_Tab_Preview);
        }

        ShowPreviewPanel();
    }

    /// <summary>
    /// L.T(L.Pwd_ShowBtn)目录预览：系统文件夹图标 + 目录名
    /// </summary>
    private void ShowDirectoryPreview(ArchiveItem item)
    {
        if (!item.IsDirectory) return;

        PreviewHeader.Text = $"📁 {item.Name.TrimEnd('/')}";

        // 内容区：文件夹图标
        PreviewFileIcon.Source = SystemIconHelper.GetFolderIcon();
        PreviewFileIcon.Visibility = Visibility.Visible;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewTextBox.Visibility = Visibility.Collapsed;
        PreviewWebBrowser.Visibility = Visibility.Collapsed;
        PreviewUnsupported.Visibility = Visibility.Collapsed;

        // 目录统计
        string formatInfo = L.T(L.Preview_DirLabel);
        if (_dirStats.TryGetValue(item.FullPath, out var stat))
        {
            var sizeStr = ArchiveItem.FormatSize(stat.Size);
            var compressedStr = ArchiveItem.FormatSize(stat.CompressedSize);
            var ratio = stat.Size > 0 ? $"{(double)stat.CompressedSize / stat.Size * 100:F1}%" : "--";
            formatInfo = L.TF(L.Preview_DirInfo, stat.Count, sizeStr, compressedStr, ratio);
        }

        // 信息面板
        SetPreviewInfo(item, formatInfo);
        PreviewFileNameText.Text = item.Name.TrimEnd('/');

        ShowPreviewPanel();
    }

    /// <summary>
    /// 显示压缩包总览信息（首次打开时）。
    /// </summary>
    private void ShowArchiveInfo()
    {
        if (string.IsNullOrEmpty(_currentArchivePath) || _allItems.Count == 0)
            return;

        var totalSize = _allItems.Sum(i => i.Size);
        var totalCompressed = _allItems.Sum(i => i.CompressedSize);
        int fileCount = _allItems.Count(i => !i.IsDirectory);
        int dirCount = _allItems.Count(i => i.IsDirectory);

        PreviewHeader.Text = L.TF(L.Preview_ArchiveHeader, Path.GetFileName(_currentArchivePath));

        // 内容区：有注释则像文本文件一样L.T(L.Pwd_ShowBtn)，L.T(L.MsgBox_No)则L.T(L.Pwd_ShowBtn)系统图标
        if (!string.IsNullOrEmpty(_archiveComment))
        {
            PreviewTextBox.Text = _archiveComment;
            PreviewTextBox.FontSize = AppSettings.Instance.TextPreviewFontSize;
            PreviewTextBox.Visibility = Visibility.Visible;
            PreviewFileIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            PreviewFileIcon.Source = SystemIconHelper.GetFileIcon(Path.GetExtension(_currentArchivePath));
            PreviewFileIcon.Visibility = Visibility.Visible;
            PreviewTextBox.Visibility = Visibility.Collapsed;
        }
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewWebBrowser.Visibility = Visibility.Collapsed;
        PreviewUnsupported.Visibility = Visibility.Collapsed;

        // 信息面板（压缩包概览）
        var ratio = totalSize > 0 ? $"{(double)totalCompressed / totalSize * 100:F1}%" : "--";
        PreviewFormatInfo.Text = L.TF(L.Preview_ArchiveOverview, fileCount, dirCount);
        PreviewFileNameText.Text = Path.GetFileName(_currentArchivePath);
        PreviewSizeText.Text = L.TF(L.Preview_ArchiveSize, FormatSize(totalSize));
        PreviewCompressedText.Text = L.TF(L.Preview_ArchiveCompressed, FormatSize(totalCompressed));
        PreviewRatioText.Text = L.TF(L.Preview_Ratio, ratio);
        PreviewDateText.Text = "";
        PreviewEncryptedText.Text = "";
        PreviewInfoPanel.Visibility = Visibility.Visible;

        ShowPreviewPanel();
    }

    /// <summary>
    /// 确保 PreviewPanel 位于正确的父 Grid 中。
    /// 位置 1/4 → 外层 ContentGrid；位置 2/3 → 内层 InnerContentGrid。
    /// </summary>
    private void EnsurePreviewInCorrectGrid(int position)
    {
        var currentParent = VisualTreeHelper.GetParent(PreviewPanel) as Grid;
        var target = (position == 2 || position == 3) ? InnerContentGrid : ContentGrid;

        if (currentParent == target) return;

        // 从当前父 Grid 中移除
        currentParent?.Children.Remove(PreviewPanel);
        // 添加到目标父 Grid（保持 z-order 合理：最后添加）
        target.Children.Add(PreviewPanel);
    }

    /// <summary>
    /// 根据 AppSettings.PreviewPosition 重新布局预览面板位置。
    /// 1=底部, 2=目录树下方, 3=文件列表下方, 4=文件列表右侧
    /// </summary>
    private void ApplyPreviewPosition(int position)
    {
        // 移动 PreviewPanel 到正确的父 Grid
        EnsurePreviewInCorrectGrid(position);

        // 先重置所有元素到默认状态
        PreviewSplitter.Visibility = Visibility.Collapsed;
        PreviewColSplitter.Visibility = Visibility.Collapsed;
        InnerPreviewSplitter.Visibility = Visibility.Collapsed;
        PreviewSplitterRow.Height = new GridLength(0);
        PreviewRow.Height = new GridLength(0);
        InnerPreviewSplitterRow.Height = new GridLength(0);
        InnerPreviewRow.Height = new GridLength(0);
        PreviewColumnDef.Width = new GridLength(0);
        PreviewColSplitterDef.Width = new GridLength(0);
        Grid.SetRowSpan(FolderTree, 1);
        Grid.SetRowSpan(TreeFileSplitter, 1);
        Grid.SetRowSpan(FileListGrid, 1);
        Grid.SetColumnSpan(FileListGrid, 1);
        Grid.SetColumnSpan(PreviewPanel, 1);
        Grid.SetRowSpan(PreviewPanel, 1);
        Grid.SetRowSpan(InnerPreviewSplitter, 1);
        Grid.SetColumnSpan(InnerPreviewSplitter, 5);
        Grid.SetRow(InnerPreviewSplitter, 1);
        Grid.SetColumn(InnerPreviewSplitter, 0);
        // 默认 InnerContentGrid 横跨全部5列
        Grid.SetColumnSpan(InnerContentGrid, 5);

        switch (position)
        {
            case 1: // 底部（当前默认）
                PreviewSplitter.Visibility = Visibility.Visible;
                PreviewSplitterRow.Height = new GridLength(4);
                Grid.SetRow(PreviewPanel, 2);
                Grid.SetColumn(PreviewPanel, 0);
                Grid.SetColumnSpan(PreviewPanel, 5);
                break;

            case 2: // 目录树下方
                Grid.SetRowSpan(FileListGrid, 3);
                Grid.SetColumnSpan(FileListGrid, 3);
                Grid.SetRowSpan(TreeFileSplitter, 3);
                Grid.SetRow(PreviewPanel, 2);
                Grid.SetColumn(PreviewPanel, 0);
                Grid.SetColumnSpan(PreviewPanel, 1);
                // 内部分隔条 Row 1
                InnerPreviewSplitter.Visibility = Visibility.Visible;
                InnerPreviewSplitterRow.Height = new GridLength(4);
                Grid.SetColumnSpan(InnerPreviewSplitter, 1);
                Grid.SetColumn(InnerPreviewSplitter, 0);
                break;

            case 3: // 文件列表下方
                Grid.SetRowSpan(FolderTree, 3);
                Grid.SetRowSpan(TreeFileSplitter, 3);
                Grid.SetColumnSpan(FileListGrid, 1);
                Grid.SetRow(PreviewPanel, 2);
                Grid.SetColumn(PreviewPanel, 2);
                Grid.SetColumnSpan(PreviewPanel, 3);
                // 内部分隔条 Row 1
                InnerPreviewSplitter.Visibility = Visibility.Visible;
                InnerPreviewSplitterRow.Height = new GridLength(4);
                Grid.SetColumnSpan(InnerPreviewSplitter, 3);
                Grid.SetColumn(InnerPreviewSplitter, 2);
                break;

            case 4: // 文件列表右侧
                PreviewColSplitter.Visibility = Visibility.Visible;
                PreviewColSplitterDef.Width = new GridLength(4);
                Grid.SetRow(PreviewPanel, 0);
                Grid.SetColumn(PreviewPanel, 4);
                Grid.SetColumnSpan(PreviewPanel, 1);
                // 限制 InnerContentGrid 只占 Col 0-2（不延伸到预览列）
                Grid.SetColumnSpan(InnerContentGrid, 3);
                break;
        }
    }

    /// <summary>
    /// 根据 AppSettings.InfoPanelOrientation 切换信息面板布局。
    /// Horizontal = 内容区右侧；Vertical = 内容区下方
    /// </summary>
    private void ApplyInfoPanelOrientation(string orientation)
    {
        if (orientation == "Vertical")
        {
            // 信息面板在内容区下方
            PreviewContentGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            PreviewContentGrid.ColumnDefinitions[1].Width = new GridLength(0);
            PreviewContentGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Auto);
            Grid.SetRow(PreviewInfoPanel, 1);
            Grid.SetColumn(PreviewInfoPanel, 0);
            PreviewInfoPanel.Margin = new Thickness(0, 12, 0, 0);
        }
        else
        {
            // 信息面板在内容区右侧（默认）
            PreviewContentGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            PreviewContentGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Auto);
            PreviewContentGrid.RowDefinitions[1].Height = new GridLength(0);
            Grid.SetRow(PreviewInfoPanel, 0);
            Grid.SetColumn(PreviewInfoPanel, 1);
            PreviewInfoPanel.Margin = new Thickness(12, 0, 0, 0);
        }
    }

    private void ShowPreviewPanel()
    {
        var pos = AppSettings.Instance.PreviewPosition;

        // 如果位置变了，保存旧位置大小并应用新布局
        if (pos != _lastAppliedPosition)
        {
            SaveCurrentPreviewSize(_lastAppliedPosition);
            _lastAppliedPosition = pos;
            ApplyPreviewPosition(pos);
        }
        else
        {
            ApplyPreviewPosition(pos);
        }

        if (pos == 4)
        {
            // 右侧模式：靠列宽度控制
            var colWidth = _lastPreviewSizes[4] > 0 ? _lastPreviewSizes[4] : 350;
            PreviewColumnDef.Width = new GridLength(colWidth, GridUnitType.Pixel);
            PreviewColSplitterDef.Width = new GridLength(4);
            PreviewColSplitter.Visibility = Visibility.Visible;
            PreviewRow.Height = new GridLength(0);
            PreviewSplitterRow.Height = new GridLength(0);
            PreviewSplitter.Visibility = Visibility.Collapsed;
            InnerPreviewRow.Height = new GridLength(0);
        }
        else if (pos == 2 || pos == 3)
        {
            // 目录树下方 / 文件列表下方：内层 Grid 的预览行控制高度
            var h = _lastPreviewSizes[pos] > 0 ? _lastPreviewSizes[pos] : 200;
            InnerPreviewRow.Height = new GridLength(h, GridUnitType.Pixel);
            PreviewRow.Height = new GridLength(0);
            PreviewSplitterRow.Height = new GridLength(0);
            PreviewSplitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            // 底部模式：外层 Grid 的预览行控制高度
                var h = _lastPreviewSizes[1] > 0
                ? new GridLength(_lastPreviewSizes[1], GridUnitType.Pixel)
                : new GridLength(1, GridUnitType.Star);
            PreviewSplitterRow.Height = new GridLength(4);
            PreviewRow.Height = h;
            PreviewSplitter.Visibility = Visibility.Visible;
        }
        if (_previewPanelEnabled)
            PreviewPanel.Visibility = Visibility.Visible;
        ApplyInfoPanelOrientation(AppSettings.Instance.InfoPanelOrientation);
    }

    /// <summary>
    /// 保存指定位置的当前预览大小到独立记忆字典。
    /// </summary>
    private void SaveCurrentPreviewSize(int position)
    {
        if (position == 4)
        {
            if (PreviewColumnDef.Width.GridUnitType == GridUnitType.Pixel)
                _lastPreviewSizes[4] = PreviewColumnDef.Width.Value;
        }
        else if (position == 2 || position == 3)
        {
            if (InnerPreviewRow.Height.GridUnitType == GridUnitType.Pixel)
                _lastPreviewSizes[position] = InnerPreviewRow.Height.Value;
            else if (InnerPreviewRow.Height.Value > 0)
                _lastPreviewSizes[position] = 300; // Star 模式切 Pixel 时给个默认值
        }
        else // position 1
        {
            if (PreviewRow.Height.GridUnitType == GridUnitType.Pixel)
                _lastPreviewSizes[1] = PreviewRow.Height.Value;
        }
    }

    /// <summary>
    /// 仅清除预览内容，不隐藏面板（用于文件间切换，避免闪烁）。
    /// </summary>
    private void ClearPreviewContent()
    {
        if (PreviewPanel.Visibility == Visibility.Visible)
            SaveCurrentPreviewSize(AppSettings.Instance.PreviewPosition);

        PreviewImage.Source = null;
        ImageBehavior.SetAnimatedSource(PreviewImage, null); // 停止 GIF 动画
        PreviewFileIcon.Source = null;
        PreviewTextBox.Text = "";
        PreviewWebBrowser.Navigate("about:blank");
        PreviewWebBrowser.Visibility = Visibility.Collapsed;
        PreviewFormatInfo.Text = "";
        PreviewFileNameText.Text = "";
        PreviewSizeText.Text = "";
        PreviewRatioText.Text = "";
        PreviewCompressedText.Text = "";
        PreviewDateText.Text = "";
        PreviewEncryptedText.Text = "";
    }

    private void HidePreview()
    {
        // 只在预览面板可见时保存大小，避免重复 HidePreview 覆盖已保存的值
        if (PreviewPanel.Visibility == Visibility.Visible)
            SaveCurrentPreviewSize(AppSettings.Instance.PreviewPosition);

        PreviewImage.Source = null;
        ImageBehavior.SetAnimatedSource(PreviewImage, null); // 停止 GIF 动画
        PreviewFileIcon.Source = null;
        PreviewTextBox.Text = "";
        // 清除 WebBrowser 内容并隐藏
        PreviewWebBrowser.Navigate("about:blank");
        PreviewWebBrowser.Visibility = Visibility.Collapsed;
        PreviewRow.Height = new GridLength(0);
        PreviewSplitterRow.Height = new GridLength(0);
        PreviewSplitter.Visibility = Visibility.Collapsed;
        PreviewPanel.Visibility = Visibility.Collapsed;
        InnerPreviewRow.Height = new GridLength(0);
        InnerPreviewSplitterRow.Height = new GridLength(0);
        InnerPreviewSplitter.Visibility = Visibility.Collapsed;
        // 重置右侧模式（位置4）的列宽
        PreviewColumnDef.Width = new GridLength(0);
        PreviewColSplitterDef.Width = new GridLength(0);
        PreviewColSplitter.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 填充预览信息面板（通用信息 + 格式特定信息）。
    /// </summary>
    private void SetPreviewInfo(ArchiveItem item, string? formatInfo = null)
    {
        var ratio = item.Size > 0
            ? $"{(double)item.CompressedSize / item.Size * 100:F1}%"
            : "--";

        PreviewFormatInfo.Text = formatInfo ?? "";
        PreviewFormatInfo.Visibility = string.IsNullOrEmpty(formatInfo) ? Visibility.Collapsed : Visibility.Visible;

        PreviewFileNameText.Text = item.Name;

        PreviewSizeText.Text = L.TF(L.Preview_OriginalSize, FormatSize(item.Size));
        PreviewCompressedText.Text = L.TF(L.Preview_PostCompressSize, FormatSize(item.CompressedSize));
        PreviewRatioText.Text = L.TF(L.Preview_Ratio, ratio);

        PreviewDateText.Text = item.LastModified > DateTime.MinValue
            ? L.TF(L.Preview_Modified, item.LastModified.ToString("yyyy-MM-dd HH:mm"))
            : L.T(L.Preview_NoModified);
        PreviewEncryptedText.Text = item.IsEncrypted ? L.T(L.Preview_Encrypted) : "";
        PreviewInfoPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// L.T(L.Pwd_ShowBtn)L.T(L.Settings_Tab_Preview)加载进度（L.T(L.CompressConflict_Overwrite)在L.T(L.Settings_Tab_Preview)内容区中央）。
    /// </summary>
    private void ShowPreviewLoading(string? fileName = null)
    {
        PreviewLoadingText.Text = L.TF(L.Preview_Loading, fileName != null ? ": " + fileName : "");
        PreviewLoadingPanel.Visibility = Visibility.Visible;
        PreviewLoadingPercent.Text = "";
    }

    /// <summary>
    /// 隐藏L.T(L.Settings_Tab_Preview)加载进度。
    /// </summary>
    private void HidePreviewLoading()
    {
        PreviewLoadingPanel.Visibility = Visibility.Collapsed;
        PreviewLoadingPercent.Text = "";
    }

    /// <summary>
    /// L.T(L.Settings_Advanced_CleanPreviewTemp)
    /// </summary>
    private void ClearPreviewTemp()
    {
        try
        {
            if (!string.IsNullOrEmpty(_previewTempDir) && Directory.Exists(_previewTempDir))
            {
                Directory.Delete(_previewTempDir, recursive: true);
                _previewTempDir = null;
            }
        }
        catch (Exception cleanupEx)
        {
            App.LogDebug("ClearPreviewTemp: failed: {0}", cleanupEx.Message);
        }
    }

    #endregion
}
