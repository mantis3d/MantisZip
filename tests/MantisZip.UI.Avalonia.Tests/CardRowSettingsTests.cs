using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 卡片信息行显示状态（thumbnail-card-badge-and-row-bars 计划 T2）：
/// ① 8 个 AppSettings bool 的默认值（访谈决策 7/8：Tile 默认 大小+日期，Content 全开）；
/// ② <c>MainWindowViewModel.GetCardRowValue / ApplyCardRow</c>（纯静态映射）的全组合对应关系；
/// ③ 切换命令翻转 + 二次执行还原（SaveSetting 落盘净零，不污染本机 settings.json）；
/// ④ <c>App.RefreshThumbnailRowResources</c> 无 App 环境静默返回（单测兜底）。
/// </summary>
public class CardRowSettingsTests
{
    // ── ① 默认值 ──────────────────────────────────────────────

    [Fact]
    public void TileRows_Default_SizeAndDateOn_CompressedAndRatioOff()
    {
        var s = new AppSettings();

        Assert.True(s.TileRowShowSize);
        Assert.False(s.TileRowShowCompressedSize);
        Assert.False(s.TileRowShowRatio);
        Assert.True(s.TileRowShowDate);
    }

    [Fact]
    public void ContentRows_Default_AllOn()
    {
        var s = new AppSettings();

        Assert.True(s.ContentRowShowSize);
        Assert.True(s.ContentRowShowCompressedSize);
        Assert.True(s.ContentRowShowRatio);
        Assert.True(s.ContentRowShowDate);
    }

    // ── ② 映射全组合 ──────────────────────────────────────────

    [Fact]
    public void ApplyCardRow_TileMode_WritesOnlyTileFields()
    {
        var s = new AppSettings();

        MainWindowViewModel.ApplyCardRow(s, isTile: true, CardRowKind.Size, false);
        MainWindowViewModel.ApplyCardRow(s, isTile: true, CardRowKind.CompressedSize, true);
        MainWindowViewModel.ApplyCardRow(s, isTile: true, CardRowKind.Ratio, true);
        MainWindowViewModel.ApplyCardRow(s, isTile: true, CardRowKind.Date, false);

        Assert.False(s.TileRowShowSize);
        Assert.True(s.TileRowShowCompressedSize);
        Assert.True(s.TileRowShowRatio);
        Assert.False(s.TileRowShowDate);
        // Content 四字段不得被 Tile 模式触碰
        Assert.True(s.ContentRowShowSize);
        Assert.True(s.ContentRowShowCompressedSize);
        Assert.True(s.ContentRowShowRatio);
        Assert.True(s.ContentRowShowDate);
    }

    [Fact]
    public void ApplyCardRow_ContentMode_WritesOnlyContentFields()
    {
        var s = new AppSettings();

        MainWindowViewModel.ApplyCardRow(s, isTile: false, CardRowKind.Size, false);
        MainWindowViewModel.ApplyCardRow(s, isTile: false, CardRowKind.CompressedSize, false);
        MainWindowViewModel.ApplyCardRow(s, isTile: false, CardRowKind.Ratio, false);
        MainWindowViewModel.ApplyCardRow(s, isTile: false, CardRowKind.Date, false);

        Assert.False(s.ContentRowShowSize);
        Assert.False(s.ContentRowShowCompressedSize);
        Assert.False(s.ContentRowShowRatio);
        Assert.False(s.ContentRowShowDate);
        // Tile 四字段不得被 Content 模式触碰（保持默认）
        Assert.True(s.TileRowShowSize);
        Assert.False(s.TileRowShowCompressedSize);
        Assert.False(s.TileRowShowRatio);
        Assert.True(s.TileRowShowDate);
    }

    [Fact]
    public void GetCardRowValue_ReadsMappedField()
    {
        var s = new AppSettings
        {
            TileRowShowSize = false,
            TileRowShowCompressedSize = true,
            TileRowShowRatio = true,
            TileRowShowDate = false,
            ContentRowShowSize = false,
            ContentRowShowCompressedSize = false,
            ContentRowShowRatio = false,
            ContentRowShowDate = false,
        };

        Assert.False(MainWindowViewModel.GetCardRowValue(s, true, CardRowKind.Size));
        Assert.True(MainWindowViewModel.GetCardRowValue(s, true, CardRowKind.CompressedSize));
        Assert.True(MainWindowViewModel.GetCardRowValue(s, true, CardRowKind.Ratio));
        Assert.False(MainWindowViewModel.GetCardRowValue(s, true, CardRowKind.Date));
        Assert.False(MainWindowViewModel.GetCardRowValue(s, false, CardRowKind.Size));
        Assert.False(MainWindowViewModel.GetCardRowValue(s, false, CardRowKind.CompressedSize));
        Assert.False(MainWindowViewModel.GetCardRowValue(s, false, CardRowKind.Ratio));
        Assert.False(MainWindowViewModel.GetCardRowValue(s, false, CardRowKind.Date));
    }

    // ── ③ 命令翻转 + 二次还原（落盘净零） ─────────────────────

    [Fact]
    public void ToggleCommands_FlipMappedField_AndRestoreOnSecondCall()
    {
        AppSettings.ResetCacheForTests();
        var snapshot = new
        {
            AppSettings.Load().TileRowShowSize,
            AppSettings.Load().TileRowShowCompressedSize,
            AppSettings.Load().TileRowShowRatio,
            AppSettings.Load().TileRowShowDate,
            AppSettings.Load().ContentRowShowSize,
            AppSettings.Load().ContentRowShowCompressedSize,
            AppSettings.Load().ContentRowShowRatio,
            AppSettings.Load().ContentRowShowDate,
        };

        try
        {
            var vm = new MainWindowViewModel();

            // Tile 模式四个 kind 逐一翻转 → 对应字段取反（按默认映射），Content 不变
            vm.ToggleTileRowCommand.Execute(CardRowKind.Size);
            Assert.NotEqual(snapshot.TileRowShowSize, AppSettings.Load().TileRowShowSize);
            vm.ToggleTileRowCommand.Execute(CardRowKind.Size);   // 二次执行还原（落盘净零）
            Assert.Equal(snapshot.TileRowShowSize, AppSettings.Load().TileRowShowSize);

            vm.ToggleContentRowCommand.Execute(CardRowKind.CompressedSize);
            Assert.NotEqual(snapshot.ContentRowShowCompressedSize, AppSettings.Load().ContentRowShowCompressedSize);
            vm.ToggleContentRowCommand.Execute(CardRowKind.CompressedSize);
            Assert.Equal(snapshot.ContentRowShowCompressedSize, AppSettings.Load().ContentRowShowCompressedSize);
        }
        finally
        {
            // 兜底：把 8 个字段全部按快照还原，防测试中断半途污染本机设置
            var disk = AppSettings.Load();
            MainWindowViewModel.ApplyCardRow(disk, true, CardRowKind.Size, snapshot.TileRowShowSize);
            MainWindowViewModel.ApplyCardRow(disk, true, CardRowKind.CompressedSize, snapshot.TileRowShowCompressedSize);
            MainWindowViewModel.ApplyCardRow(disk, true, CardRowKind.Ratio, snapshot.TileRowShowRatio);
            MainWindowViewModel.ApplyCardRow(disk, true, CardRowKind.Date, snapshot.TileRowShowDate);
            MainWindowViewModel.ApplyCardRow(disk, false, CardRowKind.Size, snapshot.ContentRowShowSize);
            MainWindowViewModel.ApplyCardRow(disk, false, CardRowKind.CompressedSize, snapshot.ContentRowShowCompressedSize);
            MainWindowViewModel.ApplyCardRow(disk, false, CardRowKind.Ratio, snapshot.ContentRowShowRatio);
            MainWindowViewModel.ApplyCardRow(disk, false, CardRowKind.Date, snapshot.ContentRowShowDate);
            disk.Save();
        }
    }

    // ── ④ 无 App 环境兜底 ─────────────────────────────────────

    [Fact]
    public void RefreshThumbnailRowResources_NoAppEnvironment_ReturnsSilently()
    {
        // 单测环境 Application.Current 不是 MantisZip App ⇒ 守卫应静默返回、不抛
        App.RefreshThumbnailRowResources();
    }
}
