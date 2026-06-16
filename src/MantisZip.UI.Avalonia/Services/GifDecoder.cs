using Avalonia.Media.Imaging;
using SkiaSharp;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Cross-platform GIF decoder using SkiaSharp SKCodec.
/// Replaces System.Drawing-based GIF decoding (Windows-only).
/// </summary>
internal static class GifDecoder
{
    /// <summary>
    /// Decode a GIF file into its constituent frames with delay times.
    /// </summary>
    /// <param name="filePath">Path to the GIF file.</param>
    /// <returns>List of frames with bitmaps and delay in ms, or null on failure.</returns>
    public static List<GifFrameData>? DecodeFrames(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return DecodeFramesFromStream(stream);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Decode GIF frames from a stream.
    /// </summary>
    private static List<GifFrameData>? DecodeFramesFromStream(Stream stream)
    {
        using var codec = SKCodec.Create(stream);
        if (codec == null) return null;

        var info = codec.Info;
        var frameCount = codec.FrameCount;
        if (frameCount <= 0) return null;

        var frames = new List<GifFrameData>(frameCount);

        for (int i = 0; i < frameCount; i++)
        {
            var frameInfo = codec.FrameInfo?[i];
            var delayMs = 100; // Default

            if (frameInfo.HasValue)
            {
                // FrameInfo.Duration is in milliseconds for SKCodec
                delayMs = Math.Max(50, (int)frameInfo.Value.Duration);
            }

            // Decode this frame using GetPixels with frame index
            var imageInfo = new SKImageInfo(info.Width, info.Height);
            using var frameBitmap = new SKBitmap(imageInfo);
            var options = new SKCodecOptions(i);
            var result = codec.GetPixels(imageInfo, frameBitmap.GetPixels(), options);
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                continue;

            // Convert SKBitmap to Avalonia Bitmap via PNG encode
            using var image = SKImage.FromBitmap(frameBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream(data.ToArray());
            ms.Position = 0;
            var avaloniaBitmap = new Bitmap(ms);

            frames.Add(new GifFrameData
            {
                Bitmap = avaloniaBitmap,
                DelayMs = delayMs
            });
        }

        return frames.Count > 0 ? frames : null;
    }

    /// <summary>
    /// Gets the total frame count of a GIF without decoding all frames.
    /// </summary>
    public static int GetFrameCount(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var codec = SKCodec.Create(stream);
            return codec?.FrameCount ?? 0;
        }
        catch
        {
            return 0;
        }
    }
}

/// <summary>
/// Represents a single GIF frame.
/// </summary>
public class GifFrameData
{
    public Bitmap Bitmap { get; set; } = null!;
    public int DelayMs { get; set; }
}
