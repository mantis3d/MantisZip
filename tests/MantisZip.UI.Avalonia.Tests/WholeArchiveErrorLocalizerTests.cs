using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 整包错误本地化映射测试。
/// 断言映射到正确的本地化 key（而非裸露异常原文），并核对 detail 透传语义。
/// 断言用 <see cref="LocalizationManager.T(string, object?[])"/> 左右同时求值，
/// 使测试不依赖 Localization 资源在测试输出目录是否被复制。
/// </summary>
public class WholeArchiveErrorLocalizerTests
{
    [Fact]
    public void FileNotFound_WithFileName_UsesFileNotFoundKey()
    {
        var ex = new FileNotFoundException("Could not find file 'D:\\x\\a.zip'.", "D:\\x\\a.zip");

        var (message, detail) = WholeArchiveErrorLocalizer.ToUserText(ex);

        Assert.Equal(LocalizationManager.T("Error_Whole_FileNotFound", ex.FileName), message);
        Assert.Equal(ex.Message, detail);
    }

    [Fact]
    public void FileNotFound_WithoutFileName_FallsBackToGeneric()
    {
        var ex = new FileNotFoundException("Could not find file.");

        var (message, detail) = WholeArchiveErrorLocalizer.ToUserText(ex);

        Assert.Equal(LocalizationManager.T("Error_Whole_FallbackFmt", ex.Message), message);
        Assert.Equal(ex.Message, detail);
    }

    [Fact]
    public void DirectoryNotFound_UsesDirectoryNotFoundKey()
    {
        var ex = new DirectoryNotFoundException("Could not find a part of the path 'D:\\x'.");

        var (message, detail) = WholeArchiveErrorLocalizer.ToUserText(ex);

        Assert.Equal(LocalizationManager.T("Error_Whole_DirectoryNotFound", ex.Message), message);
        Assert.Equal(ex.Message, detail);
    }

    [Fact]
    public void UnauthorizedAccess_UsesUnauthorizedKey()
    {
        var ex = new UnauthorizedAccessException("Access to the path 'D:\\x' is denied.");

        var (message, detail) = WholeArchiveErrorLocalizer.ToUserText(ex);

        Assert.Equal(LocalizationManager.T("Error_Whole_UnauthorizedAccess", ex.Message), message);
        Assert.Equal(ex.Message, detail);
    }

    [Fact]
    public void InvalidData_PassesThrough_PreLocalized()
    {
        // thrower 侧已本地化（Status_ArchiveCorrupted），必须透传且不带 detail（避免双重本地化）
        var ex = new InvalidDataException("文件损坏或数据错误");

        var (message, detail) = WholeArchiveErrorLocalizer.ToUserText(ex);

        Assert.Equal(ex.Message, message);
        Assert.Null(detail);
    }

    [Fact]
    public void UnknownException_UsesFallbackKey_ContainingRawMessage()
    {
        var ex = new InvalidOperationException("boom");

        var (message, detail) = WholeArchiveErrorLocalizer.ToUserText(ex);

        Assert.Equal(LocalizationManager.T("Error_Whole_FallbackFmt", "boom"), message);
        Assert.Equal("boom", detail);
    }

    // ── 引擎损坏异常 → 本地化「文件损坏」（借真实语料异常，避免猜测异常构造函数） ──

    [Fact]
    public async Task ToUserText_SharpCompressArchiveException_LocalizesToCorrupted()
    {
        var engine = new MantisZip.Core.Engines.ZipEngine();
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => engine.ListEntriesAsync(CorpusPath("corrupt-01-truncated-tail.zip")));

        var (message, detail) = WholeArchiveErrorLocalizer.ToUserText(ex);

        Assert.Equal(LocalizationManager.T("Error_Whole_Corrupted"), message);
        Assert.Equal(ex.Message, detail);
    }

    [Fact]
    public async Task ToUserText_SharpCompressInvalidFormat_LocalizesToCorrupted()
    {
        var engine = new MantisZip.Core.Engines.TarGzEngine();
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => engine.ListEntriesAsync(CorpusPath("corrupt-22-targz-inner-not-tar.tar.gz")));

        var (message, _) = WholeArchiveErrorLocalizer.ToUserText(ex);

        Assert.Equal(LocalizationManager.T("Error_Whole_Corrupted"), message);
    }

    [Fact]
    public void ToUserText_SharpSevenZipArchiveException_LocalizesToCorruptedOrPassword()
    {
        var ex = new SharpSevenZip.Exceptions.SharpSevenZipArchiveException("Invalid archive: open/read error!");

        var (message, _) = WholeArchiveErrorLocalizer.ToUserText(ex);

        Assert.Equal(LocalizationManager.T("Error_Whole_CorruptedOrPassword"), message);
    }

    [Fact]
    public void ToUserText_ExtractionFailedException_LocalizesToCorrupted()
    {
        var ex = new SharpSevenZip.Exceptions.ExtractionFailedException("File is corrupted. Data error has occured.");

        var (message, _) = WholeArchiveErrorLocalizer.ToUserText(ex);

        Assert.Equal(LocalizationManager.T("Error_Whole_Corrupted"), message);
    }

    [Fact]
    public void ToUserText_EncryptedArchiveMessage_MapsToEncryptedKey()
    {
        var ex = new InvalidOperationException(
            "此压缩包已加密，请输入密码 (This archive is encrypted, password required)");

        var (message, _) = WholeArchiveErrorLocalizer.ToUserText(ex);

        Assert.Equal(LocalizationManager.T("Error_ArchiveEncrypted"), message);
    }

    [Fact]
    public async Task ToUserText_EncryptedZipExtractWithoutPassword_MapsToEncryptedKey()
    {
        var dest = Path.Combine(Path.GetTempPath(), "mztest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        try
        {
            var engine = new MantisZip.Core.Engines.ZipEngine();
            var ex = await Assert.ThrowsAnyAsync<Exception>(
                () => engine.ExtractAsync(CorpusPath("enc-01-zip-aes.zip"), dest));

            var (message, _) = WholeArchiveErrorLocalizer.ToUserText(ex);

            Assert.Equal(LocalizationManager.T("Error_ArchiveEncrypted"), message);
        }
        finally
        {
            try { Directory.Delete(dest, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ToUserText_ZipSlipInvalidPath_PassesThroughWithoutPrefix()
    {
        var dest = Path.Combine(Path.GetTempPath(), "mztest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        try
        {
            var engine = new MantisZip.Core.Engines.ZipEngine();
            var ex = await Assert.ThrowsAnyAsync<Exception>(
                () => engine.ExtractAsync(CorpusPath("edge-02-zip-slip.zip"), dest));

            var (message, detail) = WholeArchiveErrorLocalizer.ToUserText(ex);

            Assert.Equal(ex.Message, message); // 透传（Core 已是中文可读），无「操作失败：」前缀
            Assert.Null(detail);
        }
        finally
        {
            try { Directory.Delete(dest, recursive: true); } catch { }
        }
    }

    private static string CorpusPath(string fileName) =>
        Path.Combine(GetRepoRoot(), "tests", "corpus", "corrupt-archives", fileName);

    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException(
            $"无法定位仓库根（未找到 'src' 目录）。Base directory: {AppContext.BaseDirectory}");
    }
}
