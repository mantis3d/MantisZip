using System.IO.Compression;
using Avalonia.Headless.XUnit;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 行为契约测试：锁定 preserveFullPath × currentFolder 矩阵在「真实落盘路径」上的契约
/// （来源：.omo/plans/未开始/extract-preserve-full-path-toggle.md §4.2-A）。
/// 预览树与实际解压共用 ExtractPathResolver 计算输出路径，本测试用真实的
/// SelectedItemsExtractService 走完整解压链路，证明两者契约一致。
/// 夹具压缩包用 System.IO.Compression 显式条目名构建（不用 ZipEngine.CompressAsync，
/// 后者会给条目加上源目录名前缀，破坏 docs/ 前缀语义）。
/// </summary>
public class ExtractPreserveFullPathContractTests
{
    /// <summary>
    /// 契约行 1：currentFolder="docs" + preserveFullPath=true → 保留完整路径，
    /// 落盘为 docs/readme.txt 与 docs/img/logo.png（前缀不被裁剪）。
    /// </summary>
    [AvaloniaFact]
    public async Task Extract_PreserveTrue_WithCurrentFolder_KeepsFullEntryPaths()
    {
        var (tmp, archivePath, entries) = await CreateFixtureAsync();
        try
        {
            var files = await ExtractAsync(archivePath, entries, tmp, currentFolder: "docs", preserveFullPath: true);

            Assert.Equal(
                new[] { "docs/img/logo.png", "docs/readme.txt" },
                files);
        }
        finally
        {
            Cleanup(tmp);
        }
    }

    /// <summary>
    /// 契约行 2：currentFolder="docs" + preserveFullPath=false → 裁剪当前浏览层前缀，
    /// 落盘为 readme.txt 与 img/logo.png（不含 docs/ 前缀）。
    /// </summary>
    [AvaloniaFact]
    public async Task Extract_PreserveFalse_WithCurrentFolder_TrimsCurrentFolderPrefix()
    {
        var (tmp, archivePath, entries) = await CreateFixtureAsync();
        try
        {
            var files = await ExtractAsync(archivePath, entries, tmp, currentFolder: "docs", preserveFullPath: false);

            Assert.Equal(
                new[] { "img/logo.png", "readme.txt" },
                files);
        }
        finally
        {
            Cleanup(tmp);
        }
    }

    /// <summary>
    /// 契约行 3（关键）：压缩包根目录（currentFolder=""）下两种 preserveFullPath 设置
    /// 产出完全相同的文件集合，且等于完整条目路径 —— 这是「根目录禁用该开关」
    /// 决策的可执行证明（两种设置在根目录等价，开关无效）。
    /// </summary>
    [AvaloniaFact]
    public async Task Extract_AtArchiveRoot_BothSettings_ProduceIdenticalFileSets()
    {
        var (tmp, archivePath, entries) = await CreateFixtureAsync();
        try
        {
            var filesPreserveTrue = await ExtractAsync(archivePath, entries, tmp, currentFolder: "", preserveFullPath: true, destName: "out_true");
            var filesPreserveFalse = await ExtractAsync(archivePath, entries, tmp, currentFolder: "", preserveFullPath: false, destName: "out_false");

            // 两次根目录解压结果必须逐字一致（开关在根目录不产生任何差异）
            Assert.Equal(filesPreserveTrue, filesPreserveFalse);
            Assert.Equal(
                new[] { "docs/img/logo.png", "docs/readme.txt" },
                filesPreserveTrue);
        }
        finally
        {
            Cleanup(tmp);
        }
    }

    /// <summary>
    /// 创建夹具：临时目录 + 仅含 docs/readme.txt、docs/img/logo.png 两个显式条目的 zip，
    /// 并通过 ZipEngine.ListEntriesAsync 取得真实 ArchiveItem 列表。
    /// </summary>
    private static async Task<(string Tmp, string ArchivePath, IReadOnlyList<ArchiveItem> Entries)> CreateFixtureAsync()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mz_pfp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var archivePath = Path.Combine(tmp, "a.zip");

        using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var readme = zip.CreateEntry("docs/readme.txt");
            using (var writer = new StreamWriter(readme.Open()))
            {
                writer.Write("hi");
            }

            var logo = zip.CreateEntry("docs/img/logo.png");
            using (var stream = logo.Open())
            {
                stream.Write(new byte[] { 1, 2, 3 });
            }
        }

        var engine = new ZipEngine();
        var entries = await engine.ListEntriesAsync(archivePath, password: null, cancellationToken: TestContext.Current.CancellationToken);
        return (tmp, archivePath, entries);
    }

    /// <summary>
    /// 通过真实的 SelectedItemsExtractService 解压到独立子目录，返回落盘文件的
    /// 已排序正斜杠相对路径列表（与期望数组可直接 Assert.Equal 比较）。
    /// conflictAction 用 "overwrite"：CreateExtractOptions 对其返回 null，
    /// 从而绕开冲突弹窗与 AppSettings 读取路径。
    /// </summary>
    private static async Task<List<string>> ExtractAsync(
        string archivePath,
        IReadOnlyList<ArchiveItem> entries,
        string tmp,
        string currentFolder,
        bool preserveFullPath,
        string destName = "out")
    {
        var dest = Path.Combine(tmp, destName);
        var service = new SelectedItemsExtractService();
        await service.ExtractEntriesAsync(
            archivePath,
            password: null,
            entries: entries,
            destinationPath: dest,
            conflictAction: "overwrite",
            currentFolder: currentFolder,
            preserveFullPath: preserveFullPath,
            conflictDialog: null,
            progress: new Progress<ArchiveProgress>(),
            cancellationToken: TestContext.Current.CancellationToken);

        if (!Directory.Exists(dest)) return new List<string>();

        var prefixLen = dest.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1;
        return Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories)
            .Select(p => p.Substring(prefixLen).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 清理临时夹具目录（递归），目录不存在时忽略。
    /// </summary>
    private static void Cleanup(string tmp)
    {
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
    }
}
