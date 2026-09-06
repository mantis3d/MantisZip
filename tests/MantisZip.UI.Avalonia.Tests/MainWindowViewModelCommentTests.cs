using System.IO.Compression;
using System.Text;
using Avalonia.Headless.XUnit;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

public class MainWindowViewModelCommentTests
{
    /// <summary>
    /// 回归测试：打开带注释的压缩包后（未选中任何文件），预览面板应显示压缩包注释。
    /// </summary>
    [AvaloniaFact]
    public async Task LoadArchive_WithComment_ShowsCommentPreview()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"mantiszip_comment_test_{Guid.NewGuid():N}.zip");
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("hello.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("hello");
            }
            ZipCommentHelper.WriteComment(zipPath, "归档注释 archive comment");

            var vm = new MainWindowViewModel();
            await vm.LoadArchiveAsync(zipPath);

            Assert.True(vm.IsArchiveLoaded);
            Assert.Equal(PreviewType.Text, vm.Preview.PreviewType);
            Assert.Contains("归档注释", vm.Preview.TextContent);
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }

    /// <summary>
    /// 回归测试：GBK 编码的 ZIP 注释不应乱码（系统 ANSI 回退解码）。
    /// 在真实 ZIP 上重建 EOCD 注释为 GBK 字节，模拟中文 Windows 常见工具生成的 ZIP。
    /// </summary>
    [AvaloniaFact]
    public async Task LoadArchive_WithGbkComment_DecodesCorrectly()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"mantiszip_gbk_comment_test_{Guid.NewGuid():N}.zip");
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                zip.CreateEntry("hello.txt");
            }

            // 重建 EOCD：注释替换为 GBK 字节（长度随编码变化）
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var bytes = File.ReadAllBytes(zipPath);
            int eocd = -1;
            for (int i = bytes.Length - 22; i >= Math.Max(0, bytes.Length - 22 - 65535); i--)
            {
                if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x05 && bytes[i + 3] == 0x06)
                {
                    eocd = i;
                    break;
                }
            }
            Assert.True(eocd >= 0, "EOCD 签名未找到");

            var gbkComment = System.Text.Encoding.GetEncoding(936).GetBytes("欢迎使用本压缩包");
            var newEocd = new byte[22 + gbkComment.Length];
            Array.Copy(bytes, eocd, newEocd, 0, 22);
            newEocd[20] = (byte)(gbkComment.Length & 0xFF);
            newEocd[21] = (byte)((gbkComment.Length >> 8) & 0xFF);
            Array.Copy(gbkComment, 0, newEocd, 22, gbkComment.Length);

            var head = new byte[eocd];
            Array.Copy(bytes, head, eocd);
            File.WriteAllBytes(zipPath, head.Concat(newEocd).ToArray());

            var vm = new MainWindowViewModel();
            await vm.LoadArchiveAsync(zipPath);

            Assert.True(vm.IsArchiveLoaded);
            Assert.Equal(PreviewType.Text, vm.Preview.PreviewType);
            Assert.Contains("欢迎使用本压缩包", vm.Preview.TextContent);
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }
}
