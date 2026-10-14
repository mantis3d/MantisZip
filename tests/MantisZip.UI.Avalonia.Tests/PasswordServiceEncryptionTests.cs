using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 回归：非加密包不得被判定为「含加密条目」——否则 CLI/右键解压会把密码库里任意匹配的密码
/// 误判为「密码匹配成功」，进而在批处理行点亮只应在加密包出现的 🔑 徽标（方案 A）。
/// </summary>
public class PasswordServiceEncryptionTests
{
    [Fact]
    public void HasEncryptedEntries_NonEncryptedZip_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), "pwdtest-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var e = z.CreateEntry("a.txt");
                using var s = e.Open();
                s.Write(new byte[] { 1, 2, 3 }, 0, 3);
            }

            var svc = new PasswordService();
            Assert.False(svc.HasEncryptedEntries(path, new ZipEngine()));
        }
        finally
        {
            try { File.Delete(path); } catch { /* 临时文件尽力清理 */ }
        }
    }

    /// <summary>
    /// 7z 非加密包不得被判定为「含加密条目」。
    /// 同时覆盖 HasEncryptedEntries 对 <c>SharpSevenZipExtractor</c> 无密码构造分支
    /// （<c>string.IsNullOrEmpty(password)</c> 三元）的 null 传入路径（CS8604 修复回归）。
    /// </summary>
    [Fact]
    public async Task HasEncryptedEntries_NonEncryptedSevenZip_ReturnsFalse()
    {
        // 7z.dll 不可用时跳过（与 SevenZipEngineTests 相同的环境守卫）
        if (!File.Exists(SevenZipEngine.SevenZipDllPath)) return;

        var dir = Path.Combine(Path.GetTempPath(), "pwdtest7z-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "a.7z");
        try
        {
            Directory.CreateDirectory(dir);
            var src = Path.Combine(dir, "src.txt");
            File.WriteAllText(src, "hello");
            await new SevenZipEngine().CompressAsync([src], path,
                new ArchiveOptions { PreserveDirectoryRoot = false },
                cancellationToken: TestContext.Current.CancellationToken);

            var svc = new PasswordService();
            Assert.False(svc.HasEncryptedEntries(path, new SevenZipEngine()));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 临时目录尽力清理 */ }
        }
    }
}
