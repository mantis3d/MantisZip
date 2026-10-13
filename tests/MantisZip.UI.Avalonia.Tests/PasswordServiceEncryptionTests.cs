using System;
using System.IO;
using System.IO.Compression;
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
}
