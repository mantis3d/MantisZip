using System.IO.Compression;
using System.Text;
using Avalonia.Headless.XUnit;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using SharpCompress.Common;
using SharpCompress.Writers.Zip;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 主工具栏「文件名代码页」按钮（图标 + 文字 + 图标徽章 + 弹出菜单）的回归测试。
/// 覆盖：徽章短标记表、未打开压缩包时置灰但不显示徽章、打开 GBK 命名的包后徽章/Tooltip 回显、
/// 切换编码后按钮文字即时更新、「其他代码页…」在菜单里归到同一项。
/// </summary>
public sealed class NameEncodingToolbarTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("auto", "")]
    [InlineData("system", "ANSI")]
    [InlineData("utf-8", "UTF8")]
    [InlineData("gbk", "GBK")]
    [InlineData("gb18030", "GB18")]
    [InlineData("big5", "BIG5")]
    [InlineData("shift-jis", "JIS")]
    [InlineData("euc-kr", "KR")]
    [InlineData("other:950", "950")]
    public void ShortLabel_MapsKeyToCompactBadgeText(string? key, string expected)
    {
        Assert.Equal(expected, NameEncodingOption.ShortLabel(key));
    }

    [AvaloniaFact]
    public void WithoutArchive_ButtonDisabledAndNoBadge()
    {
        var vm = new MainWindowViewModel();

        // 需求：7z/RAR/未打开压缩包时不隐藏而是置灰，且没有「检测到」的徽章
        Assert.False(vm.CanChangeNameEncoding);
        Assert.False(vm.HasNameEncodingBadge);
        Assert.Equal(string.Empty, vm.NameEncodingBadgeText);
    }

    [AvaloniaFact]
    public async Task OpeningGbkNamedArchive_ShowsBadgeWithDetectedEncoding()
    {
        var zipPath = CreateZip(Encoding.GetEncoding(936), "中文文件.txt", "测试二.txt");
        try
        {
            var vm = new MainWindowViewModel();
            await vm.LoadArchiveAsync(zipPath);

            Assert.True(vm.CanChangeNameEncoding);
            Assert.Equal("gbk", vm.ResolvedNameEncodingKey);
            Assert.Equal("GBK", vm.NameEncodingBadgeText);
            Assert.True(vm.HasNameEncodingBadge);
            // Tooltip = 名称 + 「检测到 …」
            Assert.Contains("GBK", vm.NameEncodingToolTip, StringComparison.Ordinal);
        }
        finally
        {
            DeleteQuietly(zipPath);
        }
    }

    [AvaloniaFact]
    public async Task SwitchingEncoding_UpdatesButtonTextAndBadge()
    {
        var zipPath = CreateZip(Encoding.GetEncoding(950), "中文檔案.txt");
        try
        {
            var vm = new MainWindowViewModel();
            await vm.LoadArchiveAsync(zipPath);

            // 自动检测对 Big5 必然失败（回退 GBK），徽章此时是 GBK
            Assert.Equal("GBK", vm.NameEncodingBadgeText);
            Assert.Equal(LocalizationManager.T("Toolbar_NameEncoding_Auto"), vm.NameEncodingDisplay);

            await vm.ChangeNameEncodingCommand.ExecuteAsync("big5");

            // 手动选 Big5 之后：按钮文字变成 Big5、徽章变成 BIG5、条目名正确
            Assert.Equal("big5", vm.ArchiveNameEncoding);
            Assert.Equal("Big5", vm.NameEncodingDisplay);
            Assert.Equal("BIG5", vm.NameEncodingBadgeText);
            Assert.Contains(vm.Entries, e => e.Name == "中文檔案.txt");
        }
        finally
        {
            DeleteQuietly(zipPath);
        }
    }

    [AvaloniaFact]
    public void OtherCodePage_MapsToOtherMenuItem()
    {
        var vm = new MainWindowViewModel { ArchiveNameEncoding = "other:950" };

        // 菜单只有「其他代码页…」这一项，other:950 必须归到它上面打勾
        Assert.Equal(NameEncodingOption.OtherPlaceholder, vm.NameEncodingSelectedOption?.Key);
        Assert.Equal("950", NameEncodingOption.ShortLabel(vm.ArchiveNameEncoding));
    }

    /// <summary>
    /// 回归测试（2026-11-02 实测「不论打开什么文件按钮都是灰的」）：
    /// 载入成功分支里通知 <c>CanChangeNameEncoding</c> 时 <c>IsArchiveLoaded</c> 还是 false，
    /// 只靠那一处通知，按钮会永久停在置灰态 —— 必须等 <c>IsArchiveLoaded</c> 变 true 后再通知一次。
    /// </summary>
    [AvaloniaFact]
    public async Task LoadingArchive_NotifiesCanChangeAfterIsArchiveLoaded()
    {
        var zipPath = CreateZip(Encoding.UTF8, "plain.txt");
        try
        {
            var vm = new MainWindowViewModel();
            var notifiedWhileLoaded = false;

            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.CanChangeNameEncoding) && vm.IsArchiveLoaded)
                    notifiedWhileLoaded = true;
            };

            await vm.LoadArchiveAsync(zipPath);

            Assert.True(notifiedWhileLoaded, "CanChangeNameEncoding 必须在 IsArchiveLoaded 置 true 之后通知一次");
            Assert.True(vm.CanChangeNameEncoding);
        }
        finally
        {
            DeleteQuietly(zipPath);
        }
    }

    /// <summary>造一个条目名按指定代码页写入、且不置 UTF-8 标志位的 zip（老压缩包形态）。</summary>
    private static string CreateZip(Encoding encoding, params string[] entryNames)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mantiszip_nameenc_ui_{Guid.NewGuid():N}.zip");
        using var stream = File.Create(path);
        using var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)
        {
            ArchiveEncoding = new ArchiveEncoding { Default = encoding },
            LeaveStreamOpen = false,
        });

        foreach (var name in entryNames)
        {
            using var payload = new MemoryStream(Encoding.UTF8.GetBytes("content-of " + name));
            writer.Write(name, payload, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        return path;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 测试清理失败不影响断言结果
        }
    }
}
