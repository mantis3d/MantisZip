using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
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
public class ShellIntegrationAssocTests : IDisposable
{
    /// <summary>
    /// 测试装配：将 RegistryHost 切换到独立沙箱（HKCU\Software\MantisZip\AssocTestSandbox），
    /// 使本类所有注册表读写只落在沙箱内，不再污染真实的文件关联 / 右键菜单注册。
    /// ⚠ 以下四步顺序不可颠倒。
    /// </summary>
    public ShellIntegrationAssocTests()
    {
        RegistryHost.Reset();                                   // ① 先恢复生产映射（确定性起点）
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(  // ② 原语幂等清残留（不经 RegistryHost）
            @"Software\MantisZip\AssocTestSandbox", throwOnMissingSubKey: false);
        RegistryHost.UseSandbox(@"MantisZip\AssocTestSandbox"); // ③ 挂沙箱
        RegistryHost.DeleteInterceptionForTesting = null;       // ④ 生产语义
    }

    /// <summary>
    /// 测试拆卸：先复位 RegistryHost 映射，再用注册表原语删除沙箱键。
    /// 复位后 RegistryHost.Delete 会落到生产路径，故此处必须用原语删除。
    /// </summary>
    public void Dispose()
    {
        RegistryHost.Reset();                                   // 先复位，避免任何后续调用被重定向
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(  // 原语删沙箱（此时 RegistryHost.Delete 会落到生产路径，故必须用原语）
            @"Software\MantisZip\AssocTestSandbox", throwOnMissingSubKey: false);
    }

    private static void CleanupExtension(string ext)
    {
        ShellIntegration.UninstallAssociationForExtension(ext);
    }

    // ──────────────────────────────────────────────
    // InstallAssociationForExtension
    // ──────────────────────────────────────────────

    [Fact]
    public void Install_SingleExtension_WritesOpenWithProgids()
    {
        try
        {
            Assert.False(ShellIntegration.AreAssociationsInstalledForExtension(".zip"), "Precondition: .zip should not have MantisZip association before install");
            ShellIntegration.InstallAssociationForExtension(".zip");
            Assert.True(ShellIntegration.AreAssociationsInstalledForExtension(".zip"));
        }
        finally
        {
            CleanupExtension(".zip");
        }
    }

    [Fact]
    public void Install_TarGz_WritesAssociation()
    {
        try
        {
            ShellIntegration.InstallAssociationForExtension(".tar.gz");
            Assert.True(ShellIntegration.AreAssociationsInstalledForExtension(".tar.gz"));
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
            Assert.True(ShellIntegration.AreAssociationsInstalledForExtension(".zip"), "Should be installed before uninstall");

            ShellIntegration.UninstallAssociationForExtension(".zip");
            Assert.False(ShellIntegration.AreAssociationsInstalledForExtension(".zip"));
        }
        finally
        {
            CleanupExtension(".zip");
        }
    }

    [Fact]
    public void Uninstall_TarGz_RemovesAssociation()
    {
        try
        {
            ShellIntegration.InstallAssociationForExtension(".tar.gz");
            Assert.True(ShellIntegration.AreAssociationsInstalledForExtension(".tar.gz"));
            ShellIntegration.UninstallAssociationForExtension(".tar.gz");
            Assert.False(ShellIntegration.AreAssociationsInstalledForExtension(".tar.gz"));
        }
        finally
        {
            CleanupExtension(".tar.gz");
        }
    }

    [Fact]
    public void Uninstall_ClearsDanglingUserChoice_WhenPointsToUs()
    {
        try
        {
            ShellIntegration.InstallAssociationForExtension(".zip");
            // 模拟用户"打开方式→始终"写入的默认（沙箱内）
            using (var uc = RegistryHost.Create(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice"))
                uc.SetValue("Progid", "MantisZip.Zip");

            ShellIntegration.UninstallAssociationForExtension(".zip");

            using var check = RegistryHost.Open(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice");
            Assert.Null(check);   // 指向我们的 UserChoice 应被删除
        }
        finally { CleanupExtension(".zip"); }
    }

    [Fact]
    public void Uninstall_PreservesUserChoice_WhenPointsToOtherApp()
    {
        try
        {
            ShellIntegration.InstallAssociationForExtension(".zip");
            using (var uc = RegistryHost.Create(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice"))
                uc.SetValue("Progid", "CompressedFolder");

            ShellIntegration.UninstallAssociationForExtension(".zip");

            using var check = RegistryHost.Open(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice");
            Assert.NotNull(check);                     // 别家的默认不得动
            Assert.Equal("CompressedFolder", check!.GetValue("Progid"));
        }
        finally { CleanupExtension(".zip"); }
    }

    [Fact]
    public void Uninstall_DoesNotThrow_WhenUserChoiceDeleteThrows()
    {
        try
        {
            ShellIntegration.InstallAssociationForExtension(".zip");
            using (var uc = RegistryHost.Create(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice"))
                uc.SetValue("Progid", "MantisZip.Zip");

            // 注入"删除被拒绝"：只对 UserChoice 路径抛异常（模拟 UCPD / 权限拒绝）
            RegistryHost.DeleteInterceptionForTesting = path =>
            {
                if (path.IndexOf("UserChoice", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new UnauthorizedAccessException("simulated UCPD denial");
            };

            var thrown = Record.Exception(() => ShellIntegration.UninstallAssociationForExtension(".zip"));
            Assert.Null(thrown);   // best-effort：整体不抛异常；异常路径写入 App.DebugLog（不中断卸载）

            // UserChoice 仍在（删除失败属预期，不视为阻塞；由 Task E 自愈兜底）
            using (var uc = RegistryHost.Open(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice"))
                Assert.Equal("MantisZip.Zip", uc?.GetValue("Progid"));

            // 其余卸载步骤仍然完成：ProgId 键已删、OpenWithProgids 里的值已删
            using (var progId = RegistryHost.Open(@"Software\Classes\MantisZip.Zip"))
                Assert.Null(progId);
            using (var owp = RegistryHost.Open(@"Software\Classes\.zip\OpenWithProgids"))
                Assert.Null(owp?.GetValue("MantisZip.Zip"));
        }
        finally
        {
            RegistryHost.DeleteInterceptionForTesting = null;   // 恢复注入缝，防跨用例串扰
            CleanupExtension(".zip");
        }
    }

    [Fact]
    public void Uninstall_RemovesDefaultIcon()
    {
        try
        {
            ShellIntegration.InstallAssociationForExtension(".zip");
            using (var k = RegistryHost.Open(@"Software\Classes\.zip\DefaultIcon"))
                Assert.NotNull(k?.GetValue(null));     // 安装后应存在

            ShellIntegration.UninstallAssociationForExtension(".zip");

            using var after = RegistryHost.Open(@"Software\Classes\.zip\DefaultIcon");
            Assert.Null(after?.GetValue(null));        // 卸载后应清掉
        }
        finally { CleanupExtension(".zip"); }
    }

    [Fact]
    public void Uninstall_RemovesOrphanedDefaultIcon_PointingToTestsOutputDir()
    {
        try
        {
            // 构造现场症状：DefaultIcon 指向测试输出目录的 ico（与现值/快照均不相等）
            using (var k = RegistryHost.Create(@"Software\Classes\.zip\DefaultIcon"))
                k.SetValue(null, @"D:\Github\MantisZip\tests\MantisZip.Tests\bin\Debug\net10.0-windows10.0.17763.0\Resources\Icons\zip.ico");

            ShellIntegration.UninstallAssociationForExtension(".zip");

            using var after = RegistryHost.Open(@"Software\Classes\.zip\DefaultIcon");
            Assert.Null(after?.GetValue(null));   // 残留值必须被清掉（这是用户实际症状）
        }
        finally { CleanupExtension(".zip"); }
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

    // ──────────────────────────────────────────────
    // SelfHealAssociations（启动自愈：重建缺失 ProgId）
    // 触发判据为三条 AND：① UserChoice.Progid 以 MantisZip 开头
    // ② Software\Classes\{ProgId} 键缺失 ③ 该格式未被用户显式禁用
    // ──────────────────────────────────────────────

    [Fact]
    public void SelfHeal_RebuildsProgId_WhenUserChoiceDangles()
    {
        try
        {
            // 模拟损坏现场：UserChoice 指向我们，但 ProgId 缺失
            using (var uc = RegistryHost.Create(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice"))
                uc.SetValue("Progid", "MantisZip.Zip");
            using (var chk = RegistryHost.Open(@"Software\Classes\MantisZip.Zip"))
                Assert.Null(chk);   // 前置：ProgId 不存在

            ShellIntegration.SelfHealAssociations(new AppSettings());

            using var after = RegistryHost.Open(@"Software\Classes\MantisZip.Zip\shell\open\command");
            Assert.NotNull(after?.GetValue(null));   // ProgId 被重建，UserChoice 恢复可解析
        }
        finally { CleanupExtension(".zip"); }
    }

    [Fact]
    public void SelfHeal_DoesNotHeal_WhenNoRegistryTrace()
    {
        // 无注册表痕迹 → 不自愈
        var s = new AppSettings { AssocZip = false };
        ShellIntegration.SelfHealAssociations(s);
        using var chk = RegistryHost.Open(@"Software\Classes\MantisZip.Zip");
        Assert.Null(chk);
    }

    [Fact]
    public void SelfHeal_DoesNotHeal_WhenExplicitlyDisabledAndProgIdMissing()
    {
        // 显式禁用 + ProgId 缺失 → 不自愈
        try
        {
            using (var uc = RegistryHost.Create(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice"))
                uc.SetValue("Progid", "MantisZip.Zip");
            var s = new AppSettings { AssocZip = false };
            ShellIntegration.SelfHealAssociations(s);
            using var chk = RegistryHost.Open(@"Software\Classes\MantisZip.Zip");
            Assert.Null(chk);
        }
        finally { CleanupExtension(".zip"); }
    }

    [Fact]
    public void SelfHeal_Rebuilds_TwoFormats()
    {
        try
        {
            // 模拟损坏现场：.zip 与 .7z 的 UserChoice 都指向我们，但两个 ProgId 键都不存在
            using (var ucZip = RegistryHost.Create(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice"))
                ucZip.SetValue("Progid", "MantisZip.Zip");
            using (var uc7z = RegistryHost.Create(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.7z\UserChoice"))
                uc7z.SetValue("Progid", "MantisZip.7z");
            using (var chkZip = RegistryHost.Open(@"Software\Classes\MantisZip.Zip"))
                Assert.Null(chkZip);
            using (var chk7z = RegistryHost.Open(@"Software\Classes\MantisZip.7z"))
                Assert.Null(chk7z);

            ShellIntegration.SelfHealAssociations(new AppSettings());   // 默认各格式均启用

            // 硬约束：全格式统一——两个格式都必须被重建，不得只救 .zip
            using var afterZip = RegistryHost.Open(@"Software\Classes\MantisZip.Zip\shell\open\command");
            Assert.NotNull(afterZip?.GetValue(null));
            using var after7z = RegistryHost.Open(@"Software\Classes\MantisZip.7z\shell\open\command");
            Assert.NotNull(after7z?.GetValue(null));
        }
        finally
        {
            CleanupExtension(".zip");
            CleanupExtension(".7z");
        }
    }

    [Fact]
    public void IsAssocEnabledInSettings_CoversSupportedExtensions()
    {
        // 防未来新增格式导致自愈被静默关闭：ArchiveEngineFactory.SupportedExtensions 中每个扩展名
        // 都必须命中显式分支，不得落进 `_ => false` 默认分支。
        var s = new AppSettings
        {
            AssocZip = true,
            Assoc7z = true,
            AssocRar = true,
            AssocTar = true,
            AssocTarGz = true,
            AssocGz = true,
            AssocIso = true,
        };
        foreach (var ext in ArchiveEngineFactory.SupportedExtensions)   // [".zip",".7z",".rar",".tar",".tgz",".tar.gz",".gz",".iso"]
        {
            Assert.True(ShellIntegration.IsAssocEnabledInSettings(s, ext),
                $"{ext} 落入了 `_ => false` 默认分支：未来新增格式会让自愈被静默关闭");
        }

        // 反向哨兵：防上一条断言因实现恒真而假通过
        Assert.False(ShellIntegration.IsAssocEnabledInSettings(s, ".not-a-real-ext"));
        Assert.False(ShellIntegration.IsAssocEnabledInSettings(new AppSettings { AssocTarGz = false }, ".tgz"));
    }
}
