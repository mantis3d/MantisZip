using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 压缩端「完成后打开目标目录」的回归测试：设置载荷是否带开关、会话是否按开关调用宿主端口、
/// 进度窗口底部开关的说明文案是否随流程切换。
///
/// 为什么要这三层：开关有三处入口（设置页给默认值、压缩对话框给本次初值、进度窗口底部实时改），
/// 漏接任何一处都会表现为「勾了不生效」——本仓库历史上已经因为「手抄清单漏项」出过三次同类事故。
/// </summary>
public sealed class CompressOpenFolderAfterTests : IDisposable
{
    private readonly string _tempDir;

    public CompressOpenFolderAfterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MantisZipOpenFolderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响断言结果。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void SettingsRecord_DefaultsToNotOpeningFolder()
    {
        var withoutFlag = BuildSettings(openFolderAfterCompress: null);
        var withFlag = BuildSettings(openFolderAfterCompress: true);

        Assert.False(withoutFlag.OpenFolderAfterCompress);
        Assert.True(withFlag.OpenFolderAfterCompress);
    }

    [AvaloniaFact]
    public void ProgressViewModel_CompressFlow_ShowsCompressTooltip()
    {
        var vm = new ProgressViewModel();

        vm.SetCompressPostActions(openFolder: true, openFolderSeed: true);

        Assert.True(vm.ShowOpenFolderToggle);
        Assert.True(vm.OpenFolderAfterExtract);
        Assert.Equal(
            LocalizationManager.T("Compress_OpenFolderAfter"),
            vm.OpenFolderToggleTipText);
    }

    [AvaloniaFact]
    public void ProgressViewModel_ExtractFlow_SwitchesTooltipBack()
    {
        var vm = new ProgressViewModel();
        vm.SetCompressPostActions(openFolder: true, openFolderSeed: true);

        vm.SetExtractPostActions(openFolder: true, deleteArchive: false, openFolderSeed: false);

        Assert.False(vm.OpenFolderAfterExtract);
        Assert.Equal(
            LocalizationManager.T("Settings_Extract_OpenFolderAfter"),
            vm.OpenFolderToggleTipText);
    }

    [AvaloniaFact]
    public void ProgressViewModel_WithoutCapability_HidesToggle()
    {
        var vm = new ProgressViewModel();

        vm.SetCompressPostActions(openFolder: false);

        Assert.False(vm.ShowOpenFolderToggle);
        Assert.False(vm.OpenFolderAfterExtract);
    }

    [Fact]
    public async Task Session_OpensOutputFolder_WhenSettingEnabled()
    {
        var opened = new List<IReadOnlyList<string>>();
        var session = BuildSession(opened);

        var outcome = await session.RunAsync(
            BuildSettings(openFolderAfterCompress: true),
            BuildContext(),
            TestContext.Current.CancellationToken);

        Assert.True(outcome.IsCompleted);
        var paths = Assert.Single(opened);
        Assert.Equal(ResolveOutputPath(), Assert.Single(paths));
    }

    [Fact]
    public async Task Session_DoesNotOpen_WhenSettingDisabled()
    {
        var opened = new List<IReadOnlyList<string>>();
        var session = BuildSession(opened);

        var outcome = await session.RunAsync(
            BuildSettings(openFolderAfterCompress: false),
            BuildContext(),
            TestContext.Current.CancellationToken);

        Assert.True(outcome.IsCompleted);
        Assert.Empty(opened);
    }

    [Fact]
    public async Task Session_DoesNotOpen_WhenHostHasNoCapability()
    {
        // CLI / 测试流程：宿主不注入打开端口 ⇒ 即使设置里开着也不弹资源管理器。
        var session = BuildSession(openOutputFolder: null);

        var outcome = await session.RunAsync(
            BuildSettings(openFolderAfterCompress: true),
            BuildContext(),
            TestContext.Current.CancellationToken);

        Assert.True(outcome.IsCompleted);
    }

    [Fact]
    public async Task OpenOutputFolder_EmptyPaths_CompletesWithoutThrowing()
    {
        await CompressFlow.OpenOutputFolderAsync(Array.Empty<string>());
        await CompressFlow.OpenOutputFolderAsync(new[] { "   " });
    }

    private CompressSession BuildSession(
        List<IReadOnlyList<string>>? opened = null,
        Func<IReadOnlyList<string>, Task>? openOutputFolder = null,
        bool hostHasCapability = true)
    {
        if (hostHasCapability && openOutputFolder is null && opened is not null)
        {
            openOutputFolder = paths =>
            {
                opened.Add(paths);
                return Task.CompletedTask;
            };
        }

        return new CompressSession(
            compressAsync: (_, _, _, _, _, _) => Task.FromResult(new CompressResult { Succeeded = 1 }),
            recordOutputHistory: _ => { },
            addPassword: (_, _, _) => { },
            updatePassword: (_, _, _, _) => { },
            markPasswordUsed: _ => { },
            loadSettings: () => new AppSettings(),
            openOutputFolder: openOutputFolder);
    }

    private CompressRunContext BuildContext()
        => new(
            Owner: null,
            Presenter: new RecordingPresenter(),
            ExitPolicy: ExitPolicy.None,
            DisplayPaths: new[] { ResolveOutputPath() },
            IsCompressFlow: true);

    private string ResolveOutputPath() => Path.Combine(_tempDir, "out.zip");

    private CompressSettings BuildSettings(bool? openFolderAfterCompress)
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(sourcePath, "hello");
        var outputPath = ResolveOutputPath();
        var plan = new List<CompressPlanEntry> { new(sourcePath, outputPath, null) };
        var engineOptions = new CompressEngineOptions(
            ZipCompressionMethod: "deflate",
            ZipEncryptionMethod: null,
            SevenZipCompressionMethod: "LZMA2",
            SevenZipSolid: false,
            SevenZipSolidBlockSize: null,
            SevenZipDictionarySize: null,
            SevenZipNumFastBytes: null,
            SevenZipMatchFinder: null,
            SevenZipMultithreaded: false,
            SevenZipEncryptHeaders: false,
            FileNameEncoding: "utf-8",
            KeepOriginalExtension: false,
            AdaptiveCompression: false,
            AdaptiveSmartDetect: false);

        CompressSettings Build(bool flag) => new(
            Sources: new List<CompressSourceEntry> { new(sourcePath, outputPath) },
            OutputMode: CompressOutputMode.Manual,
            Format: "zip",
            CompressionLevel: 5,
            EngineOptions: engineOptions,
            OutputPath: outputPath,
            Encrypt: false,
            Password: null,
            PasswordWriteback: new CompressPasswordWriteback(false, false, null, Array.Empty<string>(), null, null, null),
            Comment: null,
            CommentDistribution: CommentDistribution.AllSame,
            SplitSizeBytes: 0,
            MultiThreaded: false,
            ParallelDegree: 0,
            ConflictAction: null,
            Filter: null,
            Plan: plan,
            OpenFolderAfterCompress: flag);

        // null = 不传新参数，验证记录默认值是「不打开」。
        return openFolderAfterCompress is { } flag ? Build(flag) : Build(false);
    }

    /// <summary>
    /// 只跑 Operation、不真的建窗口的呈现器：本测试只关心「会话收尾时是否调用打开端口」，
    /// 窗口渲染另有 ProgressWindowPinTests / ProgressViewModelTests 覆盖。
    /// </summary>
    private sealed class RecordingPresenter : IProgressPresenter
    {
        public ProgressWindow? ActiveWindow => null;

        public async Task<bool> RunAsync(ProgressRunRequest request)
        {
            await request.Operation(new Progress<ArchiveProgress>(_ => { }), CancellationToken.None);
            return true;
        }
    }
}
