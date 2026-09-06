using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

public class IconProviderTests
{
    [Fact(Skip = "Needs SkiaSharp rendering context — run in app context")]
    public void GetFileIcon_DoesNotThrow()
    {
        // SkiaSharp context required — skip in unit test runner
    }

    [Fact(Skip = "Needs SkiaSharp rendering context — run in app context")]
    public void GetFolderIcon_DoesNotThrow()
    {
        // SkiaSharp context required — skip in unit test runner
    }

    [Fact]
    public void ClearCache_DoesNotThrow()
    {
        // ClearCache has no SkiaSharp dependency
        var exception = Record.Exception(() => IconProvider.ClearCache());
        Assert.Null(exception);
    }
}
