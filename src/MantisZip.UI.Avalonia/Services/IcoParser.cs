using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Parses ICO (Icon) files and extracts individual frame bitmaps.
/// Supports both PNG-compressed frames (common in modern ICOs) and
/// BMP/DIB frames (legacy ICOs). Falls back to SkiaSharp for BMP decoding.
/// </summary>
internal static class IcoParser
{
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// Extract all frames from an ICO file, ordered by descending size (largest first).
    /// Returns an empty list if the file is invalid or no frames can be decoded.
    /// </summary>
    public static List<IcoFrame> ExtractFrames(string filePath)
    {
        var result = new List<IcoFrame>();
        byte[] fileBytes;

        try
        {
            fileBytes = File.ReadAllBytes(filePath);
        }
        catch
        {
            return result;
        }

        if (fileBytes.Length < 6)
            return result;

        // ── Parse header ────────────────────────────────────────────────
        // ushort reserved (must be 0)
        // ushort type (must be 1 for ICO)
        // ushort count
        int count = fileBytes[4] | (fileBytes[5] << 8);
        if (count <= 0 || count > 256)
            return result;

        int headerSize = 6 + count * 16;
        if (fileBytes.Length < headerSize)
            return result;

        // ── Parse directory entries ──────────────────────────────────────
        var entries = new List<(byte width, byte height, int dataSize, int dataOffset)>(count);
        for (int i = 0; i < count; i++)
        {
            int off = 6 + i * 16;
            byte w = fileBytes[off];
            byte h = fileBytes[off + 1];
            int dataSize = fileBytes[off + 8] | (fileBytes[off + 9] << 8)
                         | (fileBytes[off + 10] << 16) | (fileBytes[off + 11] << 24);
            int dataOffset = fileBytes[off + 12] | (fileBytes[off + 13] << 8)
                           | (fileBytes[off + 14] << 16) | (fileBytes[off + 15] << 24);

            if (dataOffset < 0 || dataOffset + dataSize > fileBytes.Length || dataSize <= 0)
                continue;

            entries.Add((w, h, dataSize, dataOffset));
        }

        // Sort by size descending (0 = 256)
        entries.Sort((a, b) =>
        {
            int sa = (a.width == 0 ? 256 : a.width) * (a.height == 0 ? 256 : a.height);
            int sb = (b.width == 0 ? 256 : b.width) * (b.height == 0 ? 256 : b.height);
            return sb.CompareTo(sa);
        });

        // ── Decode each frame ────────────────────────────────────────────
        foreach (var (w, h, dataSize, dataOffset) in entries)
        {
            byte[] frameData = new byte[dataSize];
            Buffer.BlockCopy(fileBytes, dataOffset, frameData, 0, dataSize);

            Bitmap? bitmap = null;

            // Try PNG first
            if (IsPngData(frameData))
            {
                try
                {
                    using var ms = new MemoryStream(frameData);
                    bitmap = new Bitmap(ms);
                }
                catch
                {
                    // PNG decode failed, try BMP fallback
                }
            }

            // Fallback: BMP/DIB via SkiaSharp
            bitmap ??= DecodeBmpFrame(frameData);

            if (bitmap != null)
            {
                int realW = bitmap.PixelSize.Width;
                int realH = bitmap.PixelSize.Height;
                result.Add(new IcoFrame(bitmap, realW, realH));
            }
        }

        return result;
    }

    private static bool IsPngData(byte[] data)
    {
        if (data.Length < PngMagic.Length)
            return false;
        for (int i = 0; i < PngMagic.Length; i++)
            if (data[i] != PngMagic[i])
                return false;
        return true;
    }

    /// <summary>
    /// Decode a BMP/DIB frame using SkiaSharp.
    /// ICO BMP data layout: BITMAPINFOHEADER + (palette) + XOR pixels + AND mask.
    /// The AND mask must be stripped before decoding; it causes visual corruption
    /// if passed to the BMP decoder as pixel data.
    /// </summary>
    private static Bitmap? DecodeBmpFrame(byte[] dibData)
    {
        if (dibData.Length < 40)
            return null;

        // ── Parse BITMAPINFOHEADER ──────────────────────────────────────
        int headerSize = dibData[0] | (dibData[1] << 8) | (dibData[2] << 16) | (dibData[3] << 24);
        if (headerSize < 40 || headerSize > dibData.Length)
            headerSize = 40;

        int width = dibData[4] | (dibData[5] << 8) | (dibData[6] << 16) | (dibData[7] << 24);
        int biHeight = dibData[8] | (dibData[9] << 8) | (dibData[10] << 16) | (dibData[11] << 24);
        if (biHeight < 0) biHeight = -biHeight; // top-down
        short bpp = (short)(dibData[14] | (dibData[15] << 8));

        if (width <= 0 || biHeight <= 0 || bpp <= 0)
            return null;

        // ── ICO 规范：biHeight 是实际图标高度的两倍（XOR + AND 掩码）──
        int pixelHeight = biHeight / 2;
        if (pixelHeight <= 0) pixelHeight = biHeight;

        // ── Calculate palette size ──────────────────────────────────────
        int paletteSize = 0;
        if (bpp <= 8)
        {
            int clrUsed = dibData[32] | (dibData[33] << 8) | (dibData[34] << 16) | (dibData[35] << 24);
            paletteSize = clrUsed > 0 ? clrUsed * 4 : (1 << bpp) * 4;
        }

        // ── Calculate XOR pixel data size (without AND mask) ────────────
        int xorRowSize = ((width * bpp + 31) / 32) * 4;
        int xorDataSize = xorRowSize * pixelHeight;

        int dibWithoutAndMask = headerSize + paletteSize + xorDataSize;
        if (dibWithoutAndMask > dibData.Length)
            dibWithoutAndMask = dibData.Length; // no AND mask or truncated

        // ── Wrap in BMP file header and decode ──────────────────────────
        try
        {
            int fileSize = 14 + dibWithoutAndMask;
            using var ms = new MemoryStream(fileSize);
            var writer = new BinaryWriter(ms);
            writer.Write((byte)0x42); writer.Write((byte)0x4D); // "BM"
            writer.Write(fileSize);
            writer.Write((short)0);
            writer.Write((short)0);
            writer.Write(14 + headerSize); // pixel data offset
            writer.Write(dibData, 0, dibWithoutAndMask);
            writer.Flush();
            ms.Position = 0;

            using var skStream = new SkiaSharp.SKMemoryStream(ms.ToArray());
            using var skBitmap = SkiaSharp.SKBitmap.Decode(skStream);
            if (skBitmap != null)
                return SkiaSharpToAvalonia(skBitmap);
        }
        catch
        {
            // Give up
        }

        return null;
    }

    private static Bitmap SkiaSharpToAvalonia(SkiaSharp.SKBitmap skBitmap)
    {
        var info = new SkiaSharp.SKImageInfo(
            skBitmap.Width, skBitmap.Height,
            SkiaSharp.SKColorType.Bgra8888,
            SkiaSharp.SKAlphaType.Premul);

        using var image = SkiaSharp.SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        return new Bitmap(ms);
    }
}

/// <summary>
/// Represents a single frame extracted from an ICO file.
/// </summary>
public sealed record IcoFrame(Bitmap Bitmap, int Width, int Height)
{
    /// <summary>Display label like "32 × 32".</summary>
    public string Label => $"{Width} × {Height}";
}
