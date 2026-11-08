using System;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 出厂默认值回归（2026-11-08 按用户实际配置固化）。
///
/// 背景：这些值原本是「历史默认」，本次统一改成用户真机调好的那套配置；
/// 用例把它们钉住，防止后续有人按旧印象「顺手改回去」而没人发现。
/// 注意只钉「用户可改的设置项」——窗口尺寸/列宽等布局默认在 XAML 里，不在此处。
/// </summary>
public class FactoryDefaultsTests
{
    [Fact]
    public void AppSettings_Defaults_MatchAgreedProfile()
    {
        var settings = new AppSettings();

        Assert.Equal(5, settings.AutoCloseDelaySeconds);
        Assert.Equal(18, settings.TextPreviewFontSize);
        Assert.Equal(20L * 1024 * 1024, settings.MaxPreviewFileSize);
        Assert.Equal(24, settings.FontPreviewFontSize);
        Assert.True(settings.ThumbnailShowBorder);
        Assert.False(settings.ThumbnailUseTransparency);
        Assert.Equal("#7C818A", settings.ThumbnailBackdropColor);
    }

    [Fact]
    public void AppSettings_ParallelDegree_StaysMachineAdaptive()
    {
        // 2026-11-08 决定：并行解压线程数不跟随用户本机的 16，保持按 CPU 核数自适应
        Assert.Equal(Environment.ProcessorCount, new AppSettings().ParallelExtractDegree);
    }

    [Fact]
    public void MetadataPanel_DefaultTemplate_IsEmbeddedUserProfile()
    {
        var settings = MetadataSettingsManager.CreateDefault();

        // 用户把字段布局改成横排，且各类型字段有自定义位置/行号——这些必须来自内置模板，
        // 而不是 MetadataRegistry 的代码生成默认值（后者全部 infoPanel / Order=字段数×10 / Row=0）
        Assert.Equal("horizontal", settings.FieldLayoutMode);
        Assert.Equal(new[] { "common", "format" }, settings.SectionOrder);
        Assert.True(settings.Types.Count > 10,
            $"内置模板应包含全部已注册类型，实际只有 {settings.Types.Count} 个");

        var fileName = settings.Types["common"].Fields["FileName"];
        Assert.Equal("infoPanel", fileName.Position);
        Assert.Equal(50, fileName.Order);

        var productName = settings.Types["pe"].Fields["ProductName"];
        Assert.Equal("contentTop", productName.Position);
        Assert.Equal(1, productName.Row);
    }
}
