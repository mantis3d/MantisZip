using Avalonia.Media.Imaging;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 从 <c>.ico</c> 文件的原始字节里取出「最接近 16×16」的那一帧，解码成 Avalonia 位图。
///
/// <para>
/// 用途：压缩包内的 <c>.ico</c> 条目在文件列表里应当显示**它自己**的图案，
/// 而不是 Windows 按扩展名给出的通用 .ico 关联图标（见 <see cref="IconService.GetFileIcon"/>）。
/// </para>
/// <para>
/// 为什么取到帧之后要「重打包成单帧 ICO」：ICO 里的一帧有两种存储形式 ——
/// PNG（一整张 PNG 文件）和 BMP（40 字节 <c>BITMAPINFOHEADER</c> + 调色板 + XOR 位图 + AND 掩码，
/// 注意这种 BMP 是**没有文件头**的裸数据，不是完整的 .bmp 文件）。
/// 自己拼 <c>BITMAPINFOHEADER</c> 要分别处理调色板、位深、行对齐（4 字节对齐）、AND 掩码等分支，
/// 每个分支都是潜在的错位/花屏来源；而把该帧原样塞回一个「只有一帧」的 ICO 容器再交给解码器，
/// PNG 帧和 BMP 帧就共用同一条解码路径，目录项里的宽高/位深信息也原样保留。
/// </para>
/// <para>
/// 健壮性约定：任何非法/残缺/非 ICO 数据都只返回 <see langword="null"/> 并记一行 <see cref="App.DebugLog"/>，
/// **绝不抛异常** —— 图标只是列表的点缀，坏一个不能影响整个列表的显示。
/// </para>
/// </summary>
internal static class IcoIconHelper
{
    /// <summary>ICO 文件头长度：reserved(2) + type(2) + count(2)。</summary>
    private const int HeaderSize = 6;

    /// <summary>单个目录项长度：宽(1) + 高(1) + 色数(1) + 保留(1) + planes(2) + bpp(2) + 数据长度(4) + 数据偏移(4)。</summary>
    private const int DirectoryEntrySize = 16;

    /// <summary>重打包成单帧 ICO 后，帧数据的偏移：文件头 6 + 一个目录项 16 = 22。</summary>
    private const int RepackedFrameOffset = HeaderSize + DirectoryEntrySize;

    /// <summary>列表行图标的目标边长（16×16）。</summary>
    private const int ListIconSize = 16;

    /// <summary>
    /// 解码 ICO 字节中边长最接近 16 的那一帧（文件列表行图标用）。
    /// </summary>
    /// <param name="icoData">完整的 ICO 文件字节（从文件头开始）。</param>
    /// <returns>解码后的位图；数据非法或解码失败时返回 <see langword="null"/>（不抛异常）。</returns>
    public static Bitmap? ExtractIcon(byte[] icoData) => ExtractIcon(icoData, ListIconSize);

    /// <summary>
    /// 解码 ICO 字节中边长最接近 <paramref name="preferredSize"/> 的那一帧。
    /// </summary>
    /// <param name="icoData">完整的 ICO 文件字节（从文件头开始）。</param>
    /// <param name="preferredSize">期望的显示边长（像素）：16 = 列表行图标，160 = 128px 缩略图卡
    /// （见 <c>ThumbnailService</c> 里的说明：比卡片尺寸略大，才能在「只有 256 与 16 两帧」时选 256）。</param>
    /// <returns>解码后的位图；数据非法或解码失败时返回 <see langword="null"/>（不抛异常）。</returns>
    /// <remarks>
    /// 挑帧用**边长差**而不是面积差：面积差会严重偏向小帧 —— 帧集合 {16, 256} 对 128 的面积差是
    /// 16128 与 49152，于是「最接近 128」反而选中 16 帧（放大到 128 糊成一团）；按边长差则 256 胜出。
    /// 宁可拿大帧缩小（清晰），也不要拿小帧放大（糊）。
    /// </remarks>
    public static Bitmap? ExtractIcon(byte[] icoData, int preferredSize)
    {
        var preferredSide = Math.Max(1, preferredSize);

        try
        {
            if (icoData is null || icoData.Length < RepackedFrameOffset)
            {
                Log($"IcoIconHelper: 数据长度 {icoData?.Length} 字节，不足一个 ICO 头 + 目录项，忽略");
                return null;
            }

            // ICONDIR：+0 reserved（0 = 图标，2 = 光标 CUR，本方法只处理图标）、+2 type、+4 count
            ushort reserved = ReadUInt16(icoData, 0);
            ushort type = ReadUInt16(icoData, 2);
            if (reserved != 0 || type != 1)
            {
                Log($"IcoIconHelper: 不是 ICO 图标容器（reserved={reserved}, type={type}），忽略");
                return null;
            }

            int count = ReadUInt16(icoData, 4);
            if (count == 0 || icoData.Length < HeaderSize + count * DirectoryEntrySize)
            {
                Log($"IcoIconHelper: 目录不完整（count={count}, 总长={icoData.Length}），忽略");
                return null;
            }

            int bestIndex = -1;
            long bestScore = long.MaxValue;
            for (int i = 0; i < count; i++)
            {
                int entryOffset = HeaderSize + i * DirectoryEntrySize;
                // 目录项里的宽高是 1 字节，0 表示 256（ICO 格式的上限）
                long width = icoData[entryOffset] == 0 ? 256 : icoData[entryOffset];
                long height = icoData[entryOffset + 1] == 0 ? 256 : icoData[entryOffset + 1];

                // 按边长差挑帧（不用面积差，理由见方法 remarks）
                long score = Math.Abs(Math.Max(width, height) - preferredSide);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                Log("IcoIconHelper: 未找到可用帧，忽略");
                return null;
            }

            int chosenOffset = HeaderSize + bestIndex * DirectoryEntrySize;
            long frameLength = ReadUInt32(icoData, chosenOffset + 8);
            long frameStart = ReadUInt32(icoData, chosenOffset + 12);

            // 帧数据只能落在「文件头 + 全部目录项」之后（6 + 16×count），落在目录里就是损坏的偏移
            long directoryEnd = HeaderSize + count * DirectoryEntrySize;

            // 越界/空帧一律视为非法数据：不猜、不截断（被截断的图标解不出图案，还会掩盖真正的损坏）
            if (frameLength <= 0
                || frameLength > int.MaxValue - RepackedFrameOffset
                || frameStart < directoryEnd
                || frameStart + frameLength > icoData.Length)
            {
                Log($"IcoIconHelper: 第 {bestIndex} 帧越界（offset={frameStart}, length={frameLength}, 总长={icoData.Length}），忽略");
                return null;
            }

            int frameStartInt = (int)frameStart;
            int frameLengthInt = (int)frameLength;

            // 重打包成「只有一帧」的 ICO：头(6) + 目录项(16) + 该帧原始字节
            var singleFrameIco = new byte[RepackedFrameOffset + frameLengthInt];

            // ICO 头：reserved=0（+0/+1 保持 0）、type=1（+2）、count=1（+4）
            singleFrameIco[2] = 1;
            singleFrameIco[4] = 1;

            // 目录项原样复制（宽/高/色数/planes/bpp 都沿用文件作者的写法），只改数据长度与偏移
            Buffer.BlockCopy(icoData, chosenOffset, singleFrameIco, HeaderSize, DirectoryEntrySize);
            WriteUInt32(singleFrameIco, HeaderSize + 8, (uint)frameLengthInt);
            WriteUInt32(singleFrameIco, HeaderSize + 12, RepackedFrameOffset);
            Buffer.BlockCopy(icoData, frameStartInt, singleFrameIco, RepackedFrameOffset, frameLengthInt);

            return DecodeSingleFrame(singleFrameIco, icoData, preferredSide);
        }
        catch (Exception ex)
        {
            // 兜底：任何解析异常都不许冒泡到列表构建流程
            Log($"IcoIconHelper: 解析异常（{ex.GetType().Name}: {ex.Message}），忽略");
            return null;
        }
    }

    /// <summary>
    /// 解码重打包后的单帧 ICO；失败时退回仓库既有的逐帧解析（<see cref="IcoParser"/>）兜底。
    /// </summary>
    /// <param name="singleFrameIco">重打包好的单帧 ICO 字节。</param>
    /// <param name="icoData">原始 ICO 字节（兜底时重新逐帧解析用）。</param>
    /// <param name="preferredSide">期望边长（兜底逐帧挑选时用）。</param>
    private static Bitmap? DecodeSingleFrame(byte[] singleFrameIco, byte[] icoData, long preferredSide)
    {
        try
        {
            using var stream = new MemoryStream(singleFrameIco, writable: false);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            Log($"IcoIconHelper: 单帧 ICO 解码失败（{ex.GetType().Name}: {ex.Message}），改用逐帧解析兜底");
        }

        // 兜底：运行平台的解码器不认 ICO 容器时（Skia 的 ICO 支持并非所有构建都启用），
        // 改用预览画廊那条已验证的路径 —— IcoParser 会分别解码 PNG 帧与 BMP/DIB 帧
        // （含剥离 AND 掩码），再按面积挑最接近目标边长的一帧。
        return DecodeViaIcoParser(icoData, preferredSide);
    }

    /// <summary>
    /// 用既有 <see cref="IcoParser"/> 逐帧解码并挑出边长最接近 <paramref name="preferredSide"/> 的一帧。
    /// 未被选中的帧立刻释放，避免为了一个小图标白留几张 256×256 位图。
    /// </summary>
    private static Bitmap? DecodeViaIcoParser(byte[] icoData, long preferredSide)
    {
        try
        {
            var frames = IcoParser.ExtractFrames(icoData);
            if (frames.Count == 0)
                return null;

            Bitmap? best = null;
            long bestScore = long.MaxValue;

            foreach (var frame in frames)
            {
                long score = Math.Abs(Math.Max(frame.Width, frame.Height) - preferredSide);
                if (score < bestScore)
                {
                    bestScore = score;
                    // 旧候选被更合适的替换掉，释放掉它（Bitmap 是 IDisposable）
                    best?.Dispose();
                    best = frame.Bitmap;
                }
                else
                {
                    frame.Bitmap.Dispose();
                }
            }

            return best;
        }
        catch (Exception ex)
        {
            Log($"IcoIconHelper: 逐帧解析兜底失败（{ex.GetType().Name}: {ex.Message}），忽略");
            return null;
        }
    }

    /// <summary>
    /// 记一行诊断日志。日志本身不能再变成新的异常源（<see cref="App.DebugLog"/> 首次调用会惰性读设置），
    /// 所以这里再包一层：本类对外的契约是「任何情况下都不抛异常」。
    /// </summary>
    private static void Log(string message)
    {
        try
        {
            App.DebugLog(message);
        }
        catch
        {
            // 连日志都失败就彻底放弃：列表能不能显示图标比诊断信息重要
            System.Diagnostics.Debug.WriteLine(message);
        }
    }

    /// <summary>按小端序读 2 字节无符号整数（ICO 全为 little-endian）。</summary>
    private static ushort ReadUInt16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    /// <summary>按小端序读 4 字节无符号整数（返回值用 long 承载，避免后续加偏移时溢出）。</summary>
    private static long ReadUInt32(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    /// <summary>按小端序写 4 字节无符号整数。</summary>
    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }
}
