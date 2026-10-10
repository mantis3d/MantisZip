using System.Text;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using SkiaSharp;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 音频预览：内嵌封面（MP3 APIC）解码 + 无封面时标题/歌手回退文本。
/// 样本 MP3 为手写 ID3v2.3 tag（TIT2/TPE1 帧，可选 APIC 帧携带 PNG）。
/// </summary>
public class PreviewAudioPreviewTests
{
    /// <summary>
    /// 无 APIC 帧的 MP3：必须回退显示「标题 + 歌手」两行文本，且无封面位图。
    /// </summary>
    [AvaloniaFact]
    public void ShowAudio_Mp3WithoutCover_ShowsTitleArtistFallback()
    {
        var mp3Path = CreateTestMp3(withCover: false);
        try
        {
            var vm = new PreviewViewModel();
            vm.ShowAudio(mp3Path);

            Assert.Equal(PreviewType.Audio, vm.PreviewType);
            Assert.False(vm.HasAudioCover);
            Assert.Null(vm.AudioCoverImage);
            Assert.True(vm.HasAudioFallback);
            Assert.Equal($"My Song{Environment.NewLine}The Artist", vm.AudioFallbackText);
        }
        finally
        {
            File.Delete(mp3Path);
        }
    }

    /// <summary>
    /// 带 APIC 帧（内嵌 PNG）的 MP3：必须解码出封面位图，不显示回退文本。
    /// </summary>
    [AvaloniaFact]
    public void ShowAudio_Mp3WithCover_DecodesCoverArt()
    {
        var mp3Path = CreateTestMp3(withCover: true);
        try
        {
            var vm = new PreviewViewModel();
            vm.ShowAudio(mp3Path);

            Assert.Equal(PreviewType.Audio, vm.PreviewType);
            Assert.True(vm.HasAudioCover);
            Assert.NotNull(vm.AudioCoverImage);
            Assert.False(vm.HasAudioFallback);
        }
        finally
        {
            File.Delete(mp3Path);
        }
    }

    /// <summary>
    /// 回归测试：连续预览两个音频文件时，ShowAudio 必须先清空上一个文件的封面/回退状态，
    /// 否则无封面文件会残留上一个文件的封面位图（陈旧状态）。
    /// </summary>
    [AvaloniaFact]
    public void ShowAudio_SecondFileWithoutCover_DoesNotKeepStaleCover()
    {
        var withCoverPath = CreateTestMp3(withCover: true);
        var noCoverPath = CreateTestMp3(withCover: false);
        try
        {
            var vm = new PreviewViewModel();
            vm.ShowAudio(withCoverPath);
            Assert.True(vm.HasAudioCover);

            vm.ShowAudio(noCoverPath);

            Assert.False(vm.HasAudioCover);
            Assert.Null(vm.AudioCoverImage);
            Assert.True(vm.HasAudioFallback);
        }
        finally
        {
            File.Delete(withCoverPath);
            File.Delete(noCoverPath);
        }
    }

    /// <summary>
    /// 回归测试：离开音频预览（PreviewType 变为非 Audio）时必须释放封面位图，
    /// 避免大封面常驻内存（OnPreviewTypeChanged 清理逻辑）。
    /// </summary>
    [AvaloniaFact]
    public void LeavingAudioPreview_ClearsCoverState()
    {
        var mp3Path = CreateTestMp3(withCover: true);
        try
        {
            var vm = new PreviewViewModel();
            vm.ShowAudio(mp3Path);
            Assert.True(vm.HasAudioCover);

            vm.ShowUnsupported();

            Assert.Null(vm.AudioCoverImage);
            Assert.False(vm.HasAudioCover);
            Assert.Empty(vm.AudioFallbackText);
            Assert.NotEqual(PreviewType.Audio, vm.PreviewType);
        }
        finally
        {
            File.Delete(mp3Path);
        }
    }

    // ── 样本构造：手写 ID3v2.3 tag ──

    /// <summary>写一个最小 MP3（仅 ID3v2.3 tag，无 MPEG 帧）到临时目录。</summary>
    private static string CreateTestMp3(bool withCover)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mantiszip_audio_test_{Guid.NewGuid():N}.mp3");
        File.WriteAllBytes(path, BuildMp3Bytes(withCover));
        return path;
    }

    /// <summary>
    /// 构造 ID3v2.3 tag：TIT2="My Song"、TPE1="The Artist"，可选 APIC 帧携带 PNG 封面。
    /// 帧大小为普通大端 32 位整数（v2.3 规范），tag 头部大小为 syncsafe。
    /// </summary>
    private static byte[] BuildMp3Bytes(bool withCover)
    {
        var frames = new List<byte[]>
        {
            BuildTextFrame("TIT2", "My Song"),
            BuildTextFrame("TPE1", "The Artist"),
        };
        if (withCover)
            frames.Add(BuildFrame("APIC", BuildApicPayload(CreateTinyPng())));

        using var body = new MemoryStream();
        foreach (var frame in frames)
            body.Write(frame, 0, frame.Length);

        using var ms = new MemoryStream();
        ms.Write([0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
        int size = (int)body.Length;
        // syncsafe 大小（每字节仅用低 7 位）
        ms.Position = 6;
        ms.WriteByte((byte)((size >> 21) & 0x7F));
        ms.WriteByte((byte)((size >> 14) & 0x7F));
        ms.WriteByte((byte)((size >> 7) & 0x7F));
        ms.WriteByte((byte)(size & 0x7F));
        body.Position = 0;
        body.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>ID3v2.3 文本帧：encoding(0=ISO-8859-1) + 文本。</summary>
    private static byte[] BuildTextFrame(string frameId, string text)
    {
        using var payload = new MemoryStream();
        payload.WriteByte(0x00);
        var textBytes = Encoding.Latin1.GetBytes(text);
        payload.Write(textBytes, 0, textBytes.Length);
        return BuildFrame(frameId, payload.ToArray());
    }

    /// <summary>APIC 帧负载：encoding + MIME(null-term) + 图片类型 + 描述(null-term) + 图片数据。</summary>
    private static byte[] BuildApicPayload(byte[] png)
    {
        using var payload = new MemoryStream();
        payload.WriteByte(0x00); // ISO-8859-1
        var mime = Encoding.ASCII.GetBytes("image/png");
        payload.Write(mime, 0, mime.Length);
        payload.WriteByte(0x00); // MIME null terminator
        payload.WriteByte(0x03); // picture type: front cover
        payload.WriteByte(0x00); // 空描述 + null terminator
        payload.Write(png, 0, png.Length);
        return payload.ToArray();
    }

    /// <summary>包装 ID3v2.3 帧头（4 字节 ID + 大端 32 位大小 + 2 字节标志）+ 负载。</summary>
    private static byte[] BuildFrame(string frameId, byte[] payload)
    {
        var frame = new byte[10 + payload.Length];
        Encoding.ASCII.GetBytes(frameId).CopyTo(frame, 0);
        frame[4] = (byte)(payload.Length >> 24);
        frame[5] = (byte)(payload.Length >> 16);
        frame[6] = (byte)(payload.Length >> 8);
        frame[7] = (byte)payload.Length;
        payload.CopyTo(frame, 10);
        return frame;
    }

    /// <summary>用 SkiaSharp 生成 2×2 纯色 PNG 作为内嵌封面样本。</summary>
    private static byte[] CreateTinyPng()
    {
        using var bitmap = new SKBitmap(2, 2);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
