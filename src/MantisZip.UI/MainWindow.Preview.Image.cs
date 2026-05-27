using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using WpfAnimatedGif;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

public partial class MainWindow
{
    // ── Image ──

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
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                gifWidth = decoder.Frames[0].PixelWidth;
                gifHeight = decoder.Frames[0].PixelHeight;
                });

                _gifController = null;
                var gifBitmap = new System.Windows.Media.Imaging.BitmapImage();
                gifBitmap.BeginInit();
                gifBitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                gifBitmap.UriSource = new Uri(filePath);
                gifBitmap.EndInit();
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

                HideAllPreviewControls();
                PreviewImageScroll.Visibility = Visibility.Visible;
                ApplyZoom(ZoomMode.FitWindow);
                PreviewHeader.Text = L.TF(L.Preview_ImageHeader, Path.GetFileName(filePath));
                SetPreviewInfo(item, L.TF(L.Preview_Dimensions, gifWidth, gifHeight));
                ShowPreviewPanel();

                // GIF 格式信息
                try
                {
                    var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                        new Uri(filePath),
                        System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
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
                ApplyZoom(ZoomMode.FitWindow); // 初始化按钮选中态
                return;
            }

            // ICO — 多图标展示
            if (ext == ".ico")
            {
                var icoFrames = await Task.Run(() =>
                {
                    var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                        new Uri(filePath),
                        System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    var list = new List<(System.Windows.Media.Imaging.BitmapSource frame, int w, int h)>();
                    for (int i = 0; i < decoder.Frames.Count; i++)
                    {
                        var frame = decoder.Frames[i];
                        if (frame.CanFreeze) frame.Freeze();
                        list.Add((frame, frame.PixelWidth, frame.PixelHeight));
                    }
                    list.Sort((a, b) => (b.w * b.h).CompareTo(a.w * a.h)); // 大尺寸排前面
                    return list;
                });

                if (icoFrames.Count > 1)
                {
                    ShowIcoGallery(icoFrames, filePath, item);
                    return;
                }
                // 单帧 ICO 走到普通图片流程
            }

            // 普通图片 — 后台线程解码，不阻塞 UI
            var bitmap = await Task.Run(() =>
            {
                // 先获取实际尺寸，仅对超过 1920px 的图做降采样
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                    new Uri(filePath),
                    System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
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
            HideAllPreviewControls();
            PreviewImageScroll.Visibility = Visibility.Visible;
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
            ApplyZoom(ZoomMode.FitWindow); // 放在 SetToolbar 之后，以便更新按钮选中态
        }
        catch (Exception imgEx)
        {
            App.LogDebug("ShowImagePreviewAsync: failed: {0}", imgEx.Message);
            ShowUnsupportedPreview(null, L.T(L.Preview_ImageFailed));
        }
    }

    // ── ICO Gallery ──

    /// <summary>
    /// 显示 ICO 多图标画廊：将所有尺寸的图标排列在水平 WrapPanel 中。
    /// </summary>
    private void ShowIcoGallery(
        List<(BitmapSource frame, int w, int h)> frames,
        string filePath, ArchiveItem item)
    {
        HideAllPreviewControls();

        var wrapPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16)
        };

        foreach (var (frame, w, h) in frames)
        {
            // 每个图标一个带边框的卡片
            var border = new Border
            {
                BorderBrush = SystemColors.ControlLightBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(8),
                Padding = new Thickness(10),
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var img = new Image
            {
                Source = frame,
                Stretch = Stretch.None,
                MaxWidth = 256,
                MaxHeight = 256,
                Margin = new Thickness(4)
            };

            var label = new TextBlock
            {
                Text = $"{w} × {h}",
                FontSize = 11,
                Foreground = SystemColors.GrayTextBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };

            stack.Children.Add(img);
            stack.Children.Add(label);
            border.Child = stack;
            wrapPanel.Children.Add(border);
        }

        var scroll = new ScrollViewer
        {
            Content = wrapPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        PreviewTabularContainer.Children.Clear();
        PreviewTabularContainer.Children.Add(scroll);
        PreviewTabularContainer.Visibility = Visibility.Visible;

        var first = frames[0];
        SetPreviewInfo(item, L.TF(L.Preview_Dimensions, first.w, first.h));
        PreviewHeader.Text = L.TF(L.Preview_ImageHeader, Path.GetFileName(filePath));
        SetFormatSpecificInfo(
            (L.T(L.Preview_ImagePixels), $"{first.w * first.h:N0}")
        );
        SetToolbar(Array.Empty<ToolbarButton>(), Array.Empty<ToolbarButton>());
        ShowPreviewPanel();
    }

    // ── Transparency Toggle ──

    private void ToggleTransparencyBg()
    {
        _transparentBgEnabled = !_transparentBgEnabled;
        if (PreviewImageScroll.Parent is Panel parent)
            parent.Background = _transparentBgEnabled ? new ImageBrush { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute, ImageSource = CreateCheckerPattern() } : Brushes.Transparent;
    }

    // ── GIF Playback Controls ──

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

    // ── GIF Frame Input ──

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
            ShowPreviewPanel(); SetFormatSpecificInfo(("SVG", Path.GetFileName(filePath)));
            // SVG 缩放工具栏（通过 WebView2 ZoomFactor 控制）
            SetToolbar(
                new[] {
                    new ToolbarButton { Text = "⊞", Tooltip = L.T(L.Preview_ZoomFit), OnClick = () => { if (PreviewWebView2.CoreWebView2 != null) PreviewWebView2.ZoomFactor = 1.0; } },
                    new ToolbarButton { Text = "🔍−", Tooltip = "缩小", OnClick = () => { if (PreviewWebView2.CoreWebView2 != null && PreviewWebView2.ZoomFactor > 0.2) PreviewWebView2.ZoomFactor -= 0.1; } },
                    new ToolbarButton { Text = "🔍+", Tooltip = "放大", OnClick = () => { if (PreviewWebView2.CoreWebView2 != null && PreviewWebView2.ZoomFactor < 5.0) PreviewWebView2.ZoomFactor += 0.1; } },
                },
                Array.Empty<ToolbarButton>()
            );
        }
        catch (Exception ex) { App.LogDebug("ShowSvgPreview: failed: {0}", ex.Message); ShowUnsupportedPreview(null, "SVG 预览失败"); }
    }
}
