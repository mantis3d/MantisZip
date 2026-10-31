using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using SharpSevenZip;
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
    // issue #36 回归：真实大 PE 桩（归档起点超出 8KB 尾窗）
    // ──────────────────────────────────────────────
    // 旧实现只扫「头 4KB + 尾 8KB」，而真实 SFX（WinRAR / 7-Zip GUI 生成）的归档紧贴
    // PE 镜像末尾、起点在几百 KB 处 → 探测恒为 false，用户看到「不支持的文件格式: .exe」。
    // 下面用带合法节表的 ~128KB 桩复现：归档起点既不在头 16KB 内、也不在尾 8KB 内。

    [Fact]
    public void SfxProbe_LargePeStubWithZipPayload_DetectsPayloadBeyondTailWindow()
    {
        var payload = BuildZip(("big.bin", Incompressible(40_000)));
        var path = Path.Combine(_dir, "large-zip-sfx.exe");
        File.WriteAllBytes(path, BuildPeSfx(payload, gapBytes: 187));

        var bytes = File.ReadAllBytes(path);
        var head = ReadHead(bytes);

        // 前提校验：归档起点确实在旧窗口之外（否则这条测试守不住任何东西）
        long payloadStart = Array.IndexOf(bytes, (byte)0x50, 100_000);
        Assert.True(payloadStart > 16 * 1024, $"payload 起点 {payloadStart} 应远离头窗");
        Assert.True(bytes.Length - payloadStart > SfxProbe.TailWindowBytes,
            "payload 起点应落在尾窗之外");

        Assert.True(SfxProbe.TryDetectEmbeddedContainer(path, head, head.Length, out var container));
        Assert.Equal(ContainerFormat.Zip, container);
        Assert.True(SfxProbe.TryDetectPayload(path, out var kind));
        Assert.Equal(SfxPayloadKind.Zip, kind);
    }

    [Fact]
    public void SfxProbe_LargePeStubWithSevenZipPayload_DetectsSevenZip()
    {
        // 7z 签名 + 填充（内容无关紧要，只验证签名定位）
        var payload = new byte[40_000];
        new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }.CopyTo(payload, 0);
        var path = Path.Combine(_dir, "large-7z-sfx.exe");
        File.WriteAllBytes(path, BuildPeSfx(payload, gapBytes: 0));

        Assert.True(SfxProbe.TryDetectPayload(path, out var kind));
        Assert.Equal(SfxPayloadKind.SevenZip, kind);
    }

    [Theory]
    [InlineData(0x00, SfxPayloadKind.Rar4)]
    [InlineData(0x01, SfxPayloadKind.Rar5)]
    public void SfxProbe_LargePeStubWithRarPayload_DistinguishesRarVersion(
        byte versionByte, SfxPayloadKind expected)
    {
        // RAR 签名第 7 字节是版本：7z.dll 的 Rar4 / Rar 是两套格式串，传错直接 Invalid archive
        var payload = new byte[40_000];
        new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, versionByte, 0x00 }.CopyTo(payload, 0);
        var path = Path.Combine(_dir, $"large-rar-sfx-{versionByte}.exe");
        File.WriteAllBytes(path, BuildPeSfx(payload, gapBytes: 0));

        Assert.True(SfxProbe.TryDetectPayload(path, out var kind));
        Assert.Equal(expected, kind);

        // 容器层面统一报 Rar（RAR4 / RAR5 是同一容器格式）
        var head = ReadHead(File.ReadAllBytes(path));
        Assert.True(SfxProbe.TryDetectEmbeddedContainer(path, head, head.Length, out var container));
        Assert.Equal(ContainerFormat.Rar, container);
    }

    [Fact]
    public void SfxProbe_LargePeStubWithoutPayload_ReturnsFalse()
    {
        var path = Path.Combine(_dir, "large-plain.exe");
        File.WriteAllBytes(path, BuildPeStub(128 * 1024));

        Assert.False(SfxProbe.TryDetectPayload(path, out _));
    }

    [Fact]
    public void GetEngineByExtension_LargePeSfx_RoutesToSevenZipEngine()
    {
        var path = Path.Combine(_dir, "large-self-extract.exe");
        File.WriteAllBytes(path, BuildPeSfx(BuildZip(("big.bin", Incompressible(40_000))), gapBytes: 187));

        var engine = ArchiveEngineFactory.GetEngineByExtension(path);

        Assert.NotNull(engine);
        Assert.Equal("SevenZipEngine", engine!.GetType().Name);
    }

    [Fact]
    public void SevenZipExtractorFactory_ResolvesInArchiveFormatForEachSfxPayload()
    {
        // 显式传 InArchiveFormat 才能让 7z.dll 跳过「逐格式试探」（大桩 SFX 的失败点）
        Assert.Equal(InArchiveFormat.Zip, HintFor(BuildSfxPayload(0x50)));
        Assert.Equal(InArchiveFormat.SevenZip, HintFor(BuildSfxPayload(0x37)));
        Assert.Equal(InArchiveFormat.Rar4, HintFor(BuildSfxPayload(0x52, rarVersion: 0x00)));
        Assert.Equal(InArchiveFormat.Rar, HintFor(BuildSfxPayload(0x52, rarVersion: 0x01)));

        // 普通压缩包不介入：仍交给 SharpSevenZip 自动探测
        var plain = Path.Combine(_dir, "plain.7z");
        File.WriteAllBytes(plain, BuildPeStub(0));
        Assert.Null(SevenZipExtractorFactory.TryResolveFormatHint(plain));

        InArchiveFormat? HintFor(byte[] content)
        {
            var p = Path.Combine(_dir, $"hint-{Guid.NewGuid():N}.exe");
            File.WriteAllBytes(p, content);
            return SevenZipExtractorFactory.TryResolveFormatHint(p);
        }
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
                // 显式限定：文件顶部 `using SharpSevenZip;` 引入了同名的 CompressionLevel
                var entry = za.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
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

    /// <summary>
    /// 造一个带**合法 PE 头 + 单节表**的桩，镜像末尾 = <c>0x400 + rawSize</c>
    /// （section 的 <c>PointerToRawData</c>=0x400、<c>SizeOfRawData</c>=rawSize）。
    /// </summary>
    /// <remarks>
    /// 真实 SFX 的归档就贴在镜像末尾之后，所以要覆盖「大桩」必须给出可解析的节表——
    /// 只有 MZ 头（<see cref="BuildSfx"/>）的夹具会让 SfxProbe 退回旧的头/尾窗口，测不出回归。
    /// </remarks>
    private static byte[] BuildPeStub(int rawSize)
    {
        if (rawSize <= 0)
            return BuildSfx(null);

        var stub = new byte[0x400 + rawSize];
        stub[0] = 0x4D; // MZ
        stub[1] = 0x5A;
        stub[2] = 0x90;
        stub[3] = 0x00;
        WriteInt32(stub, 0x3C, 0x80);        // e_lfanew → PE 头

        // PE\0\0 @ 0x80
        stub[0x80] = 0x50;
        stub[0x81] = 0x45;
        stub[0x82] = 0x00;
        stub[0x83] = 0x00;

        const int coff = 0x84;               // COFF 头紧随签名
        WriteInt32(stub, coff + 2, 1);       // NumberOfSections = 1
        WriteInt32(stub, coff + 16, 0xE0);   // SizeOfOptionalHeader

        int section = coff + 20 + 0xE0;      // 节表 = COFF 头 + 可选头（0x178）
        WriteInt32(stub, section + 16, rawSize);  // SizeOfRawData
        WriteInt32(stub, section + 20, 0x400);    // PointerToRawData
        return stub;
    }

    /// <summary>造「128KB 大 PE 桩 + gap 字节填充 + 载荷」的自解压文件。</summary>
    private static byte[] BuildPeSfx(byte[] payload, int gapBytes)
    {
        var stub = BuildPeStub(128 * 1024);
        var result = new byte[stub.Length + gapBytes + payload.Length];
        Buffer.BlockCopy(stub, 0, result, 0, stub.Length);
        Buffer.BlockCopy(payload, 0, result, stub.Length + gapBytes, payload.Length);
        return result;
    }

    /// <summary>造「大 PE 桩 + 指定签名的 40KB 载荷」，供格式提示映射测试用。</summary>
    private static byte[] BuildSfxPayload(byte firstByte, byte rarVersion = 0x01)
    {
        var payload = new byte[40_000];
        switch (firstByte)
        {
            case 0x50: // PK\x03\x04
                new byte[] { 0x50, 0x4B, 0x03, 0x04 }.CopyTo(payload, 0);
                break;
            case 0x37: // 7z
                new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }.CopyTo(payload, 0);
                break;
            case 0x52: // Rar!\x1A\x07 + 版本
                new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, rarVersion }.CopyTo(payload, 0);
                break;
        }
        return BuildPeSfx(payload, gapBytes: 0);
    }

    /// <summary>不可压缩的载荷（全零会被 deflate 压到几十字节，就把归档起点塞进尾窗了）。</summary>
    private static byte[] Incompressible(int size)
    {
        var buffer = new byte[size];
        Random.Shared.NextBytes(buffer);
        return buffer;
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);
}
