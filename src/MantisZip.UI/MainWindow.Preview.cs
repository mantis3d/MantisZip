using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using Markdig;
using Ude;
using WpfAnimatedGif;
using Microsoft.Web.WebView2.Core;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

public partial class MainWindow
{
    #region 文件预览

    private bool _webView2Crashed;

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

    private static readonly HashSet<string> PeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".ocx"
    };

    private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ttf", ".otf", ".woff"
    };

    private static readonly HashSet<string> TorrentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".torrent"
    };

    private static readonly HashSet<string> WavExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav"
    };

    private static readonly HashSet<string> FlacExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".flac"
    };

    private static readonly HashSet<string> SqliteExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sqlite", ".sqlite3", ".db", ".db3"
    };

    private static readonly HashSet<string> IsoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".iso"
    };

    private static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".xlsx", ".pptx"
    };

    private static readonly HashSet<string> SvgExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".svg"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi"
    };

    /// <summary>只需读取文件头的格式，不受 MaxPreviewFileSize 限制。</summary>
    private static readonly HashSet<string> MetadataOnlyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".ocx",
        ".pdf", ".docx", ".xlsx", ".pptx",
        ".wav", ".flac",
        ".sqlite", ".sqlite3", ".db", ".db3",
        ".iso",
        ".torrent",
        ".mp4", ".mkv", ".avi",
    };

    // ═══════════════════════════════════════════
    //  预览辅助方法 — 所有格式共用
    // ═══════════════════════════════════════════

    /// <summary>
    /// 提取压缩包内条目到临时目录，返回临时文件路径。
    /// 自动设置 _previewTempDir 供后续 Cleanup 使用。
    /// </summary>
    private async Task<string> ExtractPreviewFileAsync(ArchiveItem item, string fallbackName, CancellationToken ct)
    {
        _previewTempDir = Path.Combine(Path.GetTempPath(), L.T(L.App_MantisZipTitle), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_previewTempDir);
        var tempFile = Path.Combine(_previewTempDir, Path.GetFileName(item.Name) ?? fallbackName);
        await Core.Utils.ArchiveEntryExtractor.ExtractEntryAsync(
            _currentArchivePath!, item.Name, tempFile, _currentFormat, _currentPassword, ct);
        ct.ThrowIfCancellationRequested();
        return tempFile;
    }

    /// <summary>
    /// 隐藏所有预览内容控件，调用者再显式打开需要的控件。
    /// 加新格式时请在方法开头调用此函数。
    /// </summary>
    /// <summary>
    /// 确保 WebView2 已初始化（CoreWebView2 可用）。浏览器进程崩溃后会重新初始化。
    /// </summary>
    private async Task EnsureWebView2InitializedAsync()
    {
        // 浏览器进程崩溃后，或首次调用尚未初始化，都需要创建
        if (!_webView2Crashed && PreviewWebView2.CoreWebView2 != null)
            return; // 已正常初始化，无需操作

        // 崩溃后或首次初始化
        _webView2Crashed = false;
        try
        {
            await PreviewWebView2.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            App.LogDebug("WebView2 (re)initialization failed: {0}", ex.Message);
            throw;
        }

        // CoreWebView2 刚刚创建，执行完整设置
        try
        {
            // 订阅浏览器进程崩溃事件
            PreviewWebView2.CoreWebView2!.ProcessFailed += OnWebView2ProcessFailed;

            // 安全配置：禁止右键菜单、禁用密码/表单自动填充
            PreviewWebView2.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            PreviewWebView2.CoreWebView2.Settings.IsScriptEnabled = true;
            PreviewWebView2.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            PreviewWebView2.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;

            // 隐藏 PDF 工具栏中的操作按钮（点击设置按钮会导致浏览器进程崩溃）
            PreviewWebView2.CoreWebView2.Settings.HiddenPdfToolbarItems =
                CoreWebView2PdfToolbarItems.Save |
                CoreWebView2PdfToolbarItems.Print |
                CoreWebView2PdfToolbarItems.SaveAs |
                CoreWebView2PdfToolbarItems.MoreSettings;

            // 阻止所有外部网络请求（只允许 file:// 本地文件）
            PreviewWebView2.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            PreviewWebView2.CoreWebView2.WebResourceRequested += (s, e) =>
            {
                if (e.Request.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    e.Request.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    e.Response = PreviewWebView2.CoreWebView2.Environment.CreateWebResourceResponse(
                        null, 403, "Blocked", null);
                }
            };
        }
        catch (Exception ex)
        {
            App.LogDebug("WebView2 initialization failed: {0}", ex.Message);
            throw;
        }
    }

    private void OnWebView2ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _webView2Crashed = true;
        App.LogDebug("WebView2 process failed: reason={0}, exitCode={1}", e.Reason, e.ExitCode);
        // 仅在 WebView2 当前可见时提示用户，否则静默恢复即可
        Dispatcher.InvokeAsync(() =>
        {
            if (PreviewWebView2.Visibility == Visibility.Visible)
            {
                PreviewWebView2.Visibility = Visibility.Collapsed;
                ShowUnsupportedPreview(null, "预览组件异常，请重新选择文件以恢复");
            }
        });
    }

    private void HideAllPreviewControls()
    {
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewTextBox.Visibility = Visibility.Collapsed;
        PreviewFileIcon.Visibility = Visibility.Collapsed;
        PreviewUnsupported.Visibility = Visibility.Collapsed;
        PreviewWebView2.Visibility = Visibility.Collapsed;
    }

    private async Task ShowPreviewAsync(ArchiveItem item)
    {
        // 取消上一次正在进行的预览（避免旧预览完成后覆盖新内容）
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        try
        {
            // 清理上次预览：先清除内容（释放对临时文件的引用）再删除文件，防止 WPF 引用已删除的字体文件导致原生崩溃
            ClearPreviewContent();
            ClearPreviewTemp();
            // 统一清空格式信息面板——所有格式共用，不清就会残留旧数据
            PreviewExtraInfoPanel.Children.Clear();
            PreviewExtraInfoPanel.Visibility = Visibility.Collapsed;

            var s = AppSettings.Instance;
            var ext = Path.GetExtension(item.Name);

            // 文件大小上限检查（仅对需要加载完整内容的格式生效，只读头的格式不受限制）
            if (!MetadataOnlyExtensions.Contains(ext) && item.Size > s.MaxPreviewFileSize)
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

                var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
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

                var tempFile = await ExtractPreviewFileAsync(item, "preview.html", ct);
                if (MarkdownExtensions.Contains(ext))
                    await ShowMarkdownPreview(tempFile, item);
                else
                    await ShowHtmlPreview(tempFile, item);
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

                var tempFile = await ExtractPreviewFileAsync(item, "preview.txt", ct);
                ShowTextPreview(tempFile, ext, item);
            }
            else if (PeExtensions.Contains(ext))
            {
                var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
                ShowPePreview(tempFile, item);
            }
            else if (PdfExtensions.Contains(ext))
            {
                var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
                await ShowPdfPreview(tempFile, item);
            }
            else if (FontExtensions.Contains(ext))
            {
                var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
                ShowFontPreview(tempFile, item);
            }
            else if (WavExtensions.Contains(ext))
            {
                var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
                ShowAudioPreview(tempFile, item, "WAV");
            }
            else if (FlacExtensions.Contains(ext))
            {
                var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
                ShowAudioPreview(tempFile, item, "FLAC");
            }
            else if (SqliteExtensions.Contains(ext))
            {
                var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
                ShowSqlitePreview(tempFile, item);
            }
            else if (IsoExtensions.Contains(ext))
            {
                var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
                ShowIsoPreview(tempFile, item);
            }
            else if (TorrentExtensions.Contains(ext))
            {
                var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
                ShowTorrentPreview(tempFile, item);
            }
            else if (OfficeExtensions.Contains(ext))
            {
                var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
                ShowOfficePreview(tempFile, item);
            }
            else if (SvgExtensions.Contains(ext))
            {
                var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
                await ShowSvgPreview(tempFile, item);
            }
            else if (VideoExtensions.Contains(ext))
            {
                var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
                ShowVideoPreview(tempFile, item);
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

                _gifController = null;
                var gifBitmap = new System.Windows.Media.Imaging.BitmapImage(new Uri(filePath));
                ImageBehavior.SetAnimatedSource(PreviewImage, gifBitmap);
                // 异步加载完成后获取控制器
                ImageBehavior.AddAnimationLoadedHandler(PreviewImage, (s, e) =>
                {
                    _gifController = ImageBehavior.GetAnimationController(PreviewImage);
                    if (_gifController != null)
                    {
                        _gifController.CurrentFrameChanged += (_, _) => UpdateGifFrameInput();
                        UpdateGifFrameInput();
                    }
                });
                // 也立即尝试获取（可能同步可用）
                _gifController = ImageBehavior.GetAnimationController(PreviewImage);
                if (_gifController != null)
                {
                    _gifController.CurrentFrameChanged += (_, _) => UpdateGifFrameInput();
                }

                PreviewImage.MaxWidth = gifWidth;
                PreviewImage.MaxHeight = gifHeight;
                HideAllPreviewControls();
                PreviewImage.Visibility = Visibility.Visible;
                PreviewHeader.Text = L.TF(L.Preview_ImageHeader, Path.GetFileName(filePath));
                SetPreviewInfo(item, L.TF(L.Preview_Dimensions, gifWidth, gifHeight));
                ShowPreviewPanel();

                // GIF 格式信息
                try
                {
                    var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                        new Uri(filePath),
                        System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                        System.Windows.Media.Imaging.BitmapCacheOption.None);
                    int frameCount = decoder.Frames.Count;
                    SetFormatSpecificInfo(
                        (L.T(L.Preview_ImagePixels), $"{gifWidth * gifHeight:N0}"),
                        (L.T(L.Preview_ImageGifFrames), frameCount.ToString())
                    );
                }
                catch
                {
                    SetFormatSpecificInfo();
                }

                // 工具栏：左侧通用缩放 + 右侧 GIF 播放控制
                SetToolbar(
                    new[] {
                        new ToolbarButton { Text = "⊞", Tooltip = L.T(L.Preview_ZoomFit), OnClick = () => ApplyZoom(ZoomMode.FitWindow) },
                        new ToolbarButton { Text = "1:1", Tooltip = L.T(L.Preview_Zoom100), OnClick = () => ApplyZoom(ZoomMode.Zoom100) },
                        new ToolbarButton { Text = "↔", Tooltip = L.T(L.Preview_ZoomFitWidth), OnClick = () => ApplyZoom(ZoomMode.FitWidth) }
                    },
                    new[] {
                        new ToolbarButton { Text = "⏮", Tooltip = L.T(L.Preview_GifPrevFrame), OnClick = GifPrevFrame },
                        new ToolbarButton { Text = "⏯", Tooltip = L.T(L.Preview_GifPause), OnClick = ToggleGifPlayPause },
                        new ToolbarButton { Text = "⏭", Tooltip = L.T(L.Preview_GifNextFrame), OnClick = GifNextFrame }
                    }
                );
                AddGifFrameInput();
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
            HideAllPreviewControls();
            PreviewImage.Visibility = Visibility.Visible;
            PreviewHeader.Text = L.TF(L.Preview_ImageHeader, Path.GetFileName(filePath));

            // 图片信息
            SetPreviewInfo(item, L.TF(L.Preview_Dimensions, bitmap.PixelWidth, bitmap.PixelHeight));

            ShowPreviewPanel();

            // 图片格式信息
            try
            {
                long pixels = bitmap.PixelWidth * (long)bitmap.PixelHeight;
                string bitDepth = bitmap.Format.BitsPerPixel.ToString();
                string dpi = $"{bitmap.DpiX:F0} x {bitmap.DpiY:F0} DPI";
                SetFormatSpecificInfo(
                    (L.T(L.Preview_ImagePixels), $"{pixels:N0}"),
                    (L.T(L.Preview_ImageBitDepth), bitDepth),
                    (L.T(L.Preview_ImageDpi), dpi)
                );
            }
            catch
            {
                SetFormatSpecificInfo();
            }

            // 工具栏：左侧通用缩放，右侧透明背景切换（仅 PNG/ICO/WebP）
            SetToolbar(
                new[] {
                    new ToolbarButton { Text = "⊞", Tooltip = L.T(L.Preview_ZoomFit), OnClick = () => ApplyZoom(ZoomMode.FitWindow) },
                    new ToolbarButton { Text = "1:1", Tooltip = L.T(L.Preview_Zoom100), OnClick = () => ApplyZoom(ZoomMode.Zoom100) },
                    new ToolbarButton { Text = "↔", Tooltip = L.T(L.Preview_ZoomFitWidth), OnClick = () => ApplyZoom(ZoomMode.FitWidth) }
                },
                (ext == ".png" || ext == ".ico" || ext == ".webp")
                    ? new[] {
                        new ToolbarButton { Text = "☐", Tooltip = L.T(L.Preview_ToggleTransparency), IsToggle = true, IsChecked = _transparentBgEnabled, OnClick = ToggleTransparencyBg }
                      }
                    : Array.Empty<ToolbarButton>()
            );
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

    private async Task ShowHtmlPreview(string filePath, ArchiveItem item)
    {
        await EnsureWebView2InitializedAsync();
        HideAllPreviewControls();
        PreviewWebView2.CoreWebView2.Navigate(new Uri(filePath).AbsoluteUri);
        PreviewWebView2.Visibility = Visibility.Visible;
        SetPreviewInfo(item, L.T(L.Preview_HtmlInfo));
        PreviewHeader.Text = L.TF(L.Preview_HtmlHeader, Path.GetFileName(filePath));
        ShowPreviewPanel();
    }

    private async Task ShowMarkdownPreview(string filePath, ArchiveItem item)
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
            // 包裹基本样式以便阅读，增加暗色主题支持
            var styledHtml = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'/>
<style>
  body {{ font-family: system-ui, sans-serif; font-size: 14px; line-height: 1.6; padding: 16px; color: #222; background: #fff; }}
  @media (prefers-color-scheme: dark) {{
    body {{ color: #e0e0e0; background: #1e1e1e; }}
    pre {{ background: #2d2d2d; }}
    code {{ background: #2d2d2d; }}
    td, th {{ border-color: #444; }}
  }}
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
            await EnsureWebView2InitializedAsync();
            PreviewWebView2.CoreWebView2.Navigate(new Uri(tempHtml).AbsoluteUri);
            HideAllPreviewControls();
            PreviewWebView2.Visibility = Visibility.Visible;
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
        HideAllPreviewControls();
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
        HideAllPreviewControls();
        PreviewFileIcon.Source = SystemIconHelper.GetFolderIcon();
        PreviewFileIcon.Visibility = Visibility.Visible;

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
        PreviewWebView2.Visibility = Visibility.Collapsed;
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
        _gifController?.Dispose();
        _gifController = null;
        PreviewFileIcon.Source = null;
        PreviewTextBox.Text = "";
        PreviewTextBox.ClearValue(TextBox.FontFamilyProperty); // 重置为默认字体，释放对临时字体文件的引用，防止 WPF 在文件被删除后崩溃
        if (PreviewWebView2.CoreWebView2 != null)
            PreviewWebView2.CoreWebView2.Navigate("about:blank");
        PreviewWebView2.Visibility = Visibility.Collapsed;
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
        // 清除 WebView2 内容并隐藏
        if (PreviewWebView2.CoreWebView2 != null)
            PreviewWebView2.CoreWebView2.Navigate("about:blank");
        PreviewWebView2.Visibility = Visibility.Collapsed;
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

    private void SetFormatSpecificInfo(params (string label, string value)[] items)
    {
        PreviewExtraInfoPanel.Children.Clear();
        if (items.Length == 0)
        {
            PreviewExtraInfoPanel.Visibility = Visibility.Collapsed;
            return;
        }
        var secondaryBrush = (Brush)FindResource("Theme_TextSecondary");
        foreach (var (label, value) in items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            row.Children.Add(new TextBlock { Text = label + ": ", FontSize = 11, Foreground = secondaryBrush, FontWeight = FontWeights.SemiBold });
            row.Children.Add(new TextBlock { Text = value, FontSize = 11, Foreground = secondaryBrush, TextWrapping = TextWrapping.Wrap });
            PreviewExtraInfoPanel.Children.Add(row);
        }
        PreviewExtraInfoPanel.Visibility = Visibility.Visible;
    }

    private void UpdateGeneralSeparator() { }

    // ═══════════════════════════════════════════
    //  工具栏基础设施
    // ═══════════════════════════════════════════

    public record ToolbarButton
    {
        public string Text { get; set; } = "";
        public string Tooltip { get; set; } = "";
        public bool IsToggle { get; set; }
        public bool IsChecked { get; set; }
        public Action? OnClick { get; set; }
    }

    private void SetToolbar(ToolbarButton[] leftButtons, ToolbarButton[] rightButtons)
    {
        PreviewToolbarPanel.Children.Clear();
        bool hasLeft = leftButtons.Length > 0;
        bool hasRight = rightButtons.Length > 0;
        if (!hasLeft && !hasRight) { PreviewToolbarBorder.Visibility = Visibility.Collapsed; return; }
        foreach (var btn in leftButtons) PreviewToolbarPanel.Children.Add(CreateToolbarButtonElement(btn));
        if (hasLeft && hasRight) AddToolbarSeparator();
        foreach (var btn in rightButtons) PreviewToolbarPanel.Children.Add(CreateToolbarButtonElement(btn));
        PreviewToolbarBorder.Visibility = Visibility.Visible;
    }

    private Border CreateToolbarButtonElement(ToolbarButton btn)
    {
        var border = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(1, 0, 1, 0),
            Cursor = Cursors.Hand,
            ToolTip = btn.Tooltip,
            Child = new TextBlock { Text = btn.Text, FontSize = 13, Padding = new Thickness(6, 3, 6, 3) },
        };
        if (btn.IsToggle)
        {
            var bgBrush = new SolidColorBrush(Color.FromArgb(30, 100, 100, 100));
            border.MouseLeftButtonUp += (_, _) =>
            {
                btn.IsChecked = !btn.IsChecked;
                border.Background = btn.IsChecked ? bgBrush : Brushes.Transparent;
                btn.OnClick?.Invoke();
            };
            if (btn.IsChecked) border.Background = bgBrush;
        }
        else
        {
            border.MouseLeftButtonUp += (_, _) => btn.OnClick?.Invoke();
        }
        return border;
    }

    private void AddToolbarSeparator()
    {
        PreviewToolbarPanel.Children.Add(new Border
        {
            Width = 1, Height = 16, Margin = new Thickness(4, 0, 4, 0),
            Background = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    // ═══════════════════════════════════════════
    //  缩放
    // ═══════════════════════════════════════════

    private enum ZoomMode { FitWindow, Zoom100, FitWidth }

    private void ApplyZoom(ZoomMode mode)
    {
        if (PreviewImage.Source is not BitmapSource bmp) return;
        switch (mode)
        {
            case ZoomMode.FitWindow:
                PreviewImage.Stretch = Stretch.Uniform;
                PreviewImage.MaxWidth = double.PositiveInfinity;
                PreviewImage.MaxHeight = double.PositiveInfinity;
                break;
            case ZoomMode.Zoom100:
                PreviewImage.Stretch = Stretch.None;
                PreviewImage.MaxWidth = bmp.PixelWidth;
                PreviewImage.MaxHeight = bmp.PixelHeight;
                break;
            case ZoomMode.FitWidth:
                PreviewImage.Stretch = Stretch.UniformToFill;
                PreviewImage.MaxWidth = double.PositiveInfinity;
                PreviewImage.MaxHeight = double.PositiveInfinity;
                break;
        }
    }

    // ═══════════════════════════════════════════
    //  字号
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

    private void ToggleTransparencyBg()
    {
        _transparentBgEnabled = !_transparentBgEnabled;
        if (VisualTreeHelper.GetParent(PreviewImage) is Panel parent)
            parent.Background = _transparentBgEnabled ? new ImageBrush { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute, ImageSource = CreateCheckerPattern() } : Brushes.Transparent;
    }

    // ── GIF 播放控制 ──

    private void ToggleGifPlayPause()
    {
        if (_gifController == null) return;
        if (_gifController.IsPaused)
            _gifController.Play();
        else
            _gifController.Pause();
    }

    private void GifPrevFrame()
    {
        if (_gifController == null) return;
        int frame = _gifController.CurrentFrame - 1;
        if (frame < 0) frame = _gifController.FrameCount - 1;
        _gifController.Pause();
        _gifController.GotoFrame(frame);
        UpdateGifFrameInput();
    }

    private void GifNextFrame()
    {
        if (_gifController == null) return;
        int frame = _gifController.CurrentFrame + 1;
        if (frame >= _gifController.FrameCount) frame = 0;
        _gifController.Pause();
        _gifController.GotoFrame(frame);
        UpdateGifFrameInput();
    }

    // ── GIF 帧输入 ──

    private void AddGifFrameInput()
    {
        AddToolbarSeparator();
        _gifFrameInput = new TextBox
        {
            Width = 36,
            Height = 20,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 1, 0),
        };
        _gifFrameInput.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && _gifController != null && int.TryParse(_gifFrameInput?.Text, out int frame))
            {
                frame = Math.Clamp(frame - 1, 0, _gifController.FrameCount - 1);
                _gifController.Pause();
                _gifController.GotoFrame(frame);
                UpdateGifFrameInput();
            }
        };
        _gifFrameTotal = new TextBlock
        {
            Text = "/ --",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        };
        PreviewToolbarPanel.Children.Add(_gifFrameInput);
        PreviewToolbarPanel.Children.Add(_gifFrameTotal);
        UpdateGifFrameInput();
    }

    private void UpdateGifFrameInput()
    {
        if (_gifFrameInput == null || _gifController == null) return;
        _gifFrameInput.Text = $"{_gifController.CurrentFrame + 1}";
        if (_gifFrameTotal != null)
            _gifFrameTotal.Text = $"/ {_gifController.FrameCount}";
    }

    // ── 字体连字切换 ──

    private void ToggleFontLigatures()
    {
        _fontLigaturesEnabled = !_fontLigaturesEnabled;
        PreviewTextBox.Typography.StandardLigatures = _fontLigaturesEnabled;
        PreviewTextBox.Typography.ContextualLigatures = _fontLigaturesEnabled;
        PreviewTextBox.Typography.DiscretionaryLigatures = _fontLigaturesEnabled;
        // WPF Typography 属性变化后需要重新设置文本才能触发重绘
        var text = PreviewTextBox.Text;
        PreviewTextBox.Text = "";
        PreviewTextBox.Text = text;
    }

    private static BitmapSource CreateCheckerPattern()
    {
        var pixels = new byte[16 * 16 * 4];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                bool isDark = (x / 8 + y / 8) % 2 == 0;
                int idx = (y * 16 + x) * 4;
                byte c = isDark ? (byte)200 : (byte)230;
                pixels[idx] = c; pixels[idx + 1] = c; pixels[idx + 2] = c; pixels[idx + 3] = 255;
            }
        var bmp = new WriteableBitmap(16, 16, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, 16, 16), pixels, 16 * 4, 0);
        return bmp;
    }

    // ── PE ──
    private void ShowPePreview(string filePath, ArchiveItem item)
    {
        var info = PeParser.Parse(filePath);
        if (info == null) { ShowUnsupportedPreview(item, L.T(L.Preview_PeParseFailed)); return; }
        var productName = info.ProductName ?? info.AdditionalInfo ?? Path.GetFileName(filePath);
        PreviewTextBox.Text = productName; PreviewTextBox.FontSize = 18;
        PreviewTextBox.TextAlignment = TextAlignment.Center;
        HideAllPreviewControls();
        PreviewTextBox.Visibility = Visibility.Visible;
        SetPreviewInfo(item); PreviewHeader.Text = L.TF(L.Preview_PeHeader, info.Architecture ?? "", info.Subsystem ?? "");
        ShowPreviewPanel();
        var extra = new List<(string, string)>();
        if (!string.IsNullOrEmpty(info.CompanyName)) extra.Add((L.T(L.Preview_PeCompany), info.CompanyName));
        if (!string.IsNullOrEmpty(info.FileVersion)) extra.Add((L.T(L.Preview_PeVersion), info.FileVersion));
        if (!string.IsNullOrEmpty(info.ProductVersion)) extra.Add((L.T(L.Preview_PeProductVersion), info.ProductVersion));
        if (!string.IsNullOrEmpty(info.AdditionalInfo)) extra.Add((L.T(L.Preview_PeDescription), info.AdditionalInfo));
        SetFormatSpecificInfo(extra.ToArray()); SetToolbar(Array.Empty<ToolbarButton>(), Array.Empty<ToolbarButton>());
    }

    // ── PDF ──
    private async Task ShowPdfPreview(string filePath, ArchiveItem item)
    {
        try
        {
            var info = PdfParser.Parse(filePath);
            if (info == null) { ShowUnsupportedPreview(item, L.T(L.Preview_PdfParseFailed)); return; }
            // 始终显示元数据和图标
            PreviewFileIcon.Source = SystemIconHelper.GetFileIcon(".pdf");
            HideAllPreviewControls();
            // 若文件大小在预览上限内，用 WebView2 渲染 PDF 内容
            if (item.Size <= AppSettings.Instance.MaxPreviewFileSize)
            {
                await EnsureWebView2InitializedAsync();
                PreviewWebView2.CoreWebView2.Navigate(new Uri(filePath).AbsoluteUri);
                PreviewWebView2.Visibility = Visibility.Visible;
            }
            else
            {
                PreviewFileIcon.Visibility = Visibility.Visible;
            }
            SetPreviewInfo(item);
            PreviewHeader.Text = L.TF(L.Preview_PdfHeader, info.AdditionalInfo ?? "PDF");
            ShowPreviewPanel();
            var extra = new List<(string, string)> { (L.T(L.Preview_PdfPages), info.PageCount?.ToString() ?? "--"), (L.T(L.Preview_PdfEncrypted), info.IsEncrypted == true ? "是" : "否") };
            if (!string.IsNullOrEmpty(info.Title)) extra.Insert(0, (L.T(L.Preview_PdfTitle), info.Title));
            if (!string.IsNullOrEmpty(info.Author)) extra.Add((L.T(L.Preview_PdfAuthor), info.Author));
            SetFormatSpecificInfo(extra.ToArray()); SetToolbar(Array.Empty<ToolbarButton>(), Array.Empty<ToolbarButton>());
        }
        catch (Exception ex) { App.LogDebug("ShowPdfPreview: failed: {0}", ex.Message); ShowUnsupportedPreview(null, L.T(L.Preview_PdfParseFailed)); }
    }

    // ── Font ──
    private void ShowFontPreview(string filePath, ArchiveItem item)
    {
        try
        {
            var info = FontParser.Parse(filePath);
            if (info == null) { ShowUnsupportedPreview(item, L.T(L.Preview_FontParseFailed)); return; }
            var sampleText = AppSettings.Instance.FontPreviewSampleText;
            if (string.IsNullOrEmpty(sampleText)) sampleText = "fi ff fl ffi ffl AaBbCc 123 示例";
            _fontLigaturesEnabled = false;
            PreviewTextBox.Typography.StandardLigatures = false;
            PreviewTextBox.Typography.ContextualLigatures = false;
            PreviewTextBox.Typography.DiscretionaryLigatures = false;
            // 将 FontFamily 设为字体文件对应的字体，使样本用实际字体渲染。
            // 格式 "filePath#FamilyName" 是 WPF 加载字体的标准方式。
            try
            {
                var fontFilePath = info.FontDecompressedPath ?? filePath;
                var familyName = info.FontName ?? Path.GetFileNameWithoutExtension(filePath);
                PreviewTextBox.FontFamily = new FontFamily(fontFilePath + "#" + familyName);
            }
            catch (Exception ex)
            {
                App.LogDebug("ShowFontPreview: failed to load FontFamily: {0}", ex.Message);
            }
            PreviewTextBox.Text = sampleText;
            PreviewTextBox.FontSize = 18; PreviewTextBox.TextAlignment = TextAlignment.Center;
            HideAllPreviewControls();
            PreviewTextBox.Visibility = Visibility.Visible;
            SetPreviewInfo(item); PreviewHeader.Text = L.TF(L.Preview_FontHeader, info.AdditionalInfo ?? "Font");
            ShowPreviewPanel();
            SetFormatSpecificInfo(
                (L.T(L.Preview_FontName), info.FontName ?? "--"),
                (L.T(L.Preview_FontStyle), info.FontStyle ?? "Regular"),
                (L.T(L.Preview_FontGlyphs), info.GlyphCount?.ToString() ?? "--"));
            SetToolbar(
                Array.Empty<ToolbarButton>(),
                new[] {
                    new ToolbarButton { Text = "🔀", Tooltip = L.T(L.Preview_FontLigatures), IsToggle = true, IsChecked = false, OnClick = ToggleFontLigatures }
                }
            );
        }
        catch (Exception ex) { App.LogDebug("ShowFontPreview: failed: {0}", ex.Message); ShowUnsupportedPreview(null, L.T(L.Preview_FontParseFailed)); }
    }

    // ── Audio ──
    private void ShowAudioPreview(string filePath, ArchiveItem item, string formatName)
    {
        try
        {
            FileFormatInfo? info = formatName == "WAV" ? RiffParser.Parse(filePath) : FlacParser.Parse(filePath);
            if (info == null) { ShowUnsupportedPreview(item, L.T(L.Preview_AudioParseFailed)); return; }
            PreviewTextBox.Text = info.DisplayName;
            PreviewTextBox.TextAlignment = TextAlignment.Left; PreviewTextBox.FontSize = 12;
            HideAllPreviewControls();
            PreviewTextBox.Visibility = Visibility.Visible;
            SetPreviewInfo(item); PreviewHeader.Text = L.TF(L.Preview_AudioHeader, formatName, Path.GetFileName(filePath));
            ShowPreviewPanel();
            var extra = new List<(string, string)>();
            if (info.Duration.HasValue) extra.Add((L.T(L.Preview_AudioDuration), info.Duration.Value.ToString("hh\\:mm\\:ss")));
            if (info.SampleRate > 0) extra.Add((L.T(L.Preview_AudioSampleRate), $"{info.SampleRate} Hz"));
            if (info.Channels > 0) extra.Add((L.T(L.Preview_AudioChannels), info.Channels.Value.ToString()));
            if (info.Bitrate > 0) extra.Add((L.T(L.Preview_AudioBitrate), $"{info.Bitrate} kbps"));
            SetFormatSpecificInfo(extra.ToArray()); SetToolbar(Array.Empty<ToolbarButton>(), Array.Empty<ToolbarButton>());
        }
        catch (Exception ex) { App.LogDebug("ShowAudioPreview: failed: {0}", ex.Message); ShowUnsupportedPreview(null, L.T(L.Preview_AudioParseFailed)); }
    }

    // ── SQLite ──
    private void ShowSqlitePreview(string filePath, ArchiveItem item)
    {
        try
        {
            var info = SQLiteParser.Parse(filePath);
            if (info == null) { ShowUnsupportedPreview(item, L.T(L.Preview_SqliteParseFailed)); return; }
            PreviewTextBox.Text = info.DisplayName;
            if (info.TableCount > 0) PreviewTextBox.Text += $"\n表数量: {info.TableCount}";
            PreviewTextBox.TextAlignment = TextAlignment.Left; PreviewTextBox.FontSize = 12;
            HideAllPreviewControls();
            PreviewTextBox.Visibility = Visibility.Visible;
            SetPreviewInfo(item); PreviewHeader.Text = L.TF(L.Preview_SqliteHeader, Path.GetFileName(filePath));
            ShowPreviewPanel();
            SetFormatSpecificInfo((L.T(L.Preview_SqliteEncoding), info.TextEncoding ?? "--"), (L.T(L.Preview_SqlitePageSize), info.AdditionalInfo?.Replace("页大小: ", "") ?? "--"));
            SetToolbar(Array.Empty<ToolbarButton>(), Array.Empty<ToolbarButton>());
        }
        catch (Exception ex) { App.LogDebug("ShowSqlitePreview: failed: {0}", ex.Message); ShowUnsupportedPreview(null, L.T(L.Preview_SqliteParseFailed)); }
    }

    // ── ISO ──
    private void ShowIsoPreview(string filePath, ArchiveItem item)
    {
        try
        {
            var info = IsoParser.Parse(filePath);
            if (info == null) { ShowUnsupportedPreview(item, L.T(L.Preview_IsoParseFailed)); return; }
            PreviewTextBox.Text = info.DisplayName;
            if (info.VolumeLabel != null) PreviewTextBox.Text += $"\n卷标: {info.VolumeLabel}";
            if (info.DiskSize > 0) PreviewTextBox.Text += $"\n大小: {ArchiveItem.FormatSize(info.DiskSize.Value)}";
            PreviewTextBox.TextAlignment = TextAlignment.Left; PreviewTextBox.FontSize = 12;
            HideAllPreviewControls();
            PreviewTextBox.Visibility = Visibility.Visible;
            SetPreviewInfo(item); PreviewHeader.Text = L.TF(L.Preview_IsoHeader, info.AdditionalInfo ?? "ISO 9660", Path.GetFileName(filePath));
            ShowPreviewPanel();
            SetFormatSpecificInfo(
                (L.T(L.Preview_IsoVolume), info.VolumeLabel ?? "--"),
                (L.T(L.Preview_IsoFormat), info.AdditionalInfo ?? "ISO 9660"));
            SetToolbar(Array.Empty<ToolbarButton>(), Array.Empty<ToolbarButton>());
        }
        catch (Exception ex) { App.LogDebug("ShowIsoPreview: failed: {0}", ex.Message); ShowUnsupportedPreview(null, L.T(L.Preview_IsoParseFailed)); }
    }

    // ── Torrent ──
    private void ShowTorrentPreview(string filePath, ArchiveItem item)
    {
        try
        {
            var info = TorrentParser.Parse(filePath);
            if (info == null) { ShowUnsupportedPreview(item, L.T(L.Preview_TorrentParseFailed)); return; }
            var sb = new StringBuilder();
            sb.Append(L.T(L.Preview_TorrentFiles)).AppendLine();
            if (info.TorrentFileName != null) sb.AppendLine($"  {info.TorrentFileName}/");
            if (info.TorrentTotalSize > 0) sb.AppendLine($"  总大小: {FormatSize(info.TorrentTotalSize!.Value)}");
            if (info.PieceCount > 0) sb.AppendLine($"  分片: {info.PieceCount} × {(info.PieceSize.HasValue ? FormatSize(info.PieceSize.Value) : "?")}");
            sb.AppendLine();
            BuildTorrentFileTree(info, sb);
            sb.AppendLine();
            if (info.MagnetLink != null) { sb.AppendLine(L.T(L.Preview_TorrentMagnet)); sb.Append(info.MagnetLink); }
            PreviewTextBox.Text = sb.ToString(); PreviewTextBox.FontSize = 12;
            PreviewTextBox.TextAlignment = TextAlignment.Left;
            HideAllPreviewControls();
            PreviewTextBox.Visibility = Visibility.Visible;
            SetPreviewInfo(item); PreviewHeader.Text = L.TF(L.Preview_TorrentHeader, info.TorrentFileName ?? Path.GetFileName(filePath));
            ShowPreviewPanel();
            var extra = new List<(string, string)> { (L.T(L.Preview_TorrentInfoHash), info.InfoHashV1 ?? "--") };
            if (info.CreationDate != null) extra.Add((L.T(L.Preview_TorrentCreationDate), info.CreationDate.Value.ToString("yyyy-MM-dd HH:mm:ss")));
            if (info.TrackerUrl != null) extra.Add((L.T(L.Preview_TorrentTracker), info.TrackerUrl!));
            if (info.TrackerCount > 1) extra.Add((L.T(L.Preview_TorrentTrackerCount), info.TrackerCount.Value.ToString()));
            if (info.CreatedBy != null) extra.Add((L.T(L.Preview_TorrentCreatedBy), info.CreatedBy!));
            if (info.IsPrivate == true) extra.Add((L.T(L.Preview_TorrentPrivate), "是"));
            if (!string.IsNullOrEmpty(info.AdditionalInfo)) extra.Add((L.T(L.Preview_TorrentComment), info.AdditionalInfo!));
            SetFormatSpecificInfo(extra.ToArray()); SetToolbar(Array.Empty<ToolbarButton>(), Array.Empty<ToolbarButton>());
        }
        catch (Exception ex) { App.LogDebug("ShowTorrentPreview: failed: {0}", ex.Message); ShowUnsupportedPreview(null, L.T(L.Preview_TorrentParseFailed)); }
    }

    private static void BuildTorrentFileTree(FileFormatInfo info, StringBuilder sb)
    {
        if (info.TorrentFileEntries == null || info.TorrentFileEntries.Count == 0)
        { sb.AppendLine($"  ({info.TorrentFileName ?? "?"})"); return; }
        var root = new Dictionary<string, object>();
        foreach (var (path, size) in info.TorrentFileEntries)
        {
            var parts = path.Split('/'); var current = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!current.TryGetValue(parts[i], out var child)) { var sub = new Dictionary<string, object>(); current[parts[i]] = sub; child = sub; }
                current = (Dictionary<string, object>)child!;
            }
            current[parts[^1]] = size;
        }
        RenderTree(sb, root, "");
    }

    private static void RenderTree(StringBuilder sb, Dictionary<string, object> node, string indent)
    {
        var items = node.ToArray();
        for (int i = 0; i < items.Length; i++)
        {
            bool isLast = i == items.Length - 1;
            string prefix = isLast ? "└── " : "├── ";
            string childIndent = isLast ? "    " : "│   ";
            if (items[i].Value is Dictionary<string, object> sub)
            { sb.AppendLine($"{indent}{prefix}[DIR] {items[i].Key}/"); RenderTree(sb, sub, indent + childIndent); }
            else
            { sb.AppendLine($"{indent}{prefix}{items[i].Key}  ({ArchiveItem.FormatSize((long)items[i].Value)})"); }
        }
    }

    // ── Office ──
    private void ShowOfficePreview(string filePath, ArchiveItem item)
    {
        try
        {
            var info = OfficeParser.Parse(filePath);
            if (info == null) { ShowUnsupportedPreview(item, L.T(L.Preview_OfficeParseFailed)); return; }
            PreviewTextBox.Text = info.DisplayName;
            if (info.Title != null) PreviewTextBox.Text += $"\n标题: {info.Title}";
            if (info.Author != null) PreviewTextBox.Text += $"\n作者: {info.Author}";
            if (info.PageCount > 0) PreviewTextBox.Text += $"\n{(filePath.EndsWith(".pptx") ? "幻灯片" : filePath.EndsWith(".xlsx") ? "工作表" : "页数")}: {info.PageCount}";
            PreviewTextBox.TextAlignment = TextAlignment.Left; PreviewTextBox.FontSize = 12;
            HideAllPreviewControls();
            PreviewTextBox.Visibility = Visibility.Visible;
            SetPreviewInfo(item); PreviewHeader.Text = info.DisplayName;
            ShowPreviewPanel();
            var extra = new List<(string, string)>();
            if (info.Title != null) extra.Add((L.T(L.Preview_DocTitle), info.Title));
            if (info.Author != null) extra.Add((L.T(L.Preview_DocAuthor), info.Author));
            if (info.PageCount > 0) { string label = filePath.EndsWith(".pptx") ? L.T(L.Preview_DocSlides) : filePath.EndsWith(".xlsx") ? L.T(L.Preview_DocSheets) : L.T(L.Preview_DocPages); extra.Add((label, info.PageCount.Value.ToString())); }
            if (info.CreationDate.HasValue) extra.Add((L.T(L.Preview_DocCreated), info.CreationDate.Value.ToString("yyyy-MM-dd HH:mm")));
            if (info.ModifiedDate.HasValue) extra.Add((L.T(L.Preview_DocModified), info.ModifiedDate.Value.ToString("yyyy-MM-dd HH:mm")));
            SetFormatSpecificInfo(extra.ToArray()); SetToolbar(Array.Empty<ToolbarButton>(), Array.Empty<ToolbarButton>());
        }
        catch (Exception ex) { App.LogDebug("ShowOfficePreview: failed: {0}", ex.Message); ShowUnsupportedPreview(null, L.T(L.Preview_OfficeParseFailed)); }
    }

    // ── SVG ──
    private async Task ShowSvgPreview(string filePath, ArchiveItem item)
    {
        try
        {
            await EnsureWebView2InitializedAsync();
            HideAllPreviewControls();
            PreviewWebView2.CoreWebView2.Navigate(new Uri(filePath).AbsoluteUri);
            PreviewWebView2.Visibility = Visibility.Visible;
            SetPreviewInfo(item); PreviewHeader.Text = L.TF(L.Preview_ImageHeader, Path.GetFileName(filePath));
            ShowPreviewPanel(); SetFormatSpecificInfo(("SVG", Path.GetFileName(filePath))); SetToolbar(Array.Empty<ToolbarButton>(), Array.Empty<ToolbarButton>());
        }
        catch (Exception ex) { App.LogDebug("ShowSvgPreview: failed: {0}", ex.Message); ShowUnsupportedPreview(null, "SVG 预览失败"); }
    }

    // ── Video ──
    private void ShowVideoPreview(string filePath, ArchiveItem item)
    {
        try
        {
            var info = VideoParser.Parse(filePath);
            if (info == null) { ShowUnsupportedPreview(item, L.T(L.Preview_VideoParseFailed)); return; }
            var sb = new StringBuilder(); sb.AppendLine(info.DisplayName);
            if (info.VideoWidth > 0 && info.VideoHeight > 0) sb.AppendLine($"  分辨率: {info.VideoWidth} × {info.VideoHeight}");
            if (info.Duration.HasValue) sb.AppendLine($"  时长: {info.Duration.Value:hh\\:mm\\:ss}");
            if (info.Codec != null) sb.AppendLine($"  编码: {info.Codec}");
            PreviewTextBox.Text = sb.ToString(); PreviewTextBox.TextAlignment = TextAlignment.Left; PreviewTextBox.FontSize = 12;
            HideAllPreviewControls();
            PreviewTextBox.Visibility = Visibility.Visible;
            SetPreviewInfo(item); PreviewHeader.Text = info.DisplayName;
            ShowPreviewPanel();
            var extra = new List<(string, string)>();
            if (info.VideoWidth > 0 && info.VideoHeight > 0) extra.Add((L.T(L.Preview_Dimensions), $"{info.VideoWidth} × {info.VideoHeight}"));
            if (info.Duration.HasValue) extra.Add((L.T(L.Preview_VideoDuration), info.Duration.Value.ToString("hh\\:mm\\:ss")));
            if (info.Codec != null) extra.Add((L.T(L.Preview_VideoCodec), info.Codec));
            SetFormatSpecificInfo(extra.ToArray()); SetToolbar(Array.Empty<ToolbarButton>(), Array.Empty<ToolbarButton>());
        }
        catch (Exception ex) { App.LogDebug("ShowVideoPreview: failed: {0}", ex.Message); ShowUnsupportedPreview(null, L.T(L.Preview_VideoParseFailed)); }
    }

    #endregion
}
