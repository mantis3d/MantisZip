using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Dynamic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using Markdig;
using Markdig.Extensions.Emoji;
using Ude;
using WpfAnimatedGif;
using Microsoft.Web.WebView2.Core;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

public partial class MainWindow
{
    #region 文件预览

    private bool _webView2Crashed;
    private bool _webView2Initialized;  // 跟踪是否已订阅事件，避免重复订阅

    // Markdown 预览状态（用于 emoji 切换后重新渲染）
    private bool _markdownEmojiEnabled;
    private string? _cachedMarkdownPath;
    private ArchiveItem? _cachedMarkdownItem;
    private static Dictionary<string, string>? _emojiMapping;  // 缓存 emoji 映射表
    private int _markdownPreviewFontSize = 14;  // Markdown 预览字号基准

    // 源码/渲染切换
    private enum PreviewSourceFormat { None, Markdown, Html }
    private PreviewSourceFormat _previewSourceFormat;
    private bool _previewShowSource;
    private string? _cachedHtmlPath;
    private ArchiveItem? _cachedHtmlItem;

    private ZoomMode _currentZoomMode = ZoomMode.FitWindow;
    private static readonly SolidColorBrush _toolbarCheckedBrush = new(Color.FromArgb(30, 100, 100, 100));

    // 魔数路由状态
    private ArchiveItem? _currentPreviewItem;
    private FileFormat? _previewFormatOverride; // null=用 detectedFormat, non-null=用此值覆盖
    private FileFormat _previewExtFormat; // 扩展名对应的格式（供切换按钮回调使用）
    private ToolbarButton[]? _toolbarConflictButtons; // 冲突时的切换按钮，SetToolbar 自动拾取

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".webp"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".ini", ".cfg", ".conf", ".xml", ".json",
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

    private static readonly HashSet<string> Mp3Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3"
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

    private static readonly HashSet<string> CsvExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".flv"
    };

    /// <summary>只需读取文件头的格式，不受 MaxPreviewFileSize 限制。</summary>
    private static readonly HashSet<string> MetadataOnlyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".ocx",
        ".pdf", ".docx", ".xlsx", ".pptx",
        ".wav", ".flac", ".mp3",
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
    /// 通过 WebResourceRequested 事件阻止页面级别的外部网络请求。
    /// 注意：WebView2 运行时自身的初始化联网（SmartScreen/遥测/组件更新）无法通过这些方式拦截。
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

        // 已经完成过一次性设置（事件订阅、安全配置），无需重复
        if (_webView2Initialized)
            return;

        try
        {
            // 订阅浏览器进程崩溃事件（仅一次，CoreWebView2 对象在进程重启后保持不变）
            PreviewWebView2.CoreWebView2!.ProcessFailed += OnWebView2ProcessFailed;

            // 安全配置：禁止右键菜单、禁用密码/表单自动填充
            PreviewWebView2.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            PreviewWebView2.CoreWebView2.Settings.IsScriptEnabled = true;
            PreviewWebView2.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            PreviewWebView2.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;

            // 隐藏 PDF 工具栏中不必要的操作按钮（点击设置按钮会导致浏览器进程崩溃）
            PreviewWebView2.CoreWebView2.Settings.HiddenPdfToolbarItems =
                CoreWebView2PdfToolbarItems.Save |
                CoreWebView2PdfToolbarItems.Print |
                CoreWebView2PdfToolbarItems.SaveAs |
                CoreWebView2PdfToolbarItems.MoreSettings |
                CoreWebView2PdfToolbarItems.FullScreen;

            // 阻止页面级别的外部网络请求（http/https），不影响 WebView2 运行时自身的联网行为
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

            _webView2Initialized = true;
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
        PreviewCsvGrid.ItemsSource = null;
        PreviewWebView2.Visibility = Visibility.Collapsed;
                ShowUnsupportedPreview(null, "预览组件异常，请重新选择文件以恢复");
            }
        });
    }

    private void HideAllPreviewControls()
    {
        PreviewImageScroll.Visibility = Visibility.Collapsed;
        PreviewTextBox.Visibility = Visibility.Collapsed;
        PreviewFileIcon.Visibility = Visibility.Collapsed;
        PreviewCsvGrid.Visibility = Visibility.Collapsed;
        PreviewTabularContainer.Visibility = Visibility.Collapsed;
        PreviewUnsupportedPanel.Visibility = Visibility.Collapsed;
        PreviewWebView2.Visibility = Visibility.Collapsed;
    }

    private async Task ShowPreviewAsync(ArchiveItem item)
    {
        _currentPreviewItem = item;
        // 新文件选择时重置格式覆盖状态（避免切换文件后仍沿用之前的切换选择）
        _previewFormatOverride = null;
        _toolbarConflictButtons = null;
        // 取消并释放上一次的 CancellationTokenSource，避免资源泄漏
        if (_previewCts != null)
        {
            _previewCts.Cancel();
            _previewCts.Dispose();
        }
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

            // 魔数检测：通过文件头部字节识别真实格式（即使扩展名缺失或错误）
            string? realFormatName = null;
            FileFormat detectedFormat = FileFormat.Unknown;
            if (s.EnableFormatDetection && _currentArchivePath != null)
            {
                try
                {
                    var head = await ArchiveEntryExtractor.ExtractHeadAsync(
                        _currentArchivePath!, item.Name,
                        AppSettings.Instance.PreviewHeadSize,
                        _currentFormat, _currentPassword, ct);
                    if (head != null && head.Length > 0)
                    {
                        detectedFormat = FileFormatDetector.Detect(head, head.Length);
                        if (detectedFormat != FileFormat.Unknown)
                        {
                            realFormatName = FileFormatHelper.GetDisplayName(detectedFormat);
                        }
                        else
                        {
                            // 魔数未匹配时，用扩展名回退猜测（对无魔数的纯文本格式有效）
                            var extFallback = FileFormatDetector.DetectByExtension(ext);
                            if (extFallback != FileFormat.Unknown)
                            {
                                realFormatName = FileFormatHelper.GetDisplayName(extFallback);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    App.LogDebug("ShowPreviewAsync: magic detection failed: {0}", ex.Message);
                }
            }

            // 各格式方法控制自己的标题，魔数结果放到信息栏
            PreviewHeader.Text = $"📄 {item.Name}";

            // 魔数结果写到信息栏（格式方法若调用 SetFormatSpecificInfo 会覆盖此行，是预期行为）
            _previewExtFormat = FileFormat.Unknown;
            if (realFormatName != null)
            {
                _previewExtFormat = FileFormatDetector.DetectByExtension(ext);
                var secondaryBrush = (Brush)FindResource("Theme_TextSecondary");
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                row.Children.Add(new TextBlock { Text = "格式: ", FontSize = 11, Foreground = secondaryBrush, FontWeight = FontWeights.SemiBold });
                var valueBlock = new TextBlock { Text = realFormatName, FontSize = 11, Foreground = secondaryBrush, TextWrapping = TextWrapping.Wrap };
                bool hasConflict = _previewExtFormat != FileFormat.Unknown && _previewExtFormat != detectedFormat;
                if (hasConflict)
                {
                    valueBlock.Foreground = (Brush)FindResource("Theme_Accent");
                    valueBlock.Text = $"⚠️ {realFormatName}（扩展名: {ext}）";
                }
                row.Children.Add(valueBlock);
                PreviewExtraInfoPanel.Children.Add(row);
                PreviewExtraInfoPanel.Visibility = Visibility.Visible;
            }

            // 冲突切换按钮：在格式方法 SetToolbar 前设置，方法内部会自动拾取
            if (detectedFormat != FileFormat.Unknown
                && _previewExtFormat != FileFormat.Unknown
                && _previewExtFormat != detectedFormat)
            {
                bool usingMagic = _previewFormatOverride == null;
                var targetFormat = usingMagic ? _previewExtFormat : detectedFormat;
                var targetName = FileFormatHelper.GetDisplayName(targetFormat);
                var currentName = usingMagic ? realFormatName : FileFormatHelper.GetDisplayName(_previewExtFormat);

                _toolbarConflictButtons = new[]
                {
                    new ToolbarButton
                    {
                        Text = usingMagic ? $"按扩展名({ext})" : $"按魔数",
                        Tooltip = $"当前: {currentName}，点击切换为 {targetName}",
                        IsToggle = true,
                        IsChecked = !usingMagic,
                        OnClick = () =>
                        {
                            _previewFormatOverride = usingMagic ? _previewExtFormat : null;
                            // 重新预览（异步触发）
                            _ = ShowPreviewAsync(_currentPreviewItem!);
                        }
                    }
                };
            }

            // 魔数优先路由（扩展名回退仅在魔数 Unknown 时生效）
            if (detectedFormat != FileFormat.Unknown)
            {
                var effectiveFormat = _previewFormatOverride ?? detectedFormat;
                // 将扩展名映射为魔数格式的标准扩展名，使格式方法（工具栏、缩放、切换等）能正确识别
                var magicExt = MapFileFormatToExtension(effectiveFormat);
                if (magicExt != null)
                    ext = magicExt;
                if (await TryMagicPreview(effectiveFormat, item, ext, ct, _previewExtFormat))
                    return;
                // TryMagicPreview 无对应路由 → 继续走扩展名链（此时 ext 已是魔数映射后的值）
            }

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
            else if (CsvExtensions.Contains(ext))
            {
                var tempFile = await ExtractPreviewFileAsync(item, "preview.csv", ct);
                ShowCsvPreview(tempFile, item);
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
            else if (Mp3Extensions.Contains(ext))
            {
                var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
                ShowMp3Preview(tempFile, item);
            }
            else if (SqliteExtensions.Contains(ext))
            {
                var tempFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
                await ShowSqlitePreview(tempFile, item);
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
                // 扩展名（含魔数映射回退）未匹配任何已知格式
                ShowUnsupportedPreview(item);
            }
        }
        catch (OperationCanceledException)
        {
            // 用户切换文件导致旧预览被取消，静默忽略
        }
        catch (Exception ex)
        {
            CoreLog.Trace("ShowPreviewAsync: failed: {0}", ex.Message);
            ShowUnsupportedPreview(item, L.TF(L.Preview_Failed, ex.Message));
        }
        finally
        {
            HidePreviewLoading();
        }
    }

    /// <summary>
    /// 魔数优先路由：根据 detectedFormat 选择预览方法。
    /// 返回 true 表示已处理预览，false 表示该格式无对应预览方法（调用方应走扩展名回退）。
    /// </summary>
    private async Task<bool> TryMagicPreview(
        FileFormat format, ArchiveItem item, string ext, CancellationToken ct, FileFormat extFormat)
    {
        var s = AppSettings.Instance;

        switch (format)
        {
            // ── 位图图像 ──
            case FileFormat.Png or FileFormat.Jpeg or FileFormat.Gif
                 or FileFormat.Bmp or FileFormat.WebP or FileFormat.Ico
                 or FileFormat.Tga or FileFormat.Hdr or FileFormat.Exr:
                if (!s.EnableImagePreview)
                {
                    HidePreviewLoading();
                    ShowUnsupportedPreview(item, L.T(L.Preview_ImageDisabled));
                    return true;
                }
                var imgFile = await ExtractPreviewFileAsync(item, "preview" + ext, ct);
                await ShowImagePreviewAsync(imgFile, item);
                return true;

            // ── SVG ──
            case FileFormat.Svg:
                if (!s.EnableImagePreview)
                {
                    HidePreviewLoading();
                    ShowUnsupportedPreview(item, L.T(L.Preview_ImageDisabled));
                    return true;
                }
                var svgFile = await ExtractPreviewFileAsync(item, "preview.svg", ct);
                await ShowSvgPreview(svgFile, item);
                return true;

            // ── 纯文本 ──
            case FileFormat.Text:
                if (!s.EnableTextPreview)
                {
                    ShowUnsupportedPreview(item, L.T(L.Preview_TextDisabled));
                    return true;
                }
                if (item.Size > s.MaxTextPreviewBytes)
                {
                    var limitMb = s.MaxTextPreviewBytes / (1024.0 * 1024.0);
                    ShowUnsupportedPreview(item, L.TF(L.Preview_TooLargeText, (double)item.Size / 1024 / 1024, limitMb));
                    return true;
                }
                var txtFile = await ExtractPreviewFileAsync(item, "preview.txt", ct);
                ShowTextPreview(txtFile, ext, item);
                return true;

            // ── HTML/Markdown ──
            case FileFormat.Html or FileFormat.Markdown:
                if (!s.EnableTextPreview)
                {
                    ShowUnsupportedPreview(item, L.T(L.Preview_TextDisabled));
                    return true;
                }
                if (item.Size > s.MaxTextPreviewBytes)
                {
                    var limitMb = s.MaxTextPreviewBytes / (1024.0 * 1024.0);
                    ShowUnsupportedPreview(item, L.TF(L.Preview_TooLargeText, (double)item.Size / 1024 / 1024, limitMb));
                    return true;
                }
                var webFile = await ExtractPreviewFileAsync(item, "preview.html", ct);
                if (format == FileFormat.Markdown)
                    await ShowMarkdownPreview(webFile, item);
                else
                    await ShowHtmlPreview(webFile, item);
                return true;

            // ── 其他检测到的格式无专用预览方法 → 返回 false 走扩展名回退 ──
            default:
                return false;
        }
    }

    /// <summary>
    /// 将魔数检测的 FileFormat 映射到标准扩展名，用于魔数识别后走扩展名链路由。
    /// 返回 null 表示该格式无标准扩展名或无需走扩展名路由。
    /// </summary>
    private static string? MapFileFormatToExtension(FileFormat format)
    {
        return format switch
        {
            // 图像
            FileFormat.Png => ".png",
            FileFormat.Jpeg => ".jpg",
            FileFormat.Gif => ".gif",
            FileFormat.Bmp => ".bmp",
            FileFormat.WebP => ".webp",
            FileFormat.Ico => ".ico",
            FileFormat.Tga => ".tga",
            FileFormat.Hdr => ".hdr",
            FileFormat.Exr => ".exr",
            FileFormat.Svg => ".svg",
            // 文本/标记
            FileFormat.Text => ".txt",
            FileFormat.Html => ".html",
            FileFormat.Markdown => ".md",
            // 档案格式（扩展名链回退）
            FileFormat.Torrent => ".torrent",
            FileFormat.Sqlite => ".sqlite",
            FileFormat.Pe => ".exe",
            FileFormat.Pdf => ".pdf",
            FileFormat.Ttf => ".ttf",
            FileFormat.Otf => ".otf",
            FileFormat.Woff => ".woff",
            FileFormat.Wav => ".wav",
            FileFormat.Flac => ".flac",
            FileFormat.Mp3 => ".mp3",
            FileFormat.Ogg => ".ogg",
            FileFormat.Iso => ".iso",
            FileFormat.Docx => ".docx",
            FileFormat.Xlsx => ".xlsx",
            FileFormat.Pptx => ".pptx",
            FileFormat.Epub => ".epub",
            FileFormat.Woff2 => ".woff2",
            FileFormat.Mp4 => ".mp4",
            FileFormat.Mkv => ".mkv",
            FileFormat.WebM => ".webm",
            FileFormat.Wmv => ".wmv",
            FileFormat.Mov => ".mov",
            FileFormat.Avi => ".avi",
            FileFormat.Flv => ".flv",
            FileFormat.Dicom => ".dcm",
            FileFormat.Cer => ".cer",
            FileFormat.Lnk => ".lnk",
            FileFormat.Pfx => ".pfx",
            FileFormat.Subtitle => ".srt",
            _ => null
        };
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
            PreviewUnsupportedIcon.Source = icon;
            PreviewUnsupportedIcon.Visibility = Visibility.Visible;
            SetPreviewInfo(item);
        }
        else
        {
            PreviewUnsupportedIcon.Visibility = Visibility.Collapsed;
        }

        PreviewUnsupportedText.Text = !string.IsNullOrEmpty(message)
            ? message
            : L.T(L.Preview_Unsupported);
        PreviewUnsupportedPanel.Visibility = Visibility.Visible;
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
            var displayMode = !string.IsNullOrEmpty(_currentArchivePath)
                ? GetCompressedDisplayMode(_currentArchivePath, _currentFormat)
                : ArchiveItem.CompressedDisplayMode.Normal;
            var (compressedStr, ratio) = displayMode switch
            {
                ArchiveItem.CompressedDisplayMode.Unavailable => ("---", "---"),
                ArchiveItem.CompressedDisplayMode.NotCompressed => (sizeStr, "100%"),
                _ => (
                    ArchiveItem.FormatSize(stat.CompressedSize),
                    stat.Size > 0 ? $"{(double)stat.CompressedSize / stat.Size * 100:F1}%" : "--")
            };
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
        int fileCount = _allItems.Count(i => !i.IsDirectory);
        int dirCount = _allItems.Count(i => i.IsDirectory);

        // 对于无法获取逐项压缩后大小的格式，用实际文件大小作为总压缩大小
        var anyUnavailable = _allItems.Any(i => i.CompressedDisplay == ArchiveItem.CompressedDisplayMode.Unavailable);
        var totalCompressed = anyUnavailable
            ? new FileInfo(_currentArchivePath).Length
            : _allItems.Sum(i => i.CompressedSize);

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
        PreviewImageScroll.Visibility = Visibility.Collapsed;
        PreviewWebView2.Visibility = Visibility.Collapsed;
        PreviewUnsupportedPanel.Visibility = Visibility.Collapsed;

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
        PreviewImage.ClearValue(Image.StretchProperty); // 还原默认拉伸，避免字体 GlyphRun 渲染时设置的 Stretch=None 影响后续图片预览
        PreviewImage.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
        PreviewImage.ClearValue(FrameworkElement.VerticalAlignmentProperty);
        ImageBehavior.SetAnimatedSource(PreviewImage, null); // 停止 GIF 动画
        _gifController?.Dispose();
        _gifController = null;
        PreviewImageScroll.Visibility = Visibility.Collapsed;
        PreviewFileIcon.Source = null;
        PreviewTextBox.Text = "";
        PreviewTextBox.ClearValue(TextBox.FontFamilyProperty); // 重置为默认字体，释放对临时字体文件的引用，防止 WPF 在文件被删除后崩溃
        PreviewCsvGrid.ItemsSource = null;
        PreviewTabularContainer.Children.Clear();
        if (PreviewWebView2.CoreWebView2 != null)
            PreviewWebView2.CoreWebView2.Navigate("about:blank");
        PreviewWebView2.Visibility = Visibility.Collapsed;
        _previewShowSource = false;
        _previewSourceFormat = PreviewSourceFormat.None;
        PreviewFormatInfo.Text = "";
        PreviewFileNameText.Text = "";
        PreviewSizeText.Text = "";
        PreviewRatioText.Text = "";
        PreviewCompressedText.Text = "";
        PreviewDateText.Text = "";
        PreviewEncryptedText.Text = "";
        _icoOriginalFrames = null;
        _icoImages = null;
        _icoBorders = null;

        // 清理棋盘格背景视觉（不重置 _transparentBgEnabled 标志位）
        if (PreviewImageScroll.Parent is Panel bgPanel)
            bgPanel.Background = Brushes.Transparent;
    }

    private void HidePreview()
    {
        // 只在预览面板可见时保存大小，避免重复 HidePreview 覆盖已保存的值
        if (PreviewPanel.Visibility == Visibility.Visible)
            SaveCurrentPreviewSize(AppSettings.Instance.PreviewPosition);

        PreviewImage.Source = null;
        ImageBehavior.SetAnimatedSource(PreviewImage, null); // 停止 GIF 动画
        PreviewImageScroll.Visibility = Visibility.Collapsed;
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
                // 重试最多 5 次，间隔 200ms，给 WPF 图片管线释放文件句柄的时间
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        Directory.Delete(_previewTempDir!, recursive: true);
                        _previewTempDir = null;
                        return;
                    }
                    catch (Exception) when (i < 4)
                    {
                        CoreLog.Trace("ExtractPreviewFileAsync: attempt {0} failed", i + 1);
                        Thread.Sleep(200);
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                }
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
        public Func<Task>? OnClickAsync { get; set; }
    }

    private void SetToolbar(ToolbarButton[] leftButtons, ToolbarButton[] rightButtons)
    {
        PreviewToolbarPanel.Children.Clear();
        bool hasLeft = leftButtons.Length > 0;
        bool hasMiddle = _toolbarConflictButtons is { Length: > 0 };
        bool hasRight = rightButtons.Length > 0;
        if (!hasLeft && !hasMiddle && !hasRight) { PreviewToolbarBorder.Visibility = Visibility.Collapsed; return; }
        foreach (var btn in leftButtons) PreviewToolbarPanel.Children.Add(CreateToolbarButtonElement(btn));
        if (hasMiddle)
        {
            if (hasLeft) AddToolbarSeparator();
            foreach (var btn in _toolbarConflictButtons!) PreviewToolbarPanel.Children.Add(CreateToolbarButtonElement(btn));
        }
        if (hasRight)
        {
            if (hasLeft || hasMiddle) AddToolbarSeparator();
            foreach (var btn in rightButtons) PreviewToolbarPanel.Children.Add(CreateToolbarButtonElement(btn));
        }
        PreviewToolbarBorder.Visibility = Visibility.Visible;
    }

    private static readonly ConcurrentDictionary<string, BitmapSource> _emojiCache = new();

    private static BitmapSource RenderEmoji(string text, double fontSize)
    {
        return _emojiCache.GetOrAdd($"{text}_{fontSize}", _ =>
        {
            var typeface = new Typeface("Segoe UI Emoji");
            var formattedText = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black,
                1.0);

            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
                ctx.DrawText(formattedText, new Point(0, 0));

            var bounds = visual.ContentBounds;
            int w = Math.Max(1, (int)Math.Ceiling(bounds.Width));
            int h = Math.Max(1, (int)Math.Ceiling(bounds.Height));
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        });
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
        };

        // 渲染 emoji（补充平面字符）为彩色图片
        bool isEmoji = btn.Text.Any(c => c > 0xFFFF);
        if (isEmoji)
        {
            border.Child = new Image
            {
                Source = RenderEmoji(btn.Text, 16),
                Width = 22,
                Height = 22,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(4, 1, 4, 1),
            };
        }
        else
        {
            border.Child = new TextBlock { Text = btn.Text, FontSize = 13, Padding = new Thickness(6, 3, 6, 3) };
        }

        if (btn.IsToggle)
        {
            border.MouseLeftButtonUp += async (_, _) =>
            {
                btn.IsChecked = !btn.IsChecked;
                border.Background = btn.IsChecked ? _toolbarCheckedBrush : Brushes.Transparent;
                try
                {
                    if (btn.OnClickAsync != null)
                        await btn.OnClickAsync();
                    else
                        btn.OnClick?.Invoke();
                }
                catch (Exception ex) { App.TraceLog("ToolbarButton.OnClick failed: {0}", ex.Message); }
            };
            if (btn.IsChecked) border.Background = _toolbarCheckedBrush;
        }
        else
        {
            border.MouseLeftButtonUp += async (_, _) =>
            {
                try
                {
                    if (btn.OnClickAsync != null)
                        await btn.OnClickAsync();
                    else
                        btn.OnClick?.Invoke();
                }
                catch (Exception ex) { App.TraceLog("ToolbarButton.OnClick failed: {0}", ex.Message); }
            };
        }
        return border;
    }

    /// <summary>
    /// 更新缩放按钮的选中状态，始终只有当前模式对应的按钮高亮。
    /// </summary>
    private void UpdateZoomButtonStates()
    {
        int idx = 0;
        foreach (var child in PreviewToolbarPanel.Children)
        {
            if (child is Border border && idx < 3)
            {
                bool isChecked = idx switch
                {
                    0 => _currentZoomMode == ZoomMode.FitWindow,
                    1 => _currentZoomMode == ZoomMode.Zoom100,
                    2 => _currentZoomMode == ZoomMode.FitWidth,
                    _ => false
                };
                border.Background = isChecked ? _toolbarCheckedBrush : Brushes.Transparent;
                idx++;
            }
        }
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
        _currentZoomMode = mode;

        // 将像素尺寸按 DPI 换算为 WPF 设备无关单位，防止 DPI ≠ 96 时图片被裁剪
        double dpiScaleX = bmp.DpiX > 0 ? 96.0 / bmp.DpiX : 1.0;
        double dpiScaleY = bmp.DpiY > 0 ? 96.0 / bmp.DpiY : 1.0;
        double naturalWidth = bmp.PixelWidth * dpiScaleX;
        double naturalHeight = bmp.PixelHeight * dpiScaleY;

        switch (mode)
        {
            case ZoomMode.FitWindow:
                PreviewImageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                PreviewImageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                PreviewImage.Stretch = Stretch.Uniform;
                PreviewImage.MaxWidth = naturalWidth;
                PreviewImage.MaxHeight = naturalHeight;
                PreviewImage.HorizontalAlignment = HorizontalAlignment.Center;
                PreviewImage.VerticalAlignment = VerticalAlignment.Center;
                break;
            case ZoomMode.Zoom100:
                PreviewImageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                PreviewImageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                PreviewImage.Stretch = Stretch.None;
                PreviewImage.MaxWidth = double.PositiveInfinity;
                PreviewImage.MaxHeight = double.PositiveInfinity;
                PreviewImage.HorizontalAlignment = HorizontalAlignment.Left;
                PreviewImage.VerticalAlignment = VerticalAlignment.Top;
                break;
            case ZoomMode.FitWidth:
                PreviewImageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                PreviewImageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                PreviewImage.Stretch = Stretch.Uniform;
                PreviewImage.MaxWidth = naturalWidth;
                PreviewImage.MaxHeight = double.PositiveInfinity;
                PreviewImage.HorizontalAlignment = HorizontalAlignment.Center;
                PreviewImage.VerticalAlignment = VerticalAlignment.Center;
                break;
        }
        UpdateZoomButtonStates();
    }
    #endregion
}
