using System.Collections.ObjectModel;
using System.Data;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using Microsoft.Data.Sqlite;
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

    // FontFamily 手动实现，不使用 [ObservableProperty]（源生成器对 Avalonia.Media 命名空间有已知问题）
    private global::Avalonia.Media.FontFamily _fontFamily = global::Avalonia.Media.FontFamily.Default;

    public global::Avalonia.Media.FontFamily FontFamily
    {
        get => _fontFamily;
        set => SetProperty(ref _fontFamily, value);
    }

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
        OnPropertyChanged(nameof(HasGifControls));


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

    // ── Image ──

    [ObservableProperty]
    private global::Avalonia.Media.Imaging.Bitmap? _previewImage;

    [ObservableProperty]
    private int _imageWidth;

    [ObservableProperty]
    private int _imageHeight;

    [ObservableProperty]
    private bool _isTransparencySupported;

    // ── Torrent ──

    [ObservableProperty]
    private ObservableCollection<TorrentFileItem> _torrentFileItems = [];

    // ── SQLite ──

    [ObservableProperty]
    private System.Data.DataView? _sqliteTableData;

    [ObservableProperty]
    private ObservableCollection<string> _sqliteTableNames = [];

    [ObservableProperty]
    private int _selectedTableIndex;

    private string? _lastPreviewFilePath;
    private System.Data.DataTable? _currentSqliteTable;

    // ── GIF animation ──

    [ObservableProperty]
    private bool _isPlaying = true;

    [ObservableProperty]
    private int _currentFrame;

    [ObservableProperty]
    private int _totalFrames;

    public bool HasGifControls => PreviewType == PreviewType.Gif;

    private List<GifFrame>? _gifFrames;
    private int _gifCurrentFrameIndex;
    private DispatcherTimer? _gifTimer;

    private struct GifFrame
    {
        public global::Avalonia.Media.Imaging.Bitmap Bitmap;
        public int DelayMs;
    }

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
        if (ImageWidth > 0 && ImageHeight > 0)
        {
            var fitX = 600.0 / ImageWidth;
            var fitY = 500.0 / ImageHeight;
            ZoomLevel = Math.Min(fitX, fitY);
            if (ZoomLevel > 1.0) ZoomLevel = 1.0;
        }
        else
        {
            ZoomLevel = 1.0;
        }
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

    // ── GIF controls ──

    [RelayCommand]
    private void PlayPauseGif()
    {
        IsPlaying = !IsPlaying;
        if (IsPlaying)
            StartGifAnimation();
        else
            StopGifTimer();
    }

    [RelayCommand]
    private void PreviousGifFrame()
    {
        if (_gifFrames == null || _gifFrames.Count == 0) return;
        StopGifTimer();
        _gifCurrentFrameIndex = (_gifCurrentFrameIndex - 1 + _gifFrames.Count) % _gifFrames.Count;
        CurrentFrame = _gifCurrentFrameIndex;
        PreviewImage = _gifFrames[_gifCurrentFrameIndex].Bitmap;
    }

    [RelayCommand]
    private void NextGifFrame()
    {
        if (_gifFrames == null || _gifFrames.Count == 0) return;
        StopGifTimer();
        _gifCurrentFrameIndex = (_gifCurrentFrameIndex + 1) % _gifFrames.Count;
        CurrentFrame = _gifCurrentFrameIndex;
        PreviewImage = _gifFrames[_gifCurrentFrameIndex].Bitmap;
    }

    partial void OnCurrentFrameChanged(int value)
    {
        if (_gifFrames == null || value < 0 || value >= _gifFrames.Count) return;
        if (!_isAnimating)
            StopGifTimer();
        _gifCurrentFrameIndex = value;
        PreviewImage = _gifFrames[value].Bitmap;
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

    // ── Image ──

    /// <summary>
    /// 显示图片预览。
    /// </summary>
    public void ShowImage(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        var bitmap = new global::Avalonia.Media.Imaging.Bitmap(fs);
        PreviewImage = bitmap;
        ImageWidth = bitmap.PixelSize.Width;
        ImageHeight = bitmap.PixelSize.Height;
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        IsTransparencySupported = ext is ".png" or ".ico" or ".webp";
        PreviewType = PreviewType.Image;
        IsPreviewVisible = true;
        IsToolbarVisible = true;
        PreviewHeaderText = "图片预览";
        // 初始缩放：适应预览区域（预估 600×500）
        var fitX = 600.0 / ImageWidth;
        var fitY = 500.0 / ImageHeight;
        ZoomLevel = Math.Min(fitX, fitY);
        if (ZoomLevel > 1.0) ZoomLevel = 1.0;

        FormatMetadata =
        [
            new FormatMetadataItem("尺寸", $"{ImageWidth} × {ImageHeight}"),
            new FormatMetadataItem("文件大小", FormatFileSize(new FileInfo(filePath).Length)),
        ];
    }

    // ── GIF ──

    /// <summary>
    /// 显示 GIF 预览。
    /// </summary>
    public void ShowGif(string filePath)
    {
        StopGifTimer();
        _gifFrames = null;

        try
        {
            using var img = System.Drawing.Image.FromFile(filePath);
            int frameCount = img.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time);
            if (frameCount <= 0)
            {
                ShowUnsupported("无法解码 GIF");
                return;
            }

            TotalFrames = frameCount;
            IsPlaying = true;
            CurrentFrame = 0;
            _gifCurrentFrameIndex = 0;

            // 读取帧延迟（PropertyTagFrameDelay = 0x5100）
            var delayBytes = img.GetPropertyItem(0x5100)?.Value;
            var delays = new int[frameCount];
            if (delayBytes != null)
            {
                for (int i = 0; i < frameCount; i++)
                    delays[i] = Math.Max(50, (delayBytes[i * 4] + delayBytes[i * 4 + 1] * 256) * 10);
            }
            else
            {
                for (int i = 0; i < frameCount; i++)
                    delays[i] = 100;
            }

            // 解码所有帧
            var frames = new List<GifFrame>(frameCount);
            for (int i = 0; i < frameCount; i++)
            {
                img.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Time, i);
                using var ms = new MemoryStream();
                img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                frames.Add(new GifFrame
                {
                    Bitmap = new global::Avalonia.Media.Imaging.Bitmap(ms),
                    DelayMs = delays[i]
                });
            }

            _gifFrames = frames;

            if (frames.Count > 0)
            {
                PreviewImage = frames[0].Bitmap;
                ImageWidth = frames[0].Bitmap.PixelSize.Width;
                ImageHeight = frames[0].Bitmap.PixelSize.Height;
            }

            // 启动动画
            if (frames.Count > 1)
                StartGifAnimation();

            // 初始缩放：适应预览区域
            var fitX = 600.0 / ImageWidth;
            var fitY = 500.0 / ImageHeight;
            ZoomLevel = Math.Min(fitX, fitY);
            if (ZoomLevel > 1.0) ZoomLevel = 1.0;

            PreviewType = PreviewType.Gif;
            IsPreviewVisible = true;
            IsToolbarVisible = true;
            PreviewHeaderText = "GIF 预览";
            FormatMetadata =
            [
                new FormatMetadataItem("尺寸", $"{ImageWidth} × {ImageHeight}"),
                new FormatMetadataItem("文件大小", FormatFileSize(new FileInfo(filePath).Length)),
                new FormatMetadataItem("帧数", TotalFrames.ToString()),
            ];
        }
        catch (Exception ex)
        {
            ShowUnsupported($"GIF 加载失败: {ex.Message}");
        }
    }

    private void StartGifAnimation()
    {
        if (_gifFrames == null || _gifFrames.Count <= 1) return;

        StopGifTimer();
        _gifTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher.UIThread);
        _gifTimer.Interval = TimeSpan.FromMilliseconds(_gifFrames[_gifCurrentFrameIndex].DelayMs);
        _gifTimer.Tick += OnGifTimerTick;
        _gifTimer.Start();
    }

    internal void StopGifTimer()
    {
        if (_gifTimer != null)
        {
            _gifTimer.Stop();
            _gifTimer.Tick -= OnGifTimerTick;
            _gifTimer = null;
        }
    }

    private bool _isAnimating;

    private void OnGifTimerTick(object? sender, EventArgs e)
    {
        try
        {
            if (_gifFrames == null || _gifFrames.Count == 0) return;

            _isAnimating = true;
            _gifCurrentFrameIndex = (_gifCurrentFrameIndex + 1) % _gifFrames.Count;
            CurrentFrame = _gifCurrentFrameIndex;
            PreviewImage = _gifFrames[_gifCurrentFrameIndex].Bitmap;

            if (_gifTimer != null && _gifCurrentFrameIndex < _gifFrames.Count)
                _gifTimer.Interval = TimeSpan.FromMilliseconds(_gifFrames[_gifCurrentFrameIndex].DelayMs);
            _isAnimating = false;
        }
        catch
        {
            _isAnimating = false;
        }
    }

    // ── SVG ──

    /// <summary>
    /// 显示 SVG 预览（通过 Bitmap 栅格化渲染）。
    /// </summary>
    public void ShowSvg(string filePath)
    {
        try
        {
            var svg = new Svg.Skia.SKSvg();
            svg.Load(filePath);

            if (svg.Picture == null)
            {
                ShowUnsupported("无法解析 SVG 文件");
                return;
            }

            var rect = svg.Picture.CullRect;
            var svgW = Math.Max(1, (float)rect.Width);
            var svgH = Math.Max(1, (float)rect.Height);

            // 最小预览尺寸 512px（小图标自动放大），最大 2048px 防撑爆
            const float minSize = 512f;
            const float maxSize = 2048f;
            var scale = 1f;
            if (svgW < minSize && svgH < minSize)
                scale = Math.Min(minSize / svgW, minSize / svgH);
            if (svgW * scale > maxSize || svgH * scale > maxSize)
                scale = Math.Min(maxSize / (svgW * scale), maxSize / (svgH * scale)) * scale;

            var w = (int)(svgW * scale);
            var h = (int)(svgH * scale);

            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(w, h));
            var canvas = surface.Canvas;
            canvas.Clear(SkiaSharp.SKColors.Transparent);
            canvas.Scale((float)w / rect.Width, (float)h / rect.Height);
            canvas.DrawPicture(svg.Picture);
            canvas.Flush();

            using var img = surface.Snapshot();
            using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            var ms = new MemoryStream(data.ToArray());
            PreviewImage = new global::Avalonia.Media.Imaging.Bitmap(ms);

            PreviewType = PreviewType.Svg;
            IsPreviewVisible = true;
            IsToolbarVisible = true;
            PreviewHeaderText = "SVG 预览";
        }
        catch (Exception ex)
        {
            ShowUnsupported($"SVG 渲染失败: {ex.Message}");
        }
    }

    // ── Font ──

    /// <summary>
    /// 显示字体元数据与示例文本。
    /// </summary>
    public void ShowFont(string filePath)
    {
        var info = FontParser.Parse(filePath);
        if (info == null)
        {
            ShowUnsupported("无法解析字体文件");
            return;
        }
        PreviewType = PreviewType.Font;
        IsPreviewVisible = true;
        IsToolbarVisible = true;
        PreviewHeaderText = "字体预览";
        FormatMetadata.Clear();
        FormatMetadata.Add(new("字体名称", info.FontName ?? "未知"));
        FormatMetadata.Add(new("样式", info.FontStyle ?? "常规"));
        FormatMetadata.Add(new("字形数", info.GlyphCount?.ToString() ?? "未知"));

        // 从字体文件加载 FontFamily，使示例文本用该字体渲染
        var fontFilePath = info.FontDecompressedPath ?? filePath;
        try
        {
            FontFamily = new global::Avalonia.Media.FontFamily(fontFilePath);
        }
        catch
        {
            FontFamily = global::Avalonia.Media.FontFamily.Default;
        }
        TextContent = "The quick brown fox jumps over the lazy dog\n0123456789\nABCDEFGHIJKLMNOPQRSTUVWXYZ\nabcdefghijklmnopqrstuvwxyz\n天地玄黄 宇宙洪荒 日月盈昃 辰宿列张";
    }

    // ── Audio ──

    /// <summary>
    /// 显示音频元数据信息。
    /// </summary>
    public void ShowAudio(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        FileFormatInfo? info = ext switch
        {
            ".flac" => FlacParser.Parse(filePath),
            ".wav" => RiffParser.Parse(filePath),
            ".mp3" => Id3v2Parser.Parse(filePath),
            _ => null
        };
        if (info == null)
        {
            ShowUnsupported("无法解析音频文件");
            return;
        }
        PreviewType = PreviewType.Audio;
        IsPreviewVisible = true;
        IsToolbarVisible = true;
        PreviewHeaderText = "音频信息";
        FormatMetadata.Clear();
        if (info.Duration.HasValue)
            FormatMetadata.Add(new("时长", info.Duration.Value.ToString(@"mm\:ss")));
        if (info.SampleRate.HasValue)
            FormatMetadata.Add(new("采样率", $"{info.SampleRate} Hz"));
        if (info.Channels.HasValue)
            FormatMetadata.Add(new("声道", info.Channels.Value.ToString()));
        if (info.Bitrate.HasValue)
            FormatMetadata.Add(new("比特率", $"{info.Bitrate} kbps"));
        if (info.Artist != null)
            FormatMetadata.Add(new("艺术家", info.Artist));
        if (info.Album != null)
            FormatMetadata.Add(new("专辑", info.Album));
    }

    // ── SQLite ──

    private void LoadSqliteTable(string filePath, string tableName)
    {
        using var conn = new SqliteConnection($"Data Source={filePath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM \"{tableName.Replace("\"", "\"\"")}\" LIMIT 100";
        using var reader = cmd.ExecuteReader();

        var table = new DataTable();
        for (int i = 0; i < reader.FieldCount && i < 100; i++)
            table.Columns.Add(reader.GetName(i));

        while (reader.Read())
        {
            var row = table.NewRow();
            for (int i = 0; i < reader.FieldCount && i < 100; i++)
                row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            table.Rows.Add(row);
        }
        _currentSqliteTable = table;
        SqliteTableData = table.DefaultView;
    }

    partial void OnSelectedTableIndexChanged(int value)
    {
        if (value >= 0 && value < SqliteTableNames.Count && !string.IsNullOrEmpty(_lastPreviewFilePath))
        {
            LoadSqliteTable(_lastPreviewFilePath, SqliteTableNames[value]);
        }
    }

    /// <summary>
    /// 显示 SQLite 数据库预览。
    /// </summary>
    public void ShowSqlitePreview(string filePath)
    {
        try
        {
            _lastPreviewFilePath = filePath;

            using var conn = new SqliteConnection($"Data Source={filePath}");
            conn.Open();

            // 获取所有表名
            var tables = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    tables.Add(reader.GetString(0));
            }

            SqliteTableNames = new ObservableCollection<string>(tables);

            // 加载第一个表
            if (tables.Count > 0)
            {
                SelectedTableIndex = 0;
                LoadSqliteTable(filePath, tables[0]);
            }

            PreviewType = PreviewType.Sqlite;
            IsPreviewVisible = true;
            IsToolbarVisible = true;
            PreviewHeaderText = "SQLite 数据库";
            FormatMetadata.Clear();
            FormatMetadata.Add(new("表数量", tables.Count.ToString()));
        }
        catch (Exception ex)
        {
            ShowUnsupported($"无法读取 SQLite 数据库: {ex.Message}");
        }
    }

    // ── ISO ──

    /// <summary>
    /// 显示光盘镜像元数据。
    /// </summary>
    public void ShowIso(string filePath)
    {
        var info = IsoParser.Parse(filePath);
        if (info == null)
        {
            ShowUnsupported("无法解析光盘镜像");
            return;
        }
        PreviewType = PreviewType.Iso;
        IsPreviewVisible = true;
        IsToolbarVisible = true;
        PreviewHeaderText = "光盘镜像";
        FormatMetadata.Clear();
        FormatMetadata.Add(new("卷标", info.VolumeLabel ?? "未知"));
        FormatMetadata.Add(new("格式", info.DisplayName ?? "ISO 9660"));
        if (info.DiskSize.HasValue)
            FormatMetadata.Add(new("大小", FormatFileSize(info.DiskSize.Value)));
    }

    // ── Torrent ──

    /// <summary>
    /// 显示 BT 种子元数据与文件列表。
    /// </summary>
    public void ShowTorrent(string filePath)
    {
        var info = TorrentParser.Parse(filePath);
        if (info == null)
        {
            ShowUnsupported("无法解析种子文件");
            return;
        }
        PreviewType = PreviewType.Torrent;
        IsPreviewVisible = true;
        IsToolbarVisible = true;
        PreviewHeaderText = "BT 种子";
        FormatMetadata.Clear();
        if (info.InfoHashV1 != null)
            FormatMetadata.Add(new("InfoHash", info.InfoHashV1));
        if (info.MagnetLink != null)
            FormatMetadata.Add(new("Magnet 链接", info.MagnetLink));
        if (info.TrackerUrl != null)
            FormatMetadata.Add(new("Tracker", info.TrackerUrl));
        if (info.CreatedBy != null)
            FormatMetadata.Add(new("创建者", info.CreatedBy));
        if (info.FileCount.HasValue)
            FormatMetadata.Add(new("文件数", info.FileCount.Value.ToString()));
        if (info.TorrentTotalSize.HasValue)
            FormatMetadata.Add(new("总大小", FormatFileSize(info.TorrentTotalSize.Value)));

        // 种子内文件列表
        if (info.TorrentFileEntries != null)
        {
            TorrentFileItems = new ObservableCollection<TorrentFileItem>(
                info.TorrentFileEntries.Select(f => new TorrentFileItem(f.Path, f.Size)));
        }
    }

    // ── Office ──

    /// <summary>
    /// 显示 Office 文档元数据。
    /// </summary>
    public void ShowOffice(string filePath)
    {
        var info = OfficeParser.Parse(filePath);
        if (info == null)
        {
            ShowUnsupported("无法解析 Office 文档");
            return;
        }
        PreviewType = PreviewType.Office;
        IsPreviewVisible = true;
        IsToolbarVisible = true;
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        PreviewHeaderText = ext switch
        {
            ".docx" => "Word 文档信息",
            ".xlsx" => "Excel 工作簿信息",
            ".pptx" => "PowerPoint 演示文稿信息",
            _ => "Office 文档信息"
        };
        FormatMetadata.Clear();
        if (info.Title != null) FormatMetadata.Add(new("标题", info.Title));
        if (info.Author != null) FormatMetadata.Add(new("作者", info.Author));
        if (info.Subject != null) FormatMetadata.Add(new("主题", info.Subject));
        if (info.PageCount.HasValue) FormatMetadata.Add(new("页数", info.PageCount.Value.ToString()));
        if (info.CreationDate.HasValue) FormatMetadata.Add(new("创建日期", info.CreationDate.Value.ToString("yyyy-MM-dd HH:mm")));
    }

    // ── Video ──

    /// <summary>
    /// 显示视频元数据。
    /// </summary>
    public void ShowVideo(string filePath)
    {
        var info = VideoParser.Parse(filePath);
        if (info == null)
        {
            ShowUnsupported("无法解析视频文件");
            return;
        }
        PreviewType = PreviewType.Video;
        IsPreviewVisible = true;
        IsToolbarVisible = true;
        PreviewHeaderText = "视频信息";
        FormatMetadata.Clear();
        if (info.VideoWidth.HasValue && info.VideoHeight.HasValue)
            FormatMetadata.Add(new("分辨率", $"{info.VideoWidth} × {info.VideoHeight}"));
        if (info.Duration.HasValue)
            FormatMetadata.Add(new("时长", info.Duration.Value.ToString(@"hh\:mm\:ss")));
        if (info.Codec != null)
            FormatMetadata.Add(new("编码", info.Codec));
        if (info.Bitrate.HasValue)
            FormatMetadata.Add(new("比特率", $"{info.Bitrate} kbps"));
    }

    /// <summary>
    /// 格式化文件大小为人类可读字符串。
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var unitIndex = 0;
        var size = (double)bytes;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return unitIndex == 0 ? $"{bytes} {units[unitIndex]}" : $"{size:F2} {units[unitIndex]}";
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
        PreviewImage = null;
        ImageWidth = 0;
        ImageHeight = 0;
        IsTransparencySupported = false;
        TorrentFileItems.Clear();
        SqliteTableData = null;
        SqliteTableNames.Clear();
        SelectedTableIndex = 0;
        _lastPreviewFilePath = null;
        StopGifTimer();
        _gifFrames = null;
        FontFamily = global::Avalonia.Media.FontFamily.Default;
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

/// <summary>
/// Torrent 文件列表条目。
/// </summary>
public record TorrentFileItem(string Path, long Size)
{
    public string SizeDisplay => FormatSize(Size);

    private static string FormatSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var unitIndex = 0;
        var size = (double)bytes;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return unitIndex == 0 ? $"{bytes} {units[unitIndex]}" : $"{size:F2} {units[unitIndex]}";
    }
}
