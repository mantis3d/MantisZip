using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

public partial class MainWindow
{
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

            var fontFilePath = info.FontDecompressedPath ?? filePath;
            var familyName = info.FontName ?? Path.GetFileNameWithoutExtension(filePath);

            App.LogDebug("ShowFontPreview: fontFilePath={0}, familyName={1}", fontFilePath, familyName);

            // 三层回退策略：
            //   1. "path#FamilyName" — TrueType TTF 支持，无需额外依赖
            //   2. FontFamily(directory, name) — WPF 通过 DirectWrite 扫描目录加载，支持 CFF-OTF
            //   3. GlyphTypeface → GlyphRun → RenderTargetBitmap — 绝对兼容的纯渲染回退
            bool fontLoaded = false;
            bool glyphRendered = false;

            // ─── 尝试 1：path#FamilyName ───
            try
            {
                var ff = new FontFamily(fontFilePath + "#" + familyName);
                var testTypeface = new Typeface(ff, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                fontLoaded = testTypeface.FontFamily.FamilyNames.Values
                    .Any(v => v.ToString().IndexOf(familyName, StringComparison.OrdinalIgnoreCase) >= 0);
                App.LogDebug("ShowFontPreview: tier 1 (# syntax) result={0}", fontLoaded);
            }
            catch (Exception ex1) { App.LogDebug("ShowFontPreview: tier 1 exception: {0}", ex1.Message); }

            if (fontLoaded)
            {
                PreviewTextBox.FontFamily = new FontFamily(fontFilePath + "#" + familyName);
            }
            else
            {
                // ─── 尝试 2：基于目录的 DirectWrite 加载 ───
                try
                {
                    var fontDir = Path.GetDirectoryName(fontFilePath);
                    if (fontDir != null)
                    {
                        var baseUri = new Uri(fontDir + "\\", UriKind.Absolute);
                        var ff2 = new FontFamily(baseUri, familyName);
                        var testTypeface2 = new Typeface(ff2, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                        if (testTypeface2.FontFamily.FamilyNames.Values
                            .Any(v => v.ToString().IndexOf(familyName, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            PreviewTextBox.FontFamily = ff2;
                            fontLoaded = true;
                        }
                        App.LogDebug("ShowFontPreview: tier 2 (dir scan) result={0}", fontLoaded);
                    }
                }
                catch (Exception ex2) { App.LogDebug("ShowFontPreview: tier 2 exception: {0}", ex2.Message); }

                // ─── 尝试 3：GlyphTypeface 渲染位图（绝对兼容） ───
                if (!fontLoaded)
                {
                    glyphRendered = ShowFontPreviewGlyphRender(fontFilePath, familyName, sampleText);
                    App.LogDebug("ShowFontPreview: tier 3 (GlyphRun) result={0}", glyphRendered);
                }
            }

            PreviewTextBox.Text = sampleText;
            PreviewTextBox.FontSize = AppSettings.Instance.FontPreviewFontSize; PreviewTextBox.TextAlignment = TextAlignment.Left;
            HideAllPreviewControls();
            if (glyphRendered)
            {
                PreviewImageScroll.Visibility = Visibility.Visible;
            }
            else
            {
                PreviewTextBox.Visibility = Visibility.Visible;
                // 所有加载方式均失败，在信息面板显示原因
                PreviewFormatInfo.Text = L.TF(L.Preview_FontLoadFailed, GetFontLoadFailureReason(fontFilePath));
                PreviewFormatInfo.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(255, 200, 120, 0)); // 橙色警告
            }
            SetPreviewInfo(item); PreviewHeader.Text = L.TF(L.Preview_FontHeader, info.AdditionalInfo ?? "Font");
            ShowPreviewPanel();
            SetFormatSpecificInfo(
                (L.T(L.Preview_FontName), info.FontName ?? "--"),
                (L.T(L.Preview_FontStyle), info.FontStyle ?? "Regular"),
                (L.T(L.Preview_FontGlyphs), info.GlyphCount?.ToString() ?? "--"));
            SetToolbar(Array.Empty<ToolbarButton>(), Array.Empty<ToolbarButton>());
            // 连字开关因 WPF Typography 对外部字体不生效已移除，后续方案见 #32
        }
        catch (Exception ex) { App.LogDebug("ShowFontPreview: failed: {0}", ex.Message); ShowUnsupportedPreview(null, L.T(L.Preview_FontParseFailed)); }
    }

    /// <summary>
    /// 最终回退：通过 GlyphTypeface + GlyphRun 直接渲染字体样本到位图，
    /// 绕过 WPF FontFamily 对 CFF 轮廓 OpenType 字体的限制。
    /// </summary>
    private bool ShowFontPreviewGlyphRender(string fontFilePath, string familyName, string sampleText)
    {
        try
        {
            var glyphTypeface = new GlyphTypeface(new Uri(fontFilePath));
            App.LogDebug("GlyphRender: loaded glyphTypeface, glyphCount={0}",
                glyphTypeface.GlyphCount);
            double fontSize = AppSettings.Instance.FontPreviewFontSize;
            double totalWidth = 0;

            var glyphIndices = new ushort[sampleText.Length];
            var advanceWidths = new double[sampleText.Length];
            for (int i = 0; i < sampleText.Length; i++)
            {
                ushort glyphIndex = glyphTypeface.CharacterToGlyphMap.TryGetValue(sampleText[i], out ushort idx)
                    ? (ushort)idx : (ushort)0;
                glyphIndices[i] = glyphIndex;
                // AdvanceWidths 单位为 em，乘以 fontSize 即得 DIP
                advanceWidths[i] = glyphTypeface.AdvanceWidths[glyphIndex] * fontSize;
                totalWidth += advanceWidths[i];
            }

            App.LogDebug("GlyphRender: glyphIndices.Length={0}, totalWidth={1:F1}", glyphIndices.Length, totalWidth);

            // GlyphRun 在 .NET 9 中内部 COM 对象未初始化导致构造失败，
            // 改用 GDI+ PrivateFontCollection 加载 CFF-OTF，绘制到 Bitmap 后转为 WPF ImageSource。
            try
            {
                float fsize = (float)fontSize;
                using var fontColl = new System.Drawing.Text.PrivateFontCollection();
                fontColl.AddFontFile(fontFilePath);
                if (fontColl.Families.Length == 0)
                {
                    App.LogDebug("GlyphRender: GDI+ PrivateFontCollection returned 0 families");
                    return false;
                }
                using var font = new System.Drawing.Font(fontColl.Families[0], fsize,
                    System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);

                // 测量文本尺寸
                using var tmpBmp = new System.Drawing.Bitmap(1, 1);
                using var tmpG = System.Drawing.Graphics.FromImage(tmpBmp);
                var textSize = tmpG.MeasureString(sampleText, font);

                int bmpW = Math.Max((int)textSize.Width + 20, 200);
                int bmpH = Math.Max((int)textSize.Height + 20, 50);

                using var bmp = new System.Drawing.Bitmap(bmpW, bmpH,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using var g = System.Drawing.Graphics.FromImage(bmp);
                g.Clear(System.Drawing.Color.Transparent);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                // 使用主题文字色（Theme_TextPrimary），在 GDI+ 中绘制
                System.Drawing.Color textColor = System.Drawing.Color.White;
                if (TryFindResource("Theme_TextPrimary") is System.Windows.Media.SolidColorBrush wpfFg)
                {
                    textColor = System.Drawing.Color.FromArgb(
                        wpfFg.Color.A, wpfFg.Color.R, wpfFg.Color.G, wpfFg.Color.B);
                }
                using var brush = new System.Drawing.SolidBrush(textColor);
                g.DrawString(sampleText, font, brush, 10, 10);

                // GDI+ Bitmap → WPF BitmapSource
                IntPtr hbitmap = bmp.GetHbitmap();
                try
                {
                    var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        hbitmap, IntPtr.Zero,
                        new Int32Rect(0, 0, bmp.Width, bmp.Height),
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    PreviewImage.Source = src;
                    PreviewImage.Stretch = System.Windows.Media.Stretch.None;
                    PreviewImage.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                    PreviewImage.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                }
                finally
                {
                    DeleteObject(hbitmap);
                }

                App.LogDebug("GlyphRender: GDI+ render OK, bmp={0}x{1}", bmpW, bmpH);
                return true;
            }
            catch (Exception ex)
            {
                App.LogDebug("GlyphRender: GDI+ render failed: {0} — {1}", ex.GetType().Name, ex.Message);
                return false;
            }
        }
        catch (Exception ex)
        {
            App.LogDebug("ShowFontPreviewGlyphRender failed: {0} — {1}", ex.GetType().Name, ex.Message);
            return false;
        }
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// 根据字体文件扩展名推断预览加载失败的原因，用于在信息面板显示。
    /// </summary>
    private static string GetFontLoadFailureReason(string fontFilePath)
        => FormatUtil.GetFontLoadFailureReason(fontFilePath);

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
            if (info.SampleRate.HasValue && info.SampleRate.Value > 0) extra.Add((L.T(L.Preview_AudioSampleRate), $"{info.SampleRate} Hz"));
            if (info.Channels.HasValue && info.Channels.Value > 0) extra.Add((L.T(L.Preview_AudioChannels), info.Channels.Value.ToString()));
            if (info.BitDepth.HasValue) extra.Add((L.T(L.Preview_AudioBitDepth), $"{info.BitDepth}-bit"));
            if (info.Bitrate.HasValue) extra.Add((L.T(L.Preview_AudioBitrate), $"{info.Bitrate} kbps"));
            SetFormatSpecificInfo(extra.ToArray()); SetToolbar(Array.Empty<ToolbarButton>(), Array.Empty<ToolbarButton>());
        }
        catch (Exception ex) { App.LogDebug("ShowAudioPreview: failed: {0}", ex.Message); ShowUnsupportedPreview(null, L.T(L.Preview_AudioParseFailed)); }
    }

    // ── MP3 ──

    private void ShowMp3Preview(string filePath, ArchiveItem item)
    {
        try
        {
            var info = Id3v2Parser.Parse(filePath);
            if (info == null) { ShowUnsupportedPreview(item, L.T(L.Preview_Mp3ParseFailed)); return; }

            HideAllPreviewControls();
            ShowPreviewPanel();

            // 信息面板：标题、歌手、专辑、时长、比特率、采样率
            var extra = new List<(string, string)>();
            if (!string.IsNullOrEmpty(info.Title)) extra.Add((L.T(L.Preview_Mp3Title), info.Title));
            if (!string.IsNullOrEmpty(info.Artist)) extra.Add((L.T(L.Preview_Mp3Artist), info.Artist));
            if (!string.IsNullOrEmpty(info.Album)) extra.Add((L.T(L.Preview_Mp3Album), info.Album));
            if (info.Duration.HasValue) extra.Add((L.T(L.Preview_AudioDuration), info.Duration.Value.ToString("hh\\:mm\\:ss")));
            if (info.Bitrate.HasValue && info.Bitrate.Value > 0) extra.Add((L.T(L.Preview_AudioBitrate), $"{info.Bitrate} kbps"));
            if (info.SampleRate.HasValue && info.SampleRate.Value > 0) extra.Add((L.T(L.Preview_AudioSampleRate), $"{info.SampleRate} Hz"));
            SetFormatSpecificInfo(extra.ToArray());
            SetToolbar(Array.Empty<ToolbarButton>(), Array.Empty<ToolbarButton>());
            SetPreviewInfo(item);
            PreviewHeader.Text = L.TF(L.Preview_Mp3Header, Path.GetFileName(filePath));

            // 内容区：优先显示封面图片，无封面时显示标题/歌手
            bool hasCoverArt = info.CoverArtData != null && info.CoverArtData.Length > 8;
            if (hasCoverArt)
            {
                try
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = new MemoryStream(info.CoverArtData!);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    PreviewImage.Source = bitmap;
                    PreviewImageScroll.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    App.LogDebug("ShowMp3Preview: failed to load cover art: {0}", ex.Message);
                    hasCoverArt = false;
                }
            }

            if (!hasCoverArt)
            {
                // 无封面时，内容区显示标题/歌手
                var sb = new System.Text.StringBuilder();
                if (!string.IsNullOrEmpty(info.Title))
                    sb.AppendLine(info.Title);
                if (!string.IsNullOrEmpty(info.Artist))
                    sb.Append(info.Artist);
                if (sb.Length > 0)
                {
                    PreviewTextBox.Text = sb.ToString().TrimEnd();
                    PreviewTextBox.FontSize = 24;
                    PreviewTextBox.TextAlignment = TextAlignment.Center;
                    PreviewTextBox.FontFamily = null; // 默认字体
                    PreviewTextBox.Visibility = Visibility.Visible;
                }
                else
                {
                    // 连标题都没有，内容区留空（仅显示信息面板）
                }
            }
        }
        catch (Exception ex) { App.LogDebug("ShowMp3Preview: failed: {0}", ex.Message); ShowUnsupportedPreview(null, L.T(L.Preview_Mp3ParseFailed)); }
    }

    // ── SQLite ──

    private async Task ShowSqlitePreview(string filePath, ArchiveItem item)
    {
        try
        {
            // 元数据 + 表格数据并行读取
            var metaTask = Task.Run(() => SQLiteParser.Parse(filePath));
            var reader = new SqliteDataReader();
            var dataTask = reader.QueryAsync(filePath,
                maxRows: AppSettings.Instance.MaxTablePreviewRows,
                maxCols: AppSettings.Instance.MaxTablePreviewCols);

            await Task.WhenAll(metaTask, dataTask);

            var meta = await metaTask;
            var data = await dataTask;

            if (meta == null) { ShowUnsupportedPreview(item, L.T(L.Preview_SqliteParseFailed)); return; }

            // 信息面板：编码、页大小、表数量
            var extra = new List<(string, string)>
            {
                (L.T(L.Preview_SqliteEncoding), meta.TextEncoding ?? "--"),
                (L.T(L.Preview_SqlitePageSize), meta.AdditionalInfo ?? "--"),
            };
            if (meta.TableCount > 0)
                extra.Add(("表数量", meta.TableCount.Value.ToString()));
            SetFormatSpecificInfo(extra.ToArray());
            SetToolbar(Array.Empty<ToolbarButton>(), Array.Empty<ToolbarButton>());

            // 表格数据展示
            var title = L.TF(L.Preview_SqliteHeader, Path.GetFileName(filePath));

            if (data?.Tables.Count > 1)
            {
                // 多表 → TabControl
                ShowMultiTablePreview(data.Tables, item, title);
            }
            else if (data?.Tables.Count == 1)
            {
                // 单表 → 直接 DataGrid（复用 ShowTablePreview）
                ShowTablePreview(data.Tables[0].Data, item, title);
            }
            else
            {
                // 无数据表 → 退化为纯元数据展示（可能为空数据库）
                HideAllPreviewControls();
                PreviewTextBox.Text = $"{meta.DisplayName}\n表数量: {meta.TableCount}";
                PreviewTextBox.TextAlignment = TextAlignment.Left;
                PreviewTextBox.FontSize = 12;
                PreviewTextBox.Visibility = Visibility.Visible;
                SetPreviewInfo(item);
                PreviewHeader.Text = title;
                ShowPreviewPanel();
            }
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
            // 详细信息放在格式信息面板中，不在内容栏重复
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
            sb.Append(FormatUtil.BuildTorrentFileTree(info));
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

    // ── Video ──

    private void ShowVideoPreview(string filePath, ArchiveItem item)
    {
        try
        {
            var info = VideoParser.Parse(filePath);
            if (info == null) { ShowUnsupportedPreview(item, L.T(L.Preview_VideoParseFailed)); return; }
            var sb = new StringBuilder();
            if (info.VideoWidth > 0 && info.VideoHeight > 0) sb.AppendLine($"  分辨率: {info.VideoWidth} × {info.VideoHeight}");
            if (info.Duration.HasValue) sb.AppendLine($"  时长: {info.Duration.Value:hh\\:mm\\:ss}");
            if (info.Codec != null) sb.AppendLine($"  编码: {info.Codec}");
            PreviewTextBox.Text = sb.ToString(); PreviewTextBox.TextAlignment = TextAlignment.Left; PreviewTextBox.FontSize = 12;
            HideAllPreviewControls();
            PreviewTextBox.Visibility = Visibility.Visible;
            SetPreviewInfo(item); PreviewHeader.Text = info.DisplayName;
            ShowPreviewPanel();
            var extra = new List<(string, string)>();
            if (info.VideoWidth > 0 && info.VideoHeight > 0) extra.Add((L.T(L.Preview_VideoDimensions), $"{info.VideoWidth} × {info.VideoHeight}"));
            if (info.Duration.HasValue) extra.Add((L.T(L.Preview_VideoDuration), info.Duration.Value.ToString("hh\\:mm\\:ss")));
            if (info.Codec != null) extra.Add((L.T(L.Preview_VideoCodec), info.Codec));
            SetFormatSpecificInfo(extra.ToArray()); SetToolbar(Array.Empty<ToolbarButton>(), Array.Empty<ToolbarButton>());
        }
        catch (Exception ex) { App.LogDebug("ShowVideoPreview: failed: {0}", ex.Message); ShowUnsupportedPreview(null, L.T(L.Preview_VideoParseFailed)); }
    }
}
