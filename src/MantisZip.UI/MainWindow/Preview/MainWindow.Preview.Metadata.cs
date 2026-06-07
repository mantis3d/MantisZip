using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
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
            PreviewTextBox.FontSize = AppSettings.Instance.FontPreviewFontSize; PreviewTextBox.TextAlignment = TextAlignment.Center;
            HideAllPreviewControls();
            PreviewTextBox.Visibility = Visibility.Visible;
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
