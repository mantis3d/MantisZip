using MantisZip.UI.Avalonia.Controls;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

public class QuickPathPickerDirectoryNormalizationTests : IDisposable
{
    private readonly string _tempDir;

    public QuickPathPickerDirectoryNormalizationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"MantisZipQPP_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void CoerceToDirectory_DirectoryPath_ReturnsAsIs()
    {
        Assert.Equal(_tempDir, QuickPathPicker.CoerceToDirectory(_tempDir));
    }

    [Fact]
    public void CoerceToDirectory_FilePath_ReturnsParentDirectory()
    {
        var file = Path.Combine(_tempDir, "sample.txt");
        File.WriteAllText(file, "x");
        Assert.Equal(_tempDir, QuickPathPicker.CoerceToDirectory(file));
    }

    [Fact]
    public void CoerceToDirectory_NonExistingPath_ReturnsAsIs()
    {
        var path = Path.Combine(_tempDir, "nonexistent_subdir");
        Assert.Equal(path, QuickPathPicker.CoerceToDirectory(path));
    }

    [Fact]
    public void CoerceToDirectory_Null_DoesNotThrow()
    {
        var result = Record.Exception(() => QuickPathPicker.CoerceToDirectory(null!));
        Assert.Null(result);
    }

    [Fact]
    public void CoerceToDirectory_Empty_ReturnsAsIs()
    {
        Assert.Equal(string.Empty, QuickPathPicker.CoerceToDirectory(string.Empty));
    }
}