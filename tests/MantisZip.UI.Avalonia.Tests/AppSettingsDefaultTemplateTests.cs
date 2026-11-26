using System;
using System.IO;
using System.Linq;
using System.Text;
using MantisZip.UI.Avalonia.Models;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 出厂默认命名模板与预设（2026-11-22）。
///
/// 背景：新装用户的 <c>settings.json</c> 不存在时走 <see cref="AppSettings"/> 的
/// <c>CreateWithDefaults()</c>，此前模板与预设全空，用户第一次打开压缩对话框看到的是空白。
/// 现在默认注入 <c>{name:auto}</c> + 两条预设；已有设置文件的用户不受影响。
///
/// 同时钉住「保留源扩展名」开关已被删除（语义并入模板 <c>{ext}</c>），防止有人顺手加回来。
/// </summary>
public class AppSettingsDefaultTemplateTests
{
    [Fact]
    public void CreateWithDefaults_SeedsDialogTemplateAndTwoPresets()
    {
        using var sandbox = new AppSettingsTestSandbox();
        Assert.False(File.Exists(sandbox.SettingsFile), "沙箱初始不应有设置文件");

        var settings = AppSettings.Load();

        Assert.Equal("{name:auto}", settings.FileNameTemplateDialog);

        Assert.Collection(
            settings.FileNameTemplatePresets,
            first =>
            {
                Assert.Equal("Auto Name", first.Name);
                Assert.Equal("{name:auto}", first.Template);
            },
            second =>
            {
                Assert.Equal("Name & Date", second.Name);
                Assert.Equal("{name:auto}-{date}", second.Template);
            });

        // Quick / Separate 保持空：它们只服务 CLI，出厂就接管会改变命令行行为
        Assert.Equal("", settings.FileNameTemplateQuick);
        Assert.Equal("", settings.FileNameTemplateSeparate);
    }

    [Fact]
    public void CreateWithDefaults_DoesNotOverrideExistingFile()
    {
        using var sandbox = new AppSettingsTestSandbox();

        // 老用户主动清空模板 → 磁盘上有文件但模板是空串，出厂默认**不得**把它填回来
        var legacy = new AppSettings { FileNameTemplateDialog = "", FileNameTemplateQuick = "{name}" };
        Assert.True(legacy.SaveTo(sandbox.SettingsFile), "写入沙箱设置文件失败");

        AppSettings.ResetCacheForTests();
        var loaded = AppSettings.Load();

        Assert.Equal("", loaded.FileNameTemplateDialog);
        Assert.Equal("{name}", loaded.FileNameTemplateQuick);
        Assert.Empty(loaded.FileNameTemplatePresets);
    }

    [Fact]
    public void CreateWithDefaults_PresetsAreUsableAsTemplate()
    {
        // 两条内置预设本身必须是合法模板，否则用户选中后立刻在校验里报错
        using var sandbox = new AppSettingsTestSandbox();
        var settings = AppSettings.Load();

        Assert.All(settings.FileNameTemplatePresets, preset =>
            Assert.True(
                MantisZip.Core.Utils.FileNameTemplate.TryValidateSyntax(preset.Template, out var error),
                $"内置预设「{preset.Name}」不是合法模板：{error?.Kind}"));
    }

    [Fact]
    public void KeepOriginalExtension_SettingIsGone()
    {
        // 2026-11-22 删除：它名义全局、实际只作用于分别压缩，语义已被模板 {ext} 覆盖。
        // 加回来会让「模板非空时被忽略」这一段重新出现在用户面前。
        Assert.Null(typeof(AppSettings).GetProperty("KeepOriginalExtension"));
    }
}
