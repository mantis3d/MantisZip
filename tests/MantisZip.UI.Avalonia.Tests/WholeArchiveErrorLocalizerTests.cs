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
}
