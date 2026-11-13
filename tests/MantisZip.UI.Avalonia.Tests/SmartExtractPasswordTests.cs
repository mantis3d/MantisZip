using System;
using System.IO;
using System.Threading.Tasks;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 回归：CLI 智能解压（<c>--extract-smart</c>，资源管理器右键「智能解压到此处」同源）在
/// <b>加密文件名</b>的包上必须弹密码框。
///
/// 缺陷背景：结构分析（判断单根目录 / 散列结构以决定解到哪）发生在取密码之前，且调用
/// <c>ListEntriesAsync</c> 时没传密码。7z <c>-mhe=on</c> / RAR 加密文件名的包不提供密码连
/// 条目都列不出来，异常被外层 catch 吞成一行 stderr 后进程直接退出 —— 用户看不到任何密码框，
/// 也没有进度窗。语料见 tests/corpus/corrupt-archives/（密码均为 <c>mantis</c>）。
///
/// 本测试注入 <c>promptPassword</c> 回调替代弹窗，无需 UI 即可验证
/// 「密码类异常 → 取密码 → 重试结构分析」这条链路。
/// </summary>
public class SmartExtractPasswordTests
{
    private const string CorpusPassword = "mantis";

    [Theory]
    [InlineData("enc-04-7z-encrypt-headers.7z")]        // 7z -mhe=on
    [InlineData("enc-06-rar-headers-encrypted.rar")]    // RAR 加密文件名
    public async Task ResolveSmartDest_HeaderEncrypted_PromptsThenSucceeds(string fileName)
    {
        using var tmp = new TempArchive(fileName);
        var engine = ArchiveEngineFactory.GetEngineByExtension(tmp.ArchivePath)!;
        var prompts = 0;

        var (targetDir, password, _, _) = await App.ResolveSmartDestWithPasswordAsync(
            tmp.ArchivePath,
            engine,
            initialPassword: null,
            promptPassword: _ =>
            {
                prompts++;
                return Task.FromResult<string?>(CorpusPassword);
            },
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, prompts);                       // 只问一次：输对后不再重弹
        Assert.Equal(CorpusPassword, password);         // 密码必须回传给后续解压（否则解压阶段再弹一次）
        Assert.False(string.IsNullOrEmpty(targetDir));
    }

    [Fact]
    public async Task ResolveSmartDest_HeaderEncrypted_UserCancels_ThrowsCancelled()
    {
        using var tmp = new TempArchive("enc-04-7z-encrypt-headers.7z");
        var engine = ArchiveEngineFactory.GetEngineByExtension(tmp.ArchivePath)!;

        await Assert.ThrowsAsync<PasswordRetryCancelledException>(() =>
            App.ResolveSmartDestWithPasswordAsync(
                tmp.ArchivePath,
                engine,
                initialPassword: null,
                promptPassword: _ => Task.FromResult<string?>(null),
                ct: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// 输错密码必须重弹（而不是拿着错密码继续跑）：第一次给 wrongpw、第二次给正确密码，
    /// 期望回调被调用两次且最终回传的是正确密码。
    /// </summary>
    [Fact]
    public async Task ResolveSmartDest_HeaderEncrypted_WrongPassword_Reprompts()
    {
        using var tmp = new TempArchive("enc-04-7z-encrypt-headers.7z");
        var engine = ArchiveEngineFactory.GetEngineByExtension(tmp.ArchivePath)!;
        var prompts = 0;

        var (_, password, _, _) = await App.ResolveSmartDestWithPasswordAsync(
            tmp.ArchivePath,
            engine,
            initialPassword: null,
            promptPassword: _ =>
            {
                prompts++;
                return Task.FromResult<string?>(prompts == 1 ? "wrongpw" : CorpusPassword);
            },
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, prompts);
        Assert.Equal(CorpusPassword, password);
    }

    [Fact]
    public async Task ResolveSmartDest_PlainArchive_DoesNotPrompt()
    {
        using var tmp = new TempArchive("valid.7z");
        var engine = ArchiveEngineFactory.GetEngineByExtension(tmp.ArchivePath)!;
        var prompts = 0;

        var (targetDir, password, _, _) = await App.ResolveSmartDestWithPasswordAsync(
            tmp.ArchivePath,
            engine,
            initialPassword: null,
            promptPassword: _ =>
            {
                prompts++;
                return Task.FromResult<string?>(CorpusPassword);
            },
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(0, prompts);
        Assert.Null(password);
        Assert.False(string.IsNullOrEmpty(targetDir));
    }

    /// <summary>
    /// 损坏包（头部结构探测判定 Broken）不得被当成「需要密码」——
    /// 否则会对着一个永远打不开的包反复弹密码框。
    /// </summary>
    [Fact]
    public async Task ResolveSmartDest_CorruptArchive_DoesNotPrompt()
    {
        using var tmp = new TempArchive("corrupt-08-7z-bad-header.7z");
        var engine = ArchiveEngineFactory.GetEngineByExtension(tmp.ArchivePath)!;
        var prompts = 0;

        await Assert.ThrowsAnyAsync<Exception>(() =>
            App.ResolveSmartDestWithPasswordAsync(
                tmp.ArchivePath,
                engine,
                initialPassword: null,
                promptPassword: _ =>
                {
                    prompts++;
                    return Task.FromResult<string?>(CorpusPassword);
                },
                ct: TestContext.Current.CancellationToken));

        Assert.Equal(0, prompts);
    }

    /// <summary>
    /// 把语料复制到独立临时目录再跑：结构分析对「散列结构」会创建目标子目录，
    /// 直接用仓库语料路径会污染 tests/corpus。
    /// </summary>
    private sealed class TempArchive : IDisposable
    {
        private readonly string _dir;

        public TempArchive(string corpusFileName)
        {
            _dir = Path.Combine(Path.GetTempPath(), "mantiszip-smart-pw-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            ArchivePath = Path.Combine(_dir, corpusFileName);
            File.Copy(CorpusPath(corpusFileName), ArchivePath, overwrite: true);
        }

        public string ArchivePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* 临时目录尽力清理 */ }
        }

        private static string CorpusPath(string fileName) =>
            Path.Combine(GetRepoRoot(), "tests", "corpus", "corrupt-archives", fileName);

        /// <summary>从测试程序集输出目录向上遍历，找到第一个含 src 子目录的目录作为仓库根。</summary>
        private static string GetRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
                dir = Path.GetDirectoryName(dir);
            return dir ?? throw new InvalidOperationException(
                $"无法定位仓库根（未找到 'src' 目录）。Base directory: {AppContext.BaseDirectory}");
        }
    }
}
