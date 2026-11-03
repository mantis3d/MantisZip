using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using MantisZip.UI.Avalonia.Models;
using SkiaSharp;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 卡片兜底图标的三态判定（缩略图 / 文字卡 / 大图标 / 小图标）。
///
/// <para>
/// 这几条属性决定缩略图卡显示什么，容易在后续改动里被「顺手」改错：
/// ①有缩略图内容时不能显示图标；②有大图标时按 96px 显示；③没有大图标（非 Windows）才退回 16px 原尺寸。
/// </para>
/// </summary>
public class ArchiveItemModelCardIconTests
{
    [AvaloniaFact]
    public void NoThumbnail_NoLargeIcon_ShowsSmallIcon()
    {
        var model = new ArchiveItemModel { Name = "movie.mp4", IconSource = CreateBitmap(16) };

        Assert.True(model.ShowSmallIcon);
        Assert.False(model.ShowLargeIcon);
    }

    [AvaloniaFact]
    public void NoThumbnail_WithLargeIcon_ShowsLargeIcon()
    {
        var model = new ArchiveItemModel
        {
            Name = "movie.mp4",
            IconSource = CreateBitmap(16),
            LargeIconSource = CreateBitmap(256),
        };

        Assert.True(model.ShowLargeIcon);
        Assert.False(model.ShowSmallIcon);
    }

    [AvaloniaFact]
    public void WithThumbnail_HidesBothIcons()
    {
        var model = new ArchiveItemModel
        {
            Name = "photo.png",
            ThumbnailSource = CreateBitmap(128),
            LargeIconSource = CreateBitmap(256),
        };

        Assert.True(model.HasThumbnailContent);
        Assert.False(model.ShowLargeIcon);
        Assert.False(model.ShowSmallIcon);
    }

    [AvaloniaFact]
    public void WithTextSnippet_HidesBothIcons()
    {
        var model = new ArchiveItemModel
        {
            Name = "notes.txt",
            TextSnippet = "hello",
            LargeIconSource = CreateBitmap(256),
        };

        Assert.True(model.HasThumbnailContent);
        Assert.False(model.ShowLargeIcon);
        Assert.False(model.ShowSmallIcon);
    }

    /// <summary>
    /// 缩略图晚到（异步加载）时，图标必须立刻让位——依赖 HasThumbnailContent 的通知链。
    /// </summary>
    [AvaloniaFact]
    public void ThumbnailArrivesLater_SwitchesFromIconToThumbnail()
    {
        var model = new ArchiveItemModel { Name = "photo.png", LargeIconSource = CreateBitmap(256) };
        var iconNotified = 0;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ArchiveItemModel.ShowLargeIcon) or nameof(ArchiveItemModel.ShowSmallIcon))
                iconNotified++;
        };

        Assert.True(model.ShowLargeIcon);

        model.ThumbnailSource = CreateBitmap(128);

        Assert.False(model.ShowLargeIcon);
        Assert.False(model.ShowSmallIcon);
        Assert.True(iconNotified > 0);
    }


    /// <summary>用 Skia 生成一张纯色 PNG 再交给 Avalonia 解码（与 IcoIconHelperTests 同一套路）。</summary>
    private static Bitmap CreateBitmap(int size)
    {
        using var bitmap = new SKBitmap(size, size);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new Bitmap(new MemoryStream(data.ToArray()));
    }
}
