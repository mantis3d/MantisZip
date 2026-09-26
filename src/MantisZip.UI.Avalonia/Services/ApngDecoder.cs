using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Formats.Png;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// APNG decoder using ImageSharp.
/// Provides frame decoding with delay times for APNG animation preview.
/// </summary>
internal static class ApngDecoder
{
    /// <summary>
    /// Decode an APNG file into its constituent frames with delay times.
    /// </summary>
    /// <param name="filePath">Path to the APNG file.</param>
    /// <returns>List of frames with bitmaps and delay in ms, or null on failure.</returns>
    public static List<AnimationFrameData>? DecodeFrames(string filePath)
    {
        try
        {
            using var image = Image.Load<Rgba32>(filePath);
            return DecodeFramesFromImage(image);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ApngDecoder.DecodeFrames error: {ex.Message}");
            Debug.WriteLine(ex.StackTrace);
            return null;
        }
    }

    /// <summary>
    /// Decode APNG frames from an already loaded ImageSharp image.
    /// </summary>
    /// <param name="image">The loaded ImageSharp image (must be APNG format).</param>
    /// <returns>List of frames with bitmaps and delay in ms, or null on failure.</returns>
    public static List<AnimationFrameData>? DecodeFramesFromImage(Image<Rgba32> image)
    {
        try
        {
            var frames = image.Frames;
            var frameCount = frames.Count;
            if (frameCount <= 0) return null;

            var result = new List<AnimationFrameData>(frameCount);

            for (int i = 0; i < frameCount; i++)
            {
                var frame = frames[i];
                
                // Get frame delay from PNG metadata
                var delayMs = 100; // Default 100ms
                var framePngMeta = frame.Metadata.GetPngMetadata();
                if (framePngMeta != null)
                {
                    // FrameDelay is a Rational (numerator/denominator)
                    var delayProp = framePngMeta.GetType().GetProperty("FrameDelay");
                    if (delayProp != null)
                    {
                        var delay = delayProp.GetValue(framePngMeta);
                        if (delay != null)
                        {
                            // Rational has Numerator and Denominator properties (both uint)
                            var numProp = delay.GetType().GetProperty("Numerator");
                            var denProp = delay.GetType().GetProperty("Denominator");
                            if (numProp != null && denProp != null)
                            {
                                var numerator = (uint)numProp.GetValue(delay)!;
                                var denominator = (uint)denProp.GetValue(delay)!;
                                if (denominator > 0)
                                {
                                    // Delay is in seconds (numerator/denominator), convert to ms
                                    delayMs = Math.Max(50, (int)(numerator * 1000 / denominator));
                                }
                            }
                        }
                    }
                }

                // Convert ImageSharp frame to Avalonia Bitmap
                // Copy pixel data to array, then create Image from array
                var pixelArray = new Rgba32[frame.Width * frame.Height];
                frame.CopyPixelDataTo(pixelArray);
                using var frameImage = Image.LoadPixelData<Rgba32>(pixelArray, frame.Width, frame.Height);
                
                using var ms = new MemoryStream();
                frameImage.SaveAsPng(ms);
                ms.Position = 0;
                
                var avaloniaBitmap = new Bitmap(ms);

                result.Add(new AnimationFrameData
                {
                    Bitmap = avaloniaBitmap,
                    DelayMs = delayMs
                });
            }

            return result.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ApngDecoder.DecodeFramesFromImage error: {ex.Message}");
            Debug.WriteLine(ex.StackTrace);
            return null;
        }
    }

    /// <summary>
    /// Gets the total frame count of an APNG without decoding all frames.
    /// </summary>
    /// <param name="filePath">Path to the APNG file.</param>
    /// <returns>Frame count, or 0 on failure.</returns>
    public static int GetFrameCount(string filePath)
    {
        try
        {
            using var image = Image.Load<Rgba32>(filePath);
            return image.Frames.Count;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ApngDecoder.GetFrameCount error: {ex.Message}");
            return 0;
        }
    }
}

/// <summary>
/// Represents a single animation frame (used for both GIF and APNG).
/// </summary>
public class AnimationFrameData
{
    public Bitmap Bitmap { get; set; } = null!;
    public int DelayMs { get; set; }
}