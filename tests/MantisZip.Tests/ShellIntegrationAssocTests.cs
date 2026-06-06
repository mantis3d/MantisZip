using MantisZip.UI;
using Xunit;

namespace MantisZip.Tests;

/// <summary>
/// 文件关联操作的注册表测试集合定义。
/// 所有注册表测试必须串行执行，避免并发 Registry 读写冲突。
/// </summary>
[CollectionDefinition("RegistryTests", DisableParallelization = true)]
public class RegistryTestsCollection { }

/// <summary>
/// ShellIntegration 文件关联操作测试。
/// ⚠ 注册表测试需要在 Windows 上运行，且修改当前用户的 HKCU 注册表。
/// 所有测试使用 try/finally 确保清理自身产生的注册表条目。
/// </summary>
[Collection("RegistryTests")]
public class ShellIntegrationAssocTests
{
    private const string ProgId = "MantisZip.Archive";

    private static void CleanupExtension(string ext)
    {
        ShellIntegration.UninstallAssociationForExtension(ext);
    }

    /// <summary>检查 Registry 中指定扩展名是否有 MantisZip.Archive ProgId。</summary>
    private static bool HasProgId(string ext)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            $@"Software\Classes\{ext}\OpenWithProgids");
        return key?.GetValue(ProgId) != null;
    }

    // ──────────────────────────────────────────────
    // InstallAssociationForExtension
    // ──────────────────────────────────────────────

    [Fact]
    public void Install_SingleExtension_WritesOpenWithProgids()
    {
        try
        {
            Assert.False(HasProgId(".zip"), "Precondition: .zip should not have MantisZip.ProgId before install");
            ShellIntegration.InstallAssociationForExtension(".zip");
            Assert.True(ShellIntegration.AreAssociationsInstalledForExtension(".zip"));
        }
        finally
        {
            CleanupExtension(".zip");
        }
    }

    [Fact]
    public void Install_SkipTarGz_DoesNotWrite()
    {
        try
        {
            ShellIntegration.InstallAssociationForExtension(".tar.gz");
            Assert.False(ShellIntegration.AreAssociationsInstalledForExtension(".tar.gz"));
        }
        finally
        {
            CleanupExtension(".tar.gz");
        }
    }

    [Fact]
    public void Install_MultipleExtensions_AllWritten()
    {
        try
        {
            ShellIntegration.InstallAssociationForExtension(".zip");
            ShellIntegration.InstallAssociationForExtension(".7z");
            Assert.True(ShellIntegration.AreAssociationsInstalledForExtension(".zip"));
            Assert.True(ShellIntegration.AreAssociationsInstalledForExtension(".7z"));
        }
        finally
        {
            CleanupExtension(".zip");
            CleanupExtension(".7z");
        }
    }

    // ──────────────────────────────────────────────
    // UninstallAssociationForExtension
    // ──────────────────────────────────────────────

    [Fact]
    public void Uninstall_SingleExtension_RemovesOpenWithProgids()
    {
        try
        {
            ShellIntegration.InstallAssociationForExtension(".zip");
            Assert.True(HasProgId(".zip"), "Should be installed before uninstall");

            ShellIntegration.UninstallAssociationForExtension(".zip");
            Assert.False(ShellIntegration.AreAssociationsInstalledForExtension(".zip"));
        }
        finally
        {
            CleanupExtension(".zip");
        }
    }

    [Fact]
    public void Uninstall_SkipTarGz_DoesNothing()
    {
        // Should not throw and should not have created entries
        ShellIntegration.UninstallAssociationForExtension(".tar.gz");
        Assert.False(ShellIntegration.AreAssociationsInstalledForExtension(".tar.gz"));
    }

    // ──────────────────────────────────────────────
    // AreAssociationsInstalledForExtension
    // ──────────────────────────────────────────────

    [Fact]
    public void IsAssociated_WhenInstalled_ReturnsTrue()
    {
        try
        {
            ShellIntegration.InstallAssociationForExtension(".zip");
            Assert.True(ShellIntegration.AreAssociationsInstalledForExtension(".zip"));
        }
        finally
        {
            CleanupExtension(".zip");
        }
    }

    [Fact]
    public void IsAssociated_WhenUninstalled_ReturnsFalse()
    {
        // Ensure clean state — a non-standard extension unlikely to have associations
        CleanupExtension(".xyz999");
        Assert.False(ShellIntegration.AreAssociationsInstalledForExtension(".xyz999"));
    }

    // ──────────────────────────────────────────────
    // GetInstalledExtensionCount
    // ──────────────────────────────────────────────

    [Fact]
    public void InstalledCount_AfterInstall_Increases()
    {
        try
        {
            CleanupExtension(".iso"); // ensure baseline
            int before = ShellIntegration.GetInstalledExtensionCount();

            ShellIntegration.InstallAssociationForExtension(".iso");
            int after = ShellIntegration.GetInstalledExtensionCount();

            Assert.Equal(before + 1, after);
        }
        finally
        {
            CleanupExtension(".iso");
        }
    }

    // ──────────────────────────────────────────────
    // GetCurrentHandler
    // ──────────────────────────────────────────────

    [Fact]
    public void GetCurrentHandler_ReturnsString()
    {
        var handler = ShellIntegration.GetCurrentHandler(".zip");
        Assert.NotNull(handler);
    }

    [Fact]
    public void GetCurrentHandler_ForUnknownExtension_ReturnsNonEmpty()
    {
        var handler = ShellIntegration.GetCurrentHandler(".nonexistent999");
        Assert.NotNull(handler);
        Assert.NotEqual("", handler);
    }

    // ──────────────────────────────────────────────
    // Custom Extension Validation
    //
    // 复制自 SettingsWindow.AddCustomBtn_Click 的验证逻辑：
    //   - Normalize: Trim + ToLowerInvariant + prepend "."
    //   - Validate: Length > 10 || Length < 2 || Contains(' ')
    //               || Count('.') > 1
    //               || Any(!char.IsLetterOrDigit && != '.')
    // ──────────────────────────────────────────────

    private static string NormalizeExt(string input)
    {
        var ext = input?.Trim().ToLowerInvariant() ?? "";
        if (!ext.StartsWith(".")) ext = "." + ext;
        return ext;
    }

    private static bool IsValidExt(string ext)
    {
        return ext.Length <= 10
            && ext.Length >= 2
            && ext.StartsWith(".")
            && !ext.Contains(' ')
            && ext.Count(c => c == '.') == 1
            && ext.Skip(1).All(c => char.IsLetterOrDigit(c));
    }

    [Fact]
    public void NormalizeCustomExtension_AddsDot()
    {
        Assert.Equal(".zipx", NormalizeExt("zipx"));
    }

    [Fact]
    public void NormalizeCustomExtension_Lowercases()
    {
        Assert.Equal(".zipx", NormalizeExt(".ZIPX"));
    }

    [Fact]
    public void NormalizeCustomExtension_TrimsWhitespace()
    {
        Assert.Equal(".zipx", NormalizeExt("  ZipX  "));
    }

    [Fact]
    public void CustomExtension_Validation_ValidNormalLength()
    {
        Assert.True(IsValidExt(".zipx"));
    }

    [Fact]
    public void CustomExtension_Validation_ValidSingleChar()
    {
        // Length 2: "." + single letter = valid
        Assert.True(IsValidExt(".a"));
    }

    [Fact]
    public void CustomExtension_Validation_InvalidTooLong()
    {
        // 11 chars after normalization (10 char limit)
        Assert.False(IsValidExt(".abcdefghijk"));
    }

    [Fact]
    public void CustomExtension_Validation_InvalidMissingDot()
    {
        Assert.False(IsValidExt("zipx"));
    }

    [Fact]
    public void CustomExtension_Validation_InvalidContainsSpace()
    {
        Assert.False(IsValidExt(".zip x"));
    }

    [Fact]
    public void CustomExtension_Validation_InvalidMultipleDots()
    {
        Assert.False(IsValidExt(".my.ext"));
    }

    [Fact]
    public void CustomExtension_Validation_InvalidSpecialChars()
    {
        Assert.False(IsValidExt(".zip!"));
        Assert.False(IsValidExt(".z@p"));
    }

    [Fact]
    public void CustomExtension_Validation_InvalidEmpty()
    {
        // Empty string → normalized to "."
        Assert.False(IsValidExt(""));
    }

    [Fact]
    public void CustomExtension_Validation_Roundtrip()
    {
        // Full roundtrip: user input → normalize → validate
        var input = "  MyExt  ";
        var normalized = NormalizeExt(input);
        Assert.Equal(".myext", normalized);
        Assert.True(IsValidExt(normalized));
    }
}
