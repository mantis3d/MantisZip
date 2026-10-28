using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// NativeSplash 水平布局测试（用户反馈「长文件名把 splash 挤满整个宽度」）：
/// 内容整体居中，但两侧必须保留 padX 空白，文本宽度上限为
/// 客户区宽度 − 两侧留白 − 图标 − 间隔，超出由 DT_END_ELLIPSIS 截断。
/// GDI 绘制本身无法在无头环境断言，故把纯算术部分抽成 ComputeHorizontalLayout。
/// </summary>
public class NativeSplashLayoutTests
{
    private const int ClientW = 360;   // NativeSplash 固定窗口宽（96dpi 基准）
    private const int IconSize = 32;
    private const int Gap = 16;
    private const int Pad = 16;

    [Fact]
    public void ShortText_IsCenteredBetweenPaddings()
    {
        var (startX, textW) = NativeSplash.ComputeHorizontalLayout(ClientW, IconSize, Gap, 120, Pad);

        Assert.Equal(120, textW); // 文本没超过上限 → 原样
        Assert.True(startX >= Pad, $"startX={startX} 应不小于左留白");
        // 整体居中：(360 - (32 + 16 + 120)) / 2 = 96
        Assert.Equal(96, startX);
    }

    [Fact]
    public void LongText_IsClampedSoBothEdgesKeepPadding()
    {
        var (startX, textW) = NativeSplash.ComputeHorizontalLayout(ClientW, IconSize, Gap, 10_000, Pad);

        // 上限 = 360 − 16*2 − 32 − 16 = 280
        Assert.Equal(280, textW);
        Assert.Equal(Pad, startX);
        var rightEdge = startX + IconSize + Gap + textW;
        Assert.Equal(ClientW - Pad, rightEdge); // 右侧同样留出 16
    }

    [Fact]
    public void NoIcon_DegradesToPlainTextLayout()
    {
        // 取不到图标时 iconSize / gap 都传 0，全部宽度留给文本（仍保留两侧留白）
        var (startX, textW) = NativeSplash.ComputeHorizontalLayout(ClientW, 0, 0, 10_000, Pad);

        Assert.Equal(ClientW - Pad * 2, textW);
        Assert.Equal(Pad, startX);
        Assert.Equal(ClientW - Pad, startX + textW);
    }

    [Fact]
    public void TooNarrow_ClampsTextWidthToZero()
    {
        var (startX, textW) = NativeSplash.ComputeHorizontalLayout(40, IconSize, Gap, 200, Pad);

        Assert.Equal(0, textW);      // 可用宽度不足 → 不画文字，也不产生负宽度
        Assert.Equal(Pad, startX);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(120)]
    [InlineData(1_000_000)]
    public void Invariants_HoldForAnyTextWidth(int measured)
    {
        var (startX, textW) = NativeSplash.ComputeHorizontalLayout(ClientW, IconSize, Gap, measured, Pad);

        Assert.True(textW >= 0);
        Assert.True(startX >= Pad);
        Assert.True(startX + IconSize + Gap + textW <= ClientW - Pad);
    }
}
