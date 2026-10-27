using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 容器 / 子类型双层识别（issue #54 的 jar/war/ear/apk、issue #36 的自解压 exe）。
/// </summary>
/// <remarks>
/// 为什么测试放在 UI 测试程序集：Core 测试项目是 xUnit v2，本机 VSTest 起不来
/// （见 docs/KNOWN_ISSUES.md KI-002），而这里两个项目都引用 Core，放到能本地跑的这侧才有回归价值。
/// </remarks>
public sealed class FormatDetectionContainerTests : IDisposable
{
    private readonly string _dir;

    public FormatDetectionContainerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "MantisZip", "Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 测试清理失败不影响结论（临时目录由系统回收）
        }
    }

    // ──────────────────────────────────────────────
    // issue #54：jar / war / ear / apk 必须能识别成 ZIP 容器
    // ──────────────────────────────────────────────

    [Fact]
    public void Detect_JarWithManifest_ReturnsZip_NotOdt()
    {
        // jar 规范首条目就是 META-INF/MANIFEST.MF —— 旧实现看到任何 META-INF/ 条目
        // 就无条件 return Odt，导致 ODT 没有引擎 ⇒ 「不支持的文件格式」
        var head = ReadHead(BuildZip(
            ("META-INF/MANIFEST.MF", Bytes("Manifest-Version: 1.0\r\n\r\n")),
            ("App.class", Bytes(new string('\u00CA', 64))),
            ("config.properties", Bytes("key=value\n"))));

        var subtype = FileFormatDetector.Detect(head, head.Length);

        Assert.NotEqual(FileFormat.Odt, subtype);
        Assert.Equal(FileFormat.Zip, subtype);
    }

    [Theory]
    [InlineData("app.jar")]
    [InlineData("app.war")]
    [InlineData("app.ear")]
    [InlineData("app.apk")]
    public void DetectFormat_JavaEcosystemArchives_ReportZipContainer(string fileName)
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllBytes(path, BuildZip(
            ("META-INF/MANIFEST.MF", Bytes("Manifest-Version: 1.0\r\n\r\n")),
            ("config.properties", Bytes("key=value\n"))));

        var head = ReadHead(File.ReadAllBytes(path));
        var detection = FileFormatDetector.DetectFormat(head, head.Length);

        Assert.Equal(ContainerFormat.Zip, detection.Container);
        Assert.Equal(FileFormat.Zip, detection.Subtype);
        Assert.True(detection.IsContainer);
    }

    [Theory]
    [InlineData("app.jar")]
    [InlineData("app.war")]
    [InlineData("app.ear")]
    [InlineData("app.apk")]
    public void GetEngineByExtension_JavaEcosystemArchives_ReturnZipEngine(string fileName)
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllBytes(path, BuildZip(
            ("META-INF/MANIFEST.MF", Bytes("Manifest-Version: 1.0\r\n\r\n")),
            ("config.properties", Bytes("key=value\n"))));

        var engine = ArchiveEngineFactory.GetEngineByExtension(path);

        Assert.NotNull(engine);
        Assert.Equal("ZipEngine", engine!.GetType().Name);
    }

    [Fact]
    public void Detect_RealOpenDocumentText_StillReportsOdt()
    {
        // 收紧 META-INF 判据不能误伤真正的 ODF：mimetype + manifest.xml 仍要认出来
        var head = ReadHead(BuildZip(
            ("mimetype", Bytes("application/vnd.oasis.opendocument.text")),
            ("META-INF/manifest.xml", Bytes(
                "<manifest><file-entry media-type=\"application/vnd.oasis.opendocument.text\"/></manifest>")),
            ("content.xml", Bytes("<office/>"))));

        var detection = FileFormatDetector.DetectFormat(head, head.Length);

        Assert.Equal(ContainerFormat.Zip, detection.Container);
        Assert.Equal(FileFormat.Odt, detection.Subtype);
    }

    [Fact]
    public void Detect_PlainZipWithoutMarkers_ReturnsZip()
    {
        var head = ReadHead(BuildZip(("a.txt", Bytes("hello"))));

        Assert.Equal(FileFormat.Zip, FileFormatDetector.Detect(head, head.Length));
    }

    // ──────────────────────────────────────────────
    // DetectContainer：只认容器，不掺子类型
    // ──────────────────────────────────────────────

    [Fact]
    public void DetectContainer_ZipSignature_ReturnsZip()
    {
        var head = ReadHead(BuildZip(("a.txt", Bytes("hello"))));

        Assert.Equal(ContainerFormat.Zip, FileFormatDetector.DetectContainer(head, head.Length));
    }

    [Fact]
    public void DetectContainer_SevenZipRarGzipSignatures_AreRecognized()
    {
        Assert.Equal(ContainerFormat.SevenZip,
            FileFormatDetector.DetectContainer([0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00], 7));

        Assert.Equal(ContainerFormat.Rar,
            FileFormatDetector.DetectContainer([0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00], 7));

        Assert.Equal(ContainerFormat.GZip,
            FileFormatDetector.DetectContainer([0x1F, 0x8B, 0x08, 0x00], 4));
    }

    [Fact]
    public void DetectContainer_MzStubAndUnknownBytes_ReturnUnknown()
    {
        Assert.Equal(ContainerFormat.Unknown,
            FileFormatDetector.DetectContainer([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00], 6));

        Assert.Equal(ContainerFormat.Unknown,
            FileFormatDetector.DetectContainer([0x00, 0x01, 0x02, 0x03], 4));
    }

    // ──────────────────────────────────────────────
    // issue #36：自解压 exe（MZ 桩 + 内嵌包）要能打开
    // ──────────────────────────────────────────────

    [Fact]
    public void SfxProbe_MzStubWithTrailingZip_DetectsZipContainer()
    {
        var path = Path.Combine(_dir, "zip-sfx.exe");
        File.WriteAllBytes(path, BuildSfx(BuildZip(("a.txt", Bytes("hello")))));

        var head = ReadHead(File.ReadAllBytes(path));

        Assert.True(SfxProbe.TryDetectEmbeddedContainer(path, head, head.Length, out var container));
        Assert.Equal(ContainerFormat.Zip, container);
    }

    [Fact]
    public void SfxProbe_MzStubWithTrailingSevenZip_DetectsSevenZipContainer()
    {
        var path = Path.Combine(_dir, "7z-sfx.exe");
        // 桩 + 7z 签名（具体内容无关：只要签名在尾部窗口内）
        var payload = new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04, 0x01, 0x02, 0x03, 0x04 };
        File.WriteAllBytes(path, BuildSfx(payload));

        var head = ReadHead(File.ReadAllBytes(path));

        Assert.True(SfxProbe.TryDetectEmbeddedContainer(path, head, head.Length, out var container));
        Assert.Equal(ContainerFormat.SevenZip, container);
    }

    [Fact]
    public void SfxProbe_MzStubWithoutPayload_ReturnsFalse()
    {
        var path = Path.Combine(_dir, "plain.exe");
        File.WriteAllBytes(path, BuildSfx(null));

        var head = ReadHead(File.ReadAllBytes(path));

        Assert.False(SfxProbe.TryDetectEmbeddedContainer(path, head, head.Length, out _));
    }

    [Fact]
    public void SfxProbe_NonMzFile_ReturnsFalse()
    {
        // 不是 MZ 桩就不进这条路（普通文件不该为探测付 IO）
        var path = Path.Combine(_dir, "data.bin");
        File.WriteAllBytes(path, BuildZip(("a.txt", Bytes("hello"))));

        var head = ReadHead(File.ReadAllBytes(path));

        Assert.False(SfxProbe.TryDetectEmbeddedContainer(path, head, head.Length, out _));
    }

    [Fact]
    public void SfxProbe_TwoByteGzipSignature_IsNotTreatedAsMatch()
    {
        // 1F 8B 只有 2 字节，在任意 PE 二进制里几乎必然出现，不能作为证据
        var buffer = new byte[] { 0x4D, 0x5A, 0x00, 0x1F, 0x8B, 0x00, 0x00, 0x00 };

        Assert.False(SfxProbe.TryDetectInBuffer(buffer, buffer.Length, out _));
    }

    [Fact]
    public void GetEngineByExtension_SfxExe_RoutesToSevenZipEngine()
    {
        // 关键：必须交给 SevenZipEngine（7z.dll 会自己按签名搜索内嵌包）。
        // 绝不能交给 ZipEngine —— SharpCompress 对 SFX zip 会静默返回 0 个条目。
        var path = Path.Combine(_dir, "self-extract.exe");
        File.WriteAllBytes(path, BuildSfx(BuildZip(("a.txt", Bytes("hello")))));

        var engine = ArchiveEngineFactory.GetEngineByExtension(path);

        Assert.NotNull(engine);
        Assert.Equal("SevenZipEngine", engine!.GetType().Name);
    }

    [Fact]
    public void GetEngineByExtension_UnknownExtensionWithZipMagic_ReturnsZipEngine()
    {
        var path = Path.Combine(_dir, "renamed.dat");
        File.WriteAllBytes(path, BuildZip(("a.txt", Bytes("hello"))));

        var engine = ArchiveEngineFactory.GetEngineByExtension(path);

        Assert.NotNull(engine);
        Assert.Equal("ZipEngine", engine!.GetType().Name);
    }

    [Fact]
    public void GetEngineByExtension_UnknownExtensionWithoutMagic_ReturnsNull()
    {
        var path = Path.Combine(_dir, "notes.weird");
        File.WriteAllBytes(path, Bytes("this is not an archive at all"));

        Assert.Null(ArchiveEngineFactory.GetEngineByExtension(path));
    }

    // ──────────────────────────────────────────────
    // 单一事实源：新扩展名进 SupportedExtensions，但暂不进文件关联
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(".jar")]
    [InlineData(".war")]
    [InlineData(".ear")]
    [InlineData(".apk")]
    public void SupportedExtensions_ContainsJavaEcosystemArchives(string ext)
    {
        Assert.Contains(ext, ArchiveEngineFactory.SupportedExtensions);
    }

    [Fact]
    public void OpenFileDialogFilter_PatternsMatchSupportedExtensions()
    {
        // 对话框过滤器由 SupportedExtensions 派生（MainWindow.axaml.cs OpenFileDialogAsync），
        // 这里守住「派生关系」这条约定：每个扩展名都能生成一个 *.ext 模式。
        var patterns = ArchiveEngineFactory.SupportedExtensions.Select(e => "*" + e).ToArray();

        Assert.Equal(ArchiveEngineFactory.SupportedExtensions.Length, patterns.Length);
        Assert.Contains("*.jar", patterns);
        Assert.Contains("*.tar.gz", patterns);
    }

    // ──────────────────────────────────────────────
    // helpers
    // ──────────────────────────────────────────────

    /// <summary>按条目顺序生成一个真实 zip（local header 依次排列，供头部扫描用）。</summary>
    private static byte[] BuildZip(params (string Name, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var za = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = za.CreateEntry(name, CompressionLevel.Optimal);
                using var s = entry.Open();
                s.Write(content, 0, content.Length);
            }
        }
        return ms.ToArray();
    }

    /// <summary>造一个「MZ 桩 + 可选内嵌包」的自解压文件。</summary>
    private static byte[] BuildSfx(byte[]? payload)
    {
        var stub = new byte[600];
        stub[0] = 0x4D; // M
        stub[1] = 0x5A; // Z
        stub[2] = 0x90;
        stub[3] = 0x00;
        // e_lfanew 指向 PE 头（本测试只需 MZ 头本身）
        stub[0x3C] = 0x80;
        stub[0x3D] = 0x00;

        if (payload == null)
            return stub;

        var result = new byte[stub.Length + payload.Length];
        Buffer.BlockCopy(stub, 0, result, 0, stub.Length);
        Buffer.BlockCopy(payload, 0, result, stub.Length, payload.Length);
        return result;
    }

    /// <summary>模拟 <c>GetEngineByMagic</c> 的读头方式（最多前 4KB）。</summary>
    private static byte[] ReadHead(byte[] content)
        => content.Length <= 4096 ? content : content[..4096];

    private static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);
}
