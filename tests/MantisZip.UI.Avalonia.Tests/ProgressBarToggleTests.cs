using System.IO.Compression;
using System.Text;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 「视图 → 进度条」开关回归测试（2026-10-22）。
///
/// 现象：开关按下后界面不刷新（进度条不出现/不消失）。
/// 根因：Avalonia 版的 <c>ToggleProgressBars</c> 只翻转了 <c>ShowProgressBars</c> 并写入
/// <c>ProgressBarEnabled</c>（该属性**无人绑定**），既不重算比例也不清零比例；
/// 而比例值（<c>SizeRatio</c> / <c>CompressedSizeRatio</c> / <c>DateRatio</c> / <c>RatioBarValue</c>）
/// 才是进度条 Rectangle 的宽度来源 → 切换后显示不变。
///
/// 参考语义（WPF 版 <c>ToggleProgressBars_Click</c>）：切换后重新填充当前目录
/// （<c>FilterFiles(_currentFolder)</c>）→ 开启时重算比例、关闭时比例归零。
/// 本实现保留当前条目与选中项，只重算/清零比例。
/// </summary>
public class ProgressBarToggleTests
{
    /// <summary>
    /// 关闭开关 → 各比例归零（进度条隐藏）；再次开启 → 比例重算（进度条显示）。
    /// </summary>
    [AvaloniaFact]
    public async Task ToggleProgressBars_ClearsRatios_WhenOff_AndRecomputes_WhenOn()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"mantiszip_progbar_test_{Guid.NewGuid():N}.zip");
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var big = zip.CreateEntry("big.bin");
                using (var w = new StreamWriter(big.Open()))
                    w.Write(new string('x', 4096));
                var small = zip.CreateEntry("small.bin");
                using var w2 = new StreamWriter(small.Open());
                w2.Write("y");
            }

            var vm = new MainWindowViewModel();
            vm.ShowProgressBars = true;          // 明确初始态（不受本机设置影响）
            await vm.LoadArchiveAsync(zipPath);

            Assert.NotEmpty(vm.CurrentEntries);

            // 开启态：比例已计算 → 进度条有宽度
            Assert.Contains(vm.CurrentEntries, e => e.SizeRatio > 0);
            Assert.All(vm.CurrentEntries, e => Assert.True(e.ProgressBarEnabled));

            // 关闭 → 比例归零（进度条隐藏）+ ProgressBarEnabled 关闭
            vm.ToggleProgressBarsCommand.Execute(null);
            Assert.False(vm.ShowProgressBars);
            Assert.All(vm.CurrentEntries, e => Assert.Equal(0d, e.SizeRatio));
            Assert.All(vm.CurrentEntries, e => Assert.Equal(0d, e.CompressedSizeRatio));
            Assert.All(vm.CurrentEntries, e => Assert.Equal(0d, e.DateRatio));
            Assert.All(vm.CurrentEntries, e => Assert.Equal(0d, e.RatioBarValue));
            Assert.All(vm.CurrentEntries, e => Assert.False(e.ProgressBarEnabled));

            // 再开启 → 比例重算（进度条恢复显示）
            vm.ToggleProgressBarsCommand.Execute(null);
            Assert.True(vm.ShowProgressBars);
            Assert.Contains(vm.CurrentEntries, e => e.SizeRatio > 0);
            Assert.All(vm.CurrentEntries, e => Assert.True(e.ProgressBarEnabled));
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }
}
