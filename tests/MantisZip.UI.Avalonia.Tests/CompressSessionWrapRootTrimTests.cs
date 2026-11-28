using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 压缩载荷里「根名」的裁剪口径：只有真的会加根的档位才允许带根名。
///
/// 为什么值得单测：GUI 侧 <c>CompressSettingsViewModel</c> 的快照已经按 <c>IsWrapActive</c> 裁过一次，
/// 所以这条缺口在界面上永远看不见 —— 只有 CLI 或测试直接构造载荷时才暴露。执行器自己再裁一次，
/// 是为了让「原样档带着上一轮留下的根名跑」这件事在契约层就不可能发生，而不是靠每个调用方自觉。
/// </summary>
public sealed class CompressSessionWrapRootTrimTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), $"mz_wraproot_{Guid.NewGuid():N}");

    public CompressSessionWrapRootTrimTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* 忽略清理失败 */ }
    }

    /// <summary>原样档：即使载荷里残留根名，执行器也必须把它清成 null。</summary>
    [Fact]
    public async Task OriginalMode_DropsLeftoverWrapRoot()
        => Assert.Null(await CaptureRequestWrapRootAsync(RootDirectoryMode.Original));

    /// <summary>智能去根档：只剥不加，根名同样没有意义。</summary>
    [Fact]
    public async Task SmartStripMode_DropsLeftoverWrapRoot()
        => Assert.Null(await CaptureRequestWrapRootAsync(RootDirectoryMode.SmartStrip));

    /// <summary>智能加根档：根名是用户真实的选择，必须原样带到执行侧。</summary>
    [Fact]
    public async Task SmartWrapMode_KeepsWrapRoot()
        => Assert.Equal("kept", await CaptureRequestWrapRootAsync(RootDirectoryMode.SmartWrap));

    /// <summary>始终加根档：同上。</summary>
    [Fact]
    public async Task AlwaysWrapMode_KeepsWrapRoot()
        => Assert.Equal("kept", await CaptureRequestWrapRootAsync(RootDirectoryMode.AlwaysWrap));

    /// <summary>
    /// 构造一份带「残留根名 kept」的载荷，跑一次会话，取回执行器实际递给引擎的请求。
    /// 根名故意给成固定值：若被保留，断言能直接看出是哪一个档位没裁。
    /// </summary>
    private async Task<string?> CaptureRequestWrapRootAsync(RootDirectoryMode rootMode)
    {
        CompressRequest? captured = null;

        var session = new CompressSession(
            compressAsync: (request, _, _, _, _, _) =>
            {
                captured = request;
                return Task.FromResult(new CompressResult { Succeeded = 1 });
            },
            recordOutputHistory: _ => { },
            addPassword: (_, _, _) => { },
            updatePassword: (_, _, _, _) => { },
            markPasswordUsed: _ => { },
            loadSettings: () => new AppSettings(),
            openOutputFolder: null);

        var outcome = await session.RunAsync(
            BuildSettings(rootMode),
            new CompressRunContext(
                Owner: null,
                Presenter: new SilentPresenter(),
                ExitPolicy: ExitPolicy.None,
                IsCompressFlow: true),
            TestContext.Current.CancellationToken);

        Assert.True(outcome.IsCompleted);
        Assert.NotNull(captured);
        return captured!.WrapRoot;
    }

    private CompressSettings BuildSettings(RootDirectoryMode rootMode)
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(sourcePath, "hello");
        var outputPath = Path.Combine(_tempDir, "out.zip");

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
            AdaptiveCompression: false,
            AdaptiveSmartDetect: false);

        return new CompressSettings(
            Sources: new List<CompressSourceEntry> { new(sourcePath, outputPath) },
            OutputMode: CompressOutputMode.Manual,
            Format: "zip",
            CompressionLevel: 5,
            EngineOptions: engineOptions,
            OutputPath: outputPath,
            Encrypt: false,
            Password: null,
            PasswordWriteback: new CompressPasswordWriteback(
                false, false, null, Array.Empty<string>(), null, null, null),
            Comment: null,
            CommentDistribution: CommentDistribution.AllSame,
            SplitSizeBytes: 0,
            MultiThreaded: false,
            ParallelDegree: 0,
            ConflictAction: null,
            Filter: null,
            Plan: new List<CompressPlanEntry> { new(sourcePath, outputPath, null) },
            OpenFolderAfterCompress: false,
            RootMode: rootMode,
            WrapRoot: "kept");
    }

    /// <summary>只跑 Operation、不建窗口的呈现器（与 CompressOpenFolderAfterTests 的同名实现同构）。</summary>
    private sealed class SilentPresenter : IProgressPresenter
    {
        public ProgressWindow? ActiveWindow => null;

        public async Task<bool> RunAsync(ProgressRunRequest request)
        {
            await request.Operation(new Progress<ArchiveProgress>(_ => { }), CancellationToken.None);
            return true;
        }
    }
}
