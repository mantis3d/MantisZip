using System;
using System.IO;
using System.Linq;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 压缩设置窗「包内根目录」四态下拉：选项裁剪、静默回退、选项与枚举的对应关系。
/// 四态曾经是竖排单选（占四行），改成下拉后最容易出的错是「选项还在但选不了」
/// 或「选了却跑不起来」—— 这些用例盯的就是那两件事。
/// </summary>
public class RootModeDropdownTests : IDisposable
{
    private readonly string _root;
    private readonly RootDirectoryMode _savedMode;
    private readonly string _savedName;
    private readonly RootDirectoryMode _savedExtractMode;

    public RootModeDropdownTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mz-rootmode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var settings = AppSettings.Load();
        _savedMode = settings.CompressRootMode;
        _savedName = settings.CompressRootName;
        _savedExtractMode = settings.ExtractRootMode;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 清理失败不影响断言 */ }

        // 设置是进程级缓存 + 落盘，测试必须还原，否则污染其它用例
        var settings = AppSettings.Load();
        settings.CompressRootMode = _savedMode;
        settings.CompressRootName = _savedName;
        settings.ExtractRootMode = _savedExtractMode;
        settings.Save();
        AppSettings.ResetCacheForTests();
        GC.SuppressFinalize(this);
    }

    private static void Apply(Action<AppSettings> mutate)
    {
        var settings = AppSettings.Load();
        mutate(settings);
        settings.Save();
        AppSettings.ResetCacheForTests();
    }

    private string MakeDir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private string MakeFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "x");
        return path;
    }

    // ── 单目录源：四档齐全 ──

    [Fact]
    public void SingleDirectorySource_OffersAllFourModes()
    {
        Apply(s => s.CompressRootMode = RootDirectoryMode.Original);

        var vm = new CompressSettingsViewModel(new[] { MakeDir("only") });

        Assert.Equal(4, vm.RootModeOptions.Count);
        Assert.Equal(
            new[]
            {
                nameof(RootDirectoryMode.Original),
                nameof(RootDirectoryMode.SmartStrip),
                nameof(RootDirectoryMode.SmartWrap),
                nameof(RootDirectoryMode.AlwaysWrap),
            },
            vm.RootModeOptions.Select(o => o.Value).ToArray());
        Assert.False(vm.IsRootModeHintVisible);
    }

    // ── 单文件源：没有「目录本身」可剥，去根档位必须消失 ──

    [Fact]
    public void SingleFileSource_HidesSmartStrip()
    {
        var vm = new CompressSettingsViewModel(new[] { MakeFile("a.txt") });

        Assert.DoesNotContain(vm.RootModeOptions, o => o.Value == nameof(RootDirectoryMode.SmartStrip));
        Assert.Contains(vm.RootModeOptions, o => o.Value == nameof(RootDirectoryMode.SmartWrap));
        Assert.True(vm.IsRootModeHintVisible);
        Assert.False(string.IsNullOrWhiteSpace(vm.RootModeHint));
    }

    // ── 多源：没有唯一根可剥 ──

    [Fact]
    public void MultipleSources_HideSmartStrip()
    {
        var vm = new CompressSettingsViewModel(new[] { MakeDir("one"), MakeDir("two") });

        Assert.DoesNotContain(vm.RootModeOptions, o => o.Value == nameof(RootDirectoryMode.SmartStrip));
        Assert.True(vm.IsRootModeHintVisible);
    }

    // ── 默认值落在不可用档位时，必须静默回退原样而不是留一个死选择 ──

    [Fact]
    public void DefaultModeThatBecomesUnavailable_FallsBackToOriginal()
    {
        Apply(s => s.CompressRootMode = RootDirectoryMode.SmartStrip);

        // 多源 ⇒「智能去除」不可用 ⇒ 构造时回退
        var vm = new CompressSettingsViewModel(new[] { MakeDir("one"), MakeDir("two") });

        Assert.Equal(RootDirectoryMode.Original, vm.RootMode);
        Assert.Equal(nameof(RootDirectoryMode.Original), vm.SelectedRootModeOption?.Value);
    }

    [Fact]
    public void DefaultModeThatStaysAvailable_IsKept()
    {
        Apply(s => s.CompressRootMode = RootDirectoryMode.SmartStrip);

        var vm = new CompressSettingsViewModel(new[] { MakeDir("only") });

        Assert.Equal(RootDirectoryMode.SmartStrip, vm.RootMode);
        Assert.Equal(nameof(RootDirectoryMode.SmartStrip), vm.SelectedRootModeOption?.Value);
    }

    // ── 下拉写入：按枚举名往返，不认语言 ──

    [Fact]
    public void SelectingOption_WritesEnumMode()
    {
        var vm = new CompressSettingsViewModel(new[] { MakeDir("only") });

        vm.SelectedRootModeOption = vm.RootModeOptions.First(o => o.Value == nameof(RootDirectoryMode.AlwaysWrap));

        Assert.Equal(RootDirectoryMode.AlwaysWrap, vm.RootMode);
        Assert.True(vm.IsWrapRootVisible);
        Assert.True(vm.IsWrapActive);
    }

    // ── 分别压缩：每包各一层，套根没有意义 ⇒ 只剩原样与去根 ──

    [Fact]
    public void SeparateOutputMode_HidesWrapModes()
    {
        var vm = new CompressSettingsViewModel(new[] { MakeDir("only") })
        {
            OutputMode = CompressOutputMode.Separate,
        };

        Assert.DoesNotContain(vm.RootModeOptions, o => o.Value == nameof(RootDirectoryMode.SmartWrap));
        Assert.DoesNotContain(vm.RootModeOptions, o => o.Value == nameof(RootDirectoryMode.AlwaysWrap));
        Assert.True(vm.IsRootModeHintVisible);
    }

    // ── 提示文字随可用性换句子（三处分支都要有真实文案，不能是空串或 key 名） ──

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void RootModeHint_MatchesAvailability(bool canStrip, bool canWrap)
    {
        var vm = canStrip
            ? new CompressSettingsViewModel(new[] { MakeDir("only") })
            : new CompressSettingsViewModel(new[] { MakeDir("one"), MakeDir("two") });

        if (!canWrap)
            vm.OutputMode = CompressOutputMode.Separate;

        var hint = vm.RootModeHint;

        Assert.Equal(canStrip && canWrap, !vm.IsRootModeHintVisible);
        if (canStrip && canWrap)
        {
            Assert.Equal("", hint);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(hint));
            // 文案必须是本地化后的中文，不能是没登记的 key 名
            Assert.DoesNotContain("Compress_RootMode_Hint", hint);
        }
    }

    // ── 说明文字：每档都要有对应文案，且切换档位后跟着变 ──

    [Fact]
    public void Description_FollowsSelectedMode()
    {
        var vm = new CompressSettingsViewModel(new[] { MakeDir("only") });

        var original = vm.RootModeDescription;
        vm.RootMode = RootDirectoryMode.AlwaysWrap;

        Assert.False(string.IsNullOrWhiteSpace(original));
        Assert.False(string.IsNullOrWhiteSpace(vm.RootModeDescription));
        Assert.NotEqual(original, vm.RootModeDescription);
        Assert.DoesNotContain("_Tip", vm.RootModeDescription);
    }

    // ── 空源：没有任何档位可用？不 —— 原样必须永远在，否则下拉是空框 ──

    [Fact]
    public void NoSources_StillOffersOriginal()
    {
        var vm = new CompressSettingsViewModel(Array.Empty<string>());

        Assert.Contains(vm.RootModeOptions, o => o.Value == nameof(RootDirectoryMode.Original));
        Assert.NotNull(vm.SelectedRootModeOption);
    }
}
