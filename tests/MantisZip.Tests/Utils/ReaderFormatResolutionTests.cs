using MantisZip.Core.Abstractions;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// 有效格式解析 <see cref="ArchiveEngineFactory.ResolveReaderFormat"/>（2026-11-05，KI-017）。
/// </summary>
/// <remarks>
/// 事故：UI 曾用 <see cref="ArchiveEngineFactory.GetFormatByExtension"/> 直判格式，<c>1.7z.001</c>
/// 只看到 <c>.001</c> 就落到默认分支 <c>Zip</c>，于是分卷 7z 的预览 / 双击打开 / 缩略图全部交给
/// SharpCompress，报 <c>Failed to locate the Zip Header</c>。这里把「分卷与 SFX 一律解析成 SevenZip」
/// 钉住；用例只用占位文件与魔数字节，**不需要 7z.dll**，CI 也会跑。
/// </remarks>
public sealed class ReaderFormatResolutionTests : IDisposable
{
    private readonly string _work = Path.Combine(
        Path.GetTempPath(), "MantisZipReaderFormatTests", Guid.NewGuid().ToString("N"));

    public ReaderFormatResolutionTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_work))
                Directory.Delete(_work, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响断言
        }
    }

    /// <summary>在临时目录造一个占位文件（内容无关，只需存在，供分卷兄弟文件探测）。</summary>
    private string Touch(string name, byte[]? content = null)
    {
        var path = Path.Combine(_work, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? new byte[4]);
        return path;
    }

    [Theory]
    // 数字后缀分卷：基名（去掉 .001）才是真实载荷
    [InlineData("pack.7z.001", ArchiveFormat.SevenZip)]
    [InlineData("pack.7z.002", ArchiveFormat.SevenZip)]
    [InlineData("pack.zip.001", ArchiveFormat.SevenZip)] // 载荷是 Zip，但只有 7z.dll 能串卷
    [InlineData("pack.rar.001", ArchiveFormat.SevenZip)]
    // RAR 新式分卷：.part1.rar / .part2.rar → 基名 pack.rar
    [InlineData("pack.part1.rar", ArchiveFormat.SevenZip)]
    [InlineData("pack.part2.rar", ArchiveFormat.SevenZip)]
    // RAR 老式分卷：首卷 pack.rar + pack.r00 起
    [InlineData("pack.r00", ArchiveFormat.SevenZip)]
    // 分卷但载荷不是 7z.dll 的强项 → 按**基名**判，不能落到 `.001` 的占位默认值 Zip
    [InlineData("pack.tar.001", ArchiveFormat.Tar)]
    public void ResolveReaderFormat_VolumePaths_MapToReaderFormat(string name, ArchiveFormat expected)
        => Assert.Equal(expected, ArchiveEngineFactory.ResolveReaderFormat(Touch(name)));

    [Theory]
    [InlineData("plain.zip", ArchiveFormat.Zip)]
    [InlineData("plain.jar", ArchiveFormat.Zip)]
    [InlineData("plain.7z", ArchiveFormat.SevenZip)]
    [InlineData("plain.rar", ArchiveFormat.Rar)]
    [InlineData("plain.iso", ArchiveFormat.Iso)]
    [InlineData("plain.tar", ArchiveFormat.Tar)]
    [InlineData("plain.tar.gz", ArchiveFormat.Tar)]
    [InlineData("plain.tgz", ArchiveFormat.Tar)]
    public void ResolveReaderFormat_PlainPaths_FallBackToExtension(string name, ArchiveFormat expected)
        => Assert.Equal(expected, ArchiveEngineFactory.ResolveReaderFormat(Touch(name)));

    [Fact]
    public void ResolveReaderFormat_PkzipSpannedSet_IsSevenZip()
    {
        // issue #68：archive.z01… + 末卷 archive.zip，锚点是末卷；两卷都必须解析成 SevenZip
        var part = Touch("spanned.z01");
        var last = Touch("spanned.zip");

        Assert.Equal(ArchiveFormat.SevenZip, ArchiveEngineFactory.ResolveReaderFormat(part));
        Assert.Equal(ArchiveFormat.SevenZip, ArchiveEngineFactory.ResolveReaderFormat(last));
    }

    [Fact]
    public void ResolveReaderFormat_SfxExe_IsSevenZip()
    {
        // MZ 桩 + 内嵌 7z 签名（issue #36）：只有 7z.dll 能读，SharpCompress 会静默返回 0 条目
        var bytes = new List<byte> { 0x4D, 0x5A };
        bytes.AddRange(new byte[8]);
        bytes.AddRange([0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]); // "7z" + 主版本/次版本/CRC
        bytes.AddRange(new byte[64]);

        var path = Touch("setup.exe", [.. bytes]);

        Assert.Equal(ArchiveFormat.SevenZip, ArchiveEngineFactory.ResolveReaderFormat(path));
    }

    [Fact]
    public void ResolveReaderFormat_UnknownExtension_KeepsPayloadFallback()
    {
        // 默认分支是**载荷**（未知扩展名 → Zip），别改成 null：魔数兜底路径依赖它决定能否 seed/追加。
        Assert.Equal(ArchiveFormat.Zip, ArchiveEngineFactory.ResolveReaderFormat(Touch("mystery.dat")));
    }

    [Fact]
    public void ResolveReaderFormat_IsConsistentWithEngineRouting_ForSplitVolumes()
    {
        // 两份事实源（引擎路由 / 格式身份）对分卷必须给出同一个答案：都是 SevenZip。
        foreach (var name in new[] { "route.7z.001", "route.zip.001", "route.rar.001" })
        {
            var path = Touch(name);
            Assert.IsType<MantisZip.Core.Engines.SevenZipEngine>(
                ArchiveEngineFactory.GetEngineByExtension(path));
            Assert.Equal(ArchiveFormat.SevenZip, ArchiveEngineFactory.ResolveReaderFormat(path));
        }
    }
}
