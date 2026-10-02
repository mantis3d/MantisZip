using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Tests.Fixtures;
using Xunit;

namespace MantisZip.Tests.Engines;

public class ZipEngineTests : IDisposable
{
    private readonly ZipEngine _engine = new();
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    public ZipEngineTests()
    {
        // Registration needed for StringCodec on code-page encoded ZIPs (used by ZipEngine.OpenZipFile)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles.Where(File.Exists))
            try { File.Delete(f); } catch { }
        foreach (var d in _tempDirs.Where(Directory.Exists))
            try { Directory.Delete(d, true); } catch { }
    }

    private string TrackFile(string path) { _tempFiles.Add(path); return path; }
    private string TrackDir(string path) { _tempDirs.Add(path); return path; }

    // ===== CanHandle =====

    [Fact]
    public void CanHandle_Zip_ReturnsTrue()
    {
        Assert.True(_engine.CanHandle(ArchiveFormat.Zip));
    }

    [Fact]
    public void CanHandle_OtherFormats_ReturnsFalse()
    {
        Assert.False(_engine.CanHandle(ArchiveFormat.SevenZip));
        Assert.False(_engine.CanHandle(ArchiveFormat.Tar));
        Assert.False(_engine.CanHandle(ArchiveFormat.GZip));
        Assert.False(_engine.CanHandle(ArchiveFormat.Rar));
        Assert.False(_engine.CanHandle(ArchiveFormat.Iso));
    }

    // ===== ListEntriesAsync =====

    [Fact]
    public async Task ListEntriesAsync_ReturnsEntries()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        var entries = await _engine.ListEntriesAsync(archive);

        Assert.NotEmpty(entries);
        var helloEntry = Assert.Single(entries, e => e.Name == "hello.txt");
        Assert.Equal(ArchiveFixtures.HelloText.Length, helloEntry.Size);
        Assert.False(helloEntry.IsDirectory);

        var nestedEntry = Assert.Single(entries, e => e.Name == "subdir/nested.txt");
        Assert.Equal(ArchiveFixtures.NestedDirFileContent.Length, nestedEntry.Size);
    }

    [Fact]
    public async Task ListEntriesAsync_EncryptedWithoutPassword_StillReturnsEntries()
    {
        // SharpZipLib can enumerate entries without decrypting
        var archive = TrackFile(ArchiveFixtures.CreateEncryptedZipArchive());

        var entries = await _engine.ListEntriesAsync(archive);

        Assert.NotEmpty(entries);
        Assert.Single(entries, e => e.Name == "secret.txt");
    }

    [Fact]
    public async Task ListEntriesAsync_CorruptZeroFilledFile_Throws()
    {
        // 全零填充的损坏 .zip（下载失败/复制中断产物）：必须报错，而不是静默返回空列表。
        // 回归：OpenArchiveWithEncodingFallback 曾用 ArchiveFactory.OpenArchive 魔数嗅探，
        // 全零文件被误判为 Tar（0 条目）导致损坏被吞掉。
        var corrupt = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}.zip"));
        Directory.CreateDirectory(Path.GetDirectoryName(corrupt)!);
        File.WriteAllBytes(corrupt, new byte[4096]); // all zeros

        await Assert.ThrowsAsync<SharpCompress.Common.ArchiveException>(() => _engine.ListEntriesAsync(corrupt));
    }

    [Fact]
    public async Task TestArchiveAsync_CorruptZeroFilledFile_ReturnsFalse()
    {
        var corrupt = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}.zip"));
        Directory.CreateDirectory(Path.GetDirectoryName(corrupt)!);
        File.WriteAllBytes(corrupt, new byte[4096]); // all zeros

        var ok = await _engine.TestArchiveAsync(corrupt);

        Assert.False(ok, "损坏的压缩包测试必须返回 false，不能瞬时通过");
    }

    [Fact]
    public async Task ListEntriesAsync_ValidEmptyZip_ReturnsEmpty()
    {
        // 合法空压缩包（仅 EOCD，无条目）不应被严格解析误伤。
        var empty = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}.zip"));
        Directory.CreateDirectory(Path.GetDirectoryName(empty)!);
        // EOCD 签名 PK\x05\x06 + 18 字节其余字段 = 22 字节
        byte[] eocd = { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        File.WriteAllBytes(empty, eocd);

        var entries = await _engine.ListEntriesAsync(empty);

        Assert.Empty(entries);
    }

    // ===== ExtractAsync =====

    [Fact]
    public async Task ExtractAsync_ExtractsFiles()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));

        await _engine.ExtractAsync(archive, dest);

        Assert.True(File.Exists(Path.Combine(dest, "hello.txt")));
        Assert.Equal(ArchiveFixtures.HelloText, await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "subdir", "nested.txt")));
        Assert.Equal(ArchiveFixtures.NestedDirFileContent, await File.ReadAllTextAsync(Path.Combine(dest, "subdir", "nested.txt")));
    }

    [Fact]
    public async Task ExtractAsync_WithPassword_ExtractsEncrypted()
    {
        var archive = TrackFile(ArchiveFixtures.CreateEncryptedZipArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));

        await _engine.ExtractAsync(archive, dest, "test123");

        Assert.True(File.Exists(Path.Combine(dest, "secret.txt")));
        Assert.Equal("secret data", await File.ReadAllTextAsync(Path.Combine(dest, "secret.txt")));
    }

    [Fact]
    public async Task ExtractAsync_WithoutPassword_ThrowsOnEncrypted()
    {
        var archive = TrackFile(ArchiveFixtures.CreateEncryptedZipArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _engine.ExtractAsync(archive, dest));
    }

    [Fact]
    public async Task ExtractAsync_WithConflictOverwrite_Overwrites()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "hello.txt"), "old content");

        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Overwrite };
        await _engine.ExtractAsync(archive, dest, options: options);

        Assert.Equal(ArchiveFixtures.HelloText, await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task ExtractAsync_WithConflictSkip_SkipsExisting()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "hello.txt"), "old content");

        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Skip };
        await _engine.ExtractAsync(archive, dest, options: options);

        // Should keep old content
        Assert.Equal("old content", await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task ExtractAsync_WithConflictRename_RenamesNewFile()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "hello.txt"), "old content");

        var options = new ArchiveOptions { ConflictAction = FileConflictAction.Rename };
        await _engine.ExtractAsync(archive, dest, options: options);

        // Original preserved, new file renamed
        Assert.Equal("old content", await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "hello (1).txt")));
    }

    // ===== CompressAsync =====

    [Fact]
    public async Task CompressAsync_CreatesValidArchive()
    {
        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.zip"));

        await _engine.CompressAsync([srcDir], outputPath, new ArchiveOptions());

        Assert.True(File.Exists(outputPath));

        // Verify by listing entries
        var entries = await _engine.ListEntriesAsync(outputPath);
        Assert.Contains(entries, e => e.Name.Contains("hello.txt"));
        Assert.Contains(entries, e => e.Name.Contains("subdir/nested.txt"));
    }

    [Fact]
    public async Task CompressAsync_RespectsCompressionLevel()
    {
        var srcDir = TrackDir(ArchiveFixtures.CreateSourceDirectory());
        var outputPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.zip"));

        await _engine.CompressAsync([srcDir], outputPath, new ArchiveOptions { CompressionLevel = 1 });

        Assert.True(File.Exists(outputPath));
        var entries = await _engine.ListEntriesAsync(outputPath);
        Assert.NotEmpty(entries);
    }

    // ===== TestArchiveAsync =====

    [Fact]
    public async Task TestArchiveAsync_ValidArchive_ReturnsTrue()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());

        var result = await _engine.TestArchiveAsync(archive);

        Assert.True(result);
    }

    [Fact]
    public async Task TestArchiveAsync_InvalidArchive_ReturnsFalse()
    {
        var badPath = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid()}.zip"));
        File.WriteAllText(badPath, "not a zip file");

        var result = await _engine.TestArchiveAsync(badPath);

        Assert.False(result);
    }

    [Fact]
    public async Task TestArchiveAsync_EncryptedArchive_ReturnsTrue()
    {
        var archive = TrackFile(ArchiveFixtures.CreateEncryptedZipArchive());

        var result = await _engine.TestArchiveAsync(archive, "test123");

        Assert.True(result);
    }

    // ===== AddToArchiveAsync =====

    [Fact]
    public async Task AddToArchiveAsync_AddsFiles()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var newFile = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), "added.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
        await File.WriteAllTextAsync(newFile, "added content");
        _tempFiles.Add(newFile);

        await _engine.AddToArchiveAsync(archive, [newFile], new ArchiveOptions());

        // Verify the file was added
        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.Name == "added.txt");
    }

    // ===== 冲突处理集成测试（copy-mode，默认不走加密路径） =====

    private async Task<string> CreateDupFileAsync(string name, string content)
    {
        var file = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString(), name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, content);
        _tempFiles.Add(file);
        return file;
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Overwrite_ReplacesContent()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive()); // hello.txt + subdir/nested.txt
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        await _engine.AddToArchiveAsync(archive, [dupFile], new ArchiveOptions { ConflictAction = FileConflictAction.Overwrite });

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Equal(1, entries.Count(e => e.Name == "hello.txt"));

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(archive, dest);
        Assert.Equal("duplicate content", await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
        // 非冲突条目必须保留（keepEntryNames 排除逻辑的正确性验证）
        Assert.Equal(ArchiveFixtures.NestedDirFileContent, await File.ReadAllTextAsync(Path.Combine(dest, "subdir", "nested.txt")));
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Skip_KeepsOriginal()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        await _engine.AddToArchiveAsync(archive, [dupFile], new ArchiveOptions { ConflictAction = FileConflictAction.Skip });

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Equal(1, entries.Count(e => e.Name == "hello.txt"));

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(archive, dest);
        Assert.Equal(ArchiveFixtures.HelloText, await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Rename_AddsUniqueEntry()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        await _engine.AddToArchiveAsync(archive, [dupFile], new ArchiveOptions { ConflictAction = FileConflictAction.Rename });

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.Name == "hello.txt");
        Assert.Contains(entries, e => e.Name == "hello (1).txt");
        // 非冲突条目必须保留（keepEntryNames == null 分支仍走完整重写）
        Assert.Contains(entries, e => e.Name == "subdir/nested.txt");
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Ask_ResolverSkip_KeepsOriginal()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = _ => Task.FromResult(FileConflictAction.Skip),
        };
        await _engine.AddToArchiveAsync(archive, [dupFile], options);

        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        await _engine.ExtractAsync(archive, dest);
        Assert.Equal(ArchiveFixtures.HelloText, await File.ReadAllTextAsync(Path.Combine(dest, "hello.txt")));
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Ask_ResolverCustomName()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = info =>
            {
                info.CustomName = "my-rename.txt";
                return Task.FromResult(FileConflictAction.Rename);
            },
        };
        await _engine.AddToArchiveAsync(archive, [dupFile], options);

        var entries = await _engine.ListEntriesAsync(archive);
        Assert.Contains(entries, e => e.Name == "hello.txt");
        Assert.Contains(entries, e => e.Name == "my-rename.txt");
        // 非冲突条目必须保留（keepEntryNames == null 分支仍走完整重写）
        Assert.Contains(entries, e => e.Name == "subdir/nested.txt");
    }

    [Fact]
    public async Task AddToArchiveAsync_DuplicateName_Ask_Cancel_AbortsOperation()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dupFile = await CreateDupFileAsync("hello.txt", "duplicate content");

        var options = new ArchiveOptions
        {
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = _ => throw new OperationCanceledException("用户取消"),
        };
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _engine.AddToArchiveAsync(archive, [dupFile], options));
    }

    // ===== 中文文件名编码（拖拽添加乱码 bug 回归锁） =====

    /// <summary>读取中央目录里每个条目的 UTF-8 标志（bit 11）与文件名原始字节。</summary>
    private static List<(ushort Flags, byte[] NameBytes)> ReadCentralDirectoryRaw(string archive)
    {
        var buf = File.ReadAllBytes(archive);
        byte[] eocdSig = [0x50, 0x4b, 0x05, 0x06];
        int eocd = -1;
        for (int i = buf.Length - 22; i >= 0; i--)
            if (buf[i] == eocdSig[0] && buf[i + 1] == eocdSig[1] &&
                buf[i + 2] == eocdSig[2] && buf[i + 3] == eocdSig[3])
            { eocd = i; break; }
        Assert.True(eocd >= 0, "未找到 EOCD");

        uint cdOffset = BitConverter.ToUInt32(buf, eocd + 16);
        uint cdSize = BitConverter.ToUInt32(buf, eocd + 12);
        var result = new List<(ushort, byte[])>();
        int p = (int)cdOffset;
        int end = (int)(cdOffset + cdSize);
        while (p < end)
        {
            if (BitConverter.ToUInt32(buf, p) != 0x02014b50) break;
            ushort flags = BitConverter.ToUInt16(buf, p + 8);
            int nameLen = BitConverter.ToUInt16(buf, p + 28);
            int extraLen = BitConverter.ToUInt16(buf, p + 30);
            int commentLen = BitConverter.ToUInt16(buf, p + 32);
            var name = new byte[nameLen];
            Array.Copy(buf, p + 46, name, 0, nameLen);
            result.Add((flags, name));
            p += 46 + nameLen + extraLen + commentLen;
        }
        return result;
    }

    private async Task<string> CreateCjkFileAsync(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, name);
        await File.WriteAllTextAsync(file, "内容");
        TrackDir(dir);
        return file;
    }

    /// <summary>
    /// 用引擎自身建一个 ZIP（与用户在 MantisZip 内新建压缩包的路径一致），
    /// 作为 AddToArchiveAsync 的目标包。
    /// <para>
    /// 不用 ArchiveFixtures.CreateZipArchive()：它用 SharpZipLib 写出的是另一种
    /// 结构，实测那条路径下新条目 bit 11 恰好为 True，无法复现本 bug。
    /// 必须用引擎产物才能命中「copy-mode 快速路径 + 新条目标志缺失」这一真实缺陷。
    /// </para>
    /// </summary>
    private async Task<string> CreateEngineBuiltZipAsync(string sourceFileName = "readme.txt")
    {
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        TrackDir(dir);
        var src = Path.Combine(dir, sourceFileName);
        await File.WriteAllTextAsync(src, "seed");
        var archive = Path.Combine(dir, "seed.zip");
        await _engine.CompressAsync([src], archive,
            new ArchiveOptions { Format = ArchiveFormat.Zip, FileNameEncoding = "utf-8" });
        return TrackFile(archive);
    }

    /// <summary>
    /// D 编码锁（copy-mode 快速路径）：显式 FileNameEncoding="utf-8" 时，
    /// 中文新条目必须以 UTF-8 字节写入**且置 bit 11 标志**。
    /// <para>
    /// 回归前的 bug：ZipBinaryRewriter 构造 LFH/CDFH 时硬编码 Flags=0，
    /// 从不置 bit 11。违反 APPNOTE 6.4.4 —— 非 ASCII 文件名未标志 UTF-8 时，
    /// 解码器按 CP437 解释 → 第三方工具（7-Zip/WinRAR/资源管理器/unzip）显示乱码。
    /// MantisZip 自己因 OpenArchiveWithEncodingFallback 的 LooksLikeValidCjk 启发式
    /// 能猜对，故 bug 在本应用内不可见，这是它潜伏至今的原因。
    /// </para>
    /// </summary>
    [Fact]
    public async Task AddToArchiveAsync_ExplicitUtf8_WritesCjkNameAsUtf8_WithUtf8Flag()
    {
        var archive = await CreateEngineBuiltZipAsync();
        var cjkFile = await CreateCjkFileAsync("季度总结.txt");

        await _engine.AddToArchiveAsync(archive, [cjkFile],
            new ArchiveOptions { FileNameEncoding = "utf-8" });

        var raw = ReadCentralDirectoryRaw(archive);
        var entry = raw.FirstOrDefault(e =>
            Encoding.UTF8.GetString(e.NameBytes).Contains("季度总结"));
        Assert.True(entry.NameBytes is { Length: > 0 }, "未找到 UTF-8 编码的季度总结条目");
        Assert.Equal("季度总结.txt", Encoding.UTF8.GetString(entry.NameBytes));
        Assert.True((entry.Flags & 0x0800) != 0,
            $"UTF-8 新条目必须置 bit 11（APPNOTE 6.4.4），实际 flags=0x{entry.Flags:X4}；" +
            "缺失时第三方工具按 CP437 解码将显示乱码");
    }

    /// <summary>
    /// D 编码锁（copy-mode 快速路径）：显式 FileNameEncoding="gbk" 时，
    /// 中文新条目以 GBK 字节写入且**不置** bit 11（供旧工具兼容）。
    /// 同时验证 UTF-8 设置不再被 ZipHasUtf8Flag 启发式覆盖。
    /// </summary>
    [Fact]
    public async Task AddToArchiveAsync_ExplicitGbk_WritesCjkNameAsGbk_WithoutUtf8Flag()
    {
        var archive = await CreateEngineBuiltZipAsync();
        var cjkFile = await CreateCjkFileAsync("月度报表.txt");

        await _engine.AddToArchiveAsync(archive, [cjkFile],
            new ArchiveOptions { FileNameEncoding = "gbk" });

        var raw = ReadCentralDirectoryRaw(archive);
        var gbk = Encoding.GetEncoding("GBK");
        var entry = raw.FirstOrDefault(e => gbk.GetString(e.NameBytes).Contains("月度报表"));
        Assert.True(entry.NameBytes is { Length: > 0 }, "未找到 GBK 编码的月度报表条目");
        Assert.Equal("月度报表.txt", gbk.GetString(entry.NameBytes));
        Assert.Equal(0, entry.Flags & 0x0800);
        // 反向锁：不能是 UTF-8 字节
        Assert.NotEqual("月度报表.txt", Encoding.UTF8.GetString(entry.NameBytes));
    }

    /// <summary>
    /// D 编码锁（copy-mode 快速路径，核心回归）：源文件名含中文时，
    /// 新条目的 bit 11 必须与所用编码一致 —— UTF-8 编码必须置位。
    /// 这条直接锁住「拖拽添加中文文件 → 其它工具打开显示乱码」的用户可见症状。
    /// </summary>
    [Fact]
    public async Task AddToArchiveAsync_CjkEntry_SetsUtf8FlagPerAppendix()
    {
        var archive = await CreateEngineBuiltZipAsync();
        var cjkFile = await CreateCjkFileAsync("会议纪要.txt");

        await _engine.AddToArchiveAsync(archive, [cjkFile],
            new ArchiveOptions { FileNameEncoding = "utf-8" });

        var raw = ReadCentralDirectoryRaw(archive);
        foreach (var (flags, nameBytes) in raw)
        {
            bool highAscii = nameBytes.Any(b => b > 127);
            if (!highAscii) continue;
            // 名字含高位字符 → 必须置 bit 11，否则按 CP437 解出乱码
            Assert.True((flags & 0x0800) != 0,
                $"条目 '{Encoding.UTF8.GetString(nameBytes)}' 含非 ASCII 字符但未置 bit 11 " +
                $"(flags=0x{flags:X4})，违反 APPNOTE 6.4.4，第三方工具将显示乱码");
        }
    }

    /// <summary>
    /// D 编码锁（legacy 回退路径，密码强制触发）：legacy 路径会用 FileNameEncoding
    /// 重写整个包。回归前的 bug：未透传设置时整包被降级重写为 GBK，
    /// 已有的正确 UTF-8 条目全部损坏。
    /// </summary>
    [Fact]
    public async Task AddToArchiveAsync_EncryptedLegacyPath_PreservesExistingUtf8Entries()
    {
        // 先用 UTF-8 建一个含中文的包
        var cjkFile = await CreateCjkFileAsync("原始数据.txt");
        var archiveDir = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(archiveDir);
        TrackDir(archiveDir);
        var archive = TrackFile(Path.Combine(archiveDir, "enc.zip"));
        await _engine.CompressAsync([cjkFile], archive,
            new ArchiveOptions { Format = ArchiveFormat.Zip, FileNameEncoding = "utf-8" });

        // 加密添加强制走 legacy 路径（copy-mode 不支持加密）
        var added = await CreateCjkFileAsync("新增文件.txt");
        await _engine.AddToArchiveAsync(archive, [added],
            new ArchiveOptions { Format = ArchiveFormat.Zip, FileNameEncoding = "utf-8", Encrypt = true, Password = "pw123" });

        // 原有 UTF-8 条目必须仍是正确的中文（未被降级成 GBK 字节）
        var entries = await _engine.ListEntriesAsync(archive, "pw123");
        Assert.Contains(entries, e => e.Name == "原始数据.txt");
        Assert.Contains(entries, e => e.Name == "新增文件.txt");
    }

    // ===== 删除路径的编码回归锁 =====

    /// <summary>
    /// 删除条目时，存活的其它条目会被整包重写。回归前的 bug：
    /// DeleteEntriesAsync 用 ZipHasUtf8Flag 启发式猜编码（与 Add 路径不一致，
    /// 未透传用户的 ZipEncoding 设置），会把存活的中文条目降级重写成 GBK 字节，
    /// 导致「本来正常的文件在删除另一个文件后变成乱码」。
    /// </summary>
    [Fact]
    public async Task DeleteEntriesAsync_SurvivingCjkEntries_KeepUtf8Encoding()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        TrackDir(dir);

        // 建一个含 3 个中文条目的 UTF-8 包
        var src = Path.Combine(dir, "src");
        Directory.CreateDirectory(src);
        foreach (var n in new[] { "要删除.txt", "保留甲.txt", "保留乙.txt" })
            await File.WriteAllTextAsync(Path.Combine(src, n), "x");

        var archive = TrackFile(Path.Combine(dir, "del.zip"));
        await _engine.CompressAsync([src], archive,
            new ArchiveOptions { Format = ArchiveFormat.Zip, FileNameEncoding = "utf-8" });

        var target = (await _engine.ListEntriesAsync(archive)).First(e => e.Name.EndsWith("要删除.txt"));
        await _engine.DeleteEntriesAsync(archive, [target.Name],
            options: new ArchiveOptions { FileNameEncoding = "utf-8" });

        // 存活条目的字节必须是 UTF-8 且置 bit 11（未被降级为 GBK）
        var raw = ReadCentralDirectoryRaw(archive);
        foreach (var (flags, nameBytes) in raw)
        {
            if (!nameBytes.Any(b => b > 127)) continue;
            var asUtf8 = Encoding.UTF8.GetString(nameBytes);
            var asGbk = Encoding.GetEncoding("GBK").GetString(nameBytes);
            // 字节是 UTF-8 → 用 GBK 解必须得到不同的（乱码）结果
            Assert.NotEqual(asUtf8, asGbk);
            // 且 GBK 解码结果不应是任何合法中文名（若相等说明存的是 GBK 字节）
            Assert.False(asGbk.Contains("保留"), $"存活条目被降级成 GBK 字节：GBK 解码得 '{asGbk}'");
            Assert.True((flags & 0x0800) != 0,
                $"存活条目 '{asUtf8}' 在删除后丢失 bit 11（flags=0x{flags:X4}），将被第三方工具显示为乱码");
        }

        var after = await _engine.ListEntriesAsync(archive);
        Assert.DoesNotContain(after, e => e.Name.EndsWith("要删除.txt"));
        Assert.Contains(after, e => e.Name.EndsWith("保留甲.txt"));
        Assert.Contains(after, e => e.Name.EndsWith("保留乙.txt"));
    }

    /// <summary>
    /// 删除路径不得误删：不同目录下的同名文件，删其中一个，另一个必须保留。
    /// </summary>
    [Fact]
    public async Task DeleteEntriesAsync_SameNameDifferentDirs_DeletesOnlyTarget()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        TrackDir(dir);

        var d1 = Path.Combine(dir, "文档A");
        var d2 = Path.Combine(dir, "文档B");
        Directory.CreateDirectory(d1);
        Directory.CreateDirectory(d2);
        await File.WriteAllTextAsync(Path.Combine(d1, "同名.txt"), "AAA");
        await File.WriteAllTextAsync(Path.Combine(d2, "同名.txt"), "BBB");
        await File.WriteAllTextAsync(Path.Combine(d2, "独有.txt"), "CCC");

        var archive = TrackFile(Path.Combine(dir, "dup.zip"));
        await _engine.CompressAsync([d1, d2], archive,
            new ArchiveOptions { Format = ArchiveFormat.Zip, FileNameEncoding = "utf-8" });

        var target = (await _engine.ListEntriesAsync(archive)).First(e => e.Name.StartsWith("文档A"));
        await _engine.DeleteEntriesAsync(archive, [target.Name],
            options: new ArchiveOptions { FileNameEncoding = "utf-8" });

        var after = await _engine.ListEntriesAsync(archive);
        Assert.DoesNotContain(after, e => e.Name.StartsWith("文档A"));
        Assert.Contains(after, e => e.Name.StartsWith("文档B") && e.Name.EndsWith("同名.txt"));
        Assert.Contains(after, e => e.Name.EndsWith("独有.txt"));
        Assert.Equal(2, after.Count);
    }

    /// <summary>
    /// D 编码锁（删除路径核心回归）：删除一个条目<b>不得改变其它条目的文件名编码</b>。
    /// <para>
    /// 回归前的 bug：ZipBinaryRewriter.ReadCentralDirectory 固定用 UTF-8 解码文件名，
    /// WriteCentralDirectory 又用传入 encoding 重编码 → 「解码→重编码」往返把
    /// 非 UTF-8 编码的条目损坏成乱码。删除一个文件会连带毁掉无关文件。
    /// </para>
    /// <para>
    /// 修复：CdEntry 保留 RawFileNameBytes，既有条目原样写回。
    /// 本用例构造 <b>GBX 编码（无 bit 11）</b>的包 —— 修复前必然被毁成乱码。
    /// </para>
    /// </summary>
    [Fact]
    public async Task DeleteEntriesAsync_NonUtf8Archive_SurvivingEntriesKeepOriginalBytes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        TrackDir(dir);

        // 用 GBK 建包：无 bit 11，条目名是 GBK 字节
        var src = Path.Combine(dir, "src");
        Directory.CreateDirectory(src);
        foreach (var n in new[] { "要删.txt", "留下甲.txt", "留下乙.txt" })
            await File.WriteAllTextAsync(Path.Combine(src, n), "x");

        var archive = TrackFile(Path.Combine(dir, "gbk.zip"));
        await _engine.CompressAsync([src], archive,
            new ArchiveOptions { Format = ArchiveFormat.Zip, FileNameEncoding = "gbk" });

        // 记录删除前的原始字节
        var beforeBytes = ReadCentralDirectoryRaw(archive).ToDictionary(
            e => Encoding.GetEncoding("GBK").GetString(e.NameBytes), e => Convert.ToHexString(e.NameBytes));

        var target = (await _engine.ListEntriesAsync(archive)).First(e => e.Name.EndsWith("要删.txt"));
        await _engine.DeleteEntriesAsync(archive, [target.Name],
            options: new ArchiveOptions { FileNameEncoding = "gbk" });

        // 存活条目的原始字节必须与删除前**逐字节相同**（GBK 编码未被改动）
        var gbk = Encoding.GetEncoding("GBK");
        foreach (var (flags, nameBytes) in ReadCentralDirectoryRaw(archive))
        {
            var name = gbk.GetString(nameBytes);
            if (name.Contains("要删")) continue;
            Assert.True(beforeBytes.ContainsKey(name), $"删除后出现未知条目 '{name}'");
            Assert.Equal(beforeBytes[name], Convert.ToHexString(nameBytes));
            Assert.Equal(0, flags & 0x0800);
        }

        // 并且条目仍能正确读回
        var after = await _engine.ListEntriesAsync(archive);
        Assert.DoesNotContain(after, e => e.Name.EndsWith("要删.txt"));
        Assert.Contains(after, e => e.Name.EndsWith("留下甲.txt"));
        Assert.Contains(after, e => e.Name.EndsWith("留下乙.txt"));
    }

    // ===== Progress Reporting =====

    [Fact]
    public async Task ExtractAsync_ReportsProgress()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive());
        var dest = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString()));
        var progressItems = new List<ArchiveProgress>();

        await _engine.ExtractAsync(archive, dest, progress: new Progress<ArchiveProgress>(p =>
        {
            progressItems.Add(p);
        }));

        // Should have at least initial and final progress reports
        Assert.NotEmpty(progressItems);
        Assert.Contains(progressItems, p => p.PercentComplete == 100);
    }
}
