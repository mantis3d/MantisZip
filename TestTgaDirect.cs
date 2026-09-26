using System;
using System.IO;
using SkiaSharp;
using Avalonia.Media.Imaging;

namespace TestTgaDirect
{
    class Program
    {
        static void Main(string[] args)
        {
            string filePath = @"F:\GitHub\MantisZip\TestPreview\tga_test.tga";
            
            Console.WriteLine($"Testing: {filePath}");
            
            try
            {
                // Try SkiaSharp directly
                using var fs = File.OpenRead(filePath);
                using var skStream = new SKManagedStream(fs);
                using var codec = SKCodec.Create(skStream);
                
                if (codec == null)
                {
                    Console.WriteLine("ERROR: SKCodec.Create returned null");
                    return;
                }
                
                Console.WriteLine($"[TGA] SKCodec created: {codec.Info.Width}x{codec.Info.Height}, ColorType={codec.Info.ColorType}, AlphaType={codec.Info.AlphaType}");
                Console.WriteLine($"[TGA] FrameCount: {codec.FrameCount}");
                
                var info = codec.Info;
                var imageInfo = new SKImageInfo(info.Width, info.Height);
                using var frameBitmap = new SKBitmap(imageInfo);
                var options = new SKCodecOptions(0);
                var result = codec.GetPixels(imageInfo, frameBitmap.GetPixels(), options);
                Console.WriteLine($"[TGA] Frame 0 GetPixels result: {result}");
                
                if (result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput)
                {
                    using var image = SKImage.FromBitmap(frameBitmap);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    Console.WriteLine($"[TGA] Frame 0 encoded to PNG: {data.Size} bytes");
                    
                    using var ms = new MemoryStream(data.ToArray());
                    var bitmap = new global::Avalonia.Media.Imaging.Bitmap(ms);
                    Console.WriteLine($"[TGA] Avalonia Bitmap loaded: {bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}