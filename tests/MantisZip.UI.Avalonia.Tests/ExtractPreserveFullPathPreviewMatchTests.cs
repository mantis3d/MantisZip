using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 计划 §7 验收标准中**可自动化**的部分（人工验收项的机器等价物，证据强度高于人工点击）。
/// </summary>
/// <remarks/// 覆盖：
/// <list type="bullet">
/// <item>DoD 10（核心验收点）——勾选/取消勾选后，<b>预览树路径与真实落盘路径逐条一致</b>。
/// 预览侧走 <see cref="ResultPreviewService.BuildExtractPreview"/>，解压侧走
/// <see cref="SelectedItemsExtractService"/>；两侧共用 <c>ExtractPathResolver</c>，
/// 本测试即是对「单一事实来源」契约的可执行证明。</item>
/// <item>DoD 14（决策 A 反向）——对话框内切换参数<b>不回写</b> <c>AppSettings</c>。</item>
/// <item>DoD 18（三语）——3 个新i18n key在三语文件中均存在且取值非空（空标签会导致 UI 上出现空白控件）。</item>
/// </list>
/// 真正只能靠 GUI 的项目（悬停提示渲染、多参数换行、按钮行视觉不重叠）见计划 §7 第 12/20/22 项。
/// </remarks>
/// </summary>
public class ExtractPreserveFullPathPreviewMatchTests
{
    /// <summary>
    /// DoD 10（核心验收点）：对<b>两种preserveFullPath 取值</b>分别构建预览树并真实解压，
    /// 断言两侧得到的相对路径集合完全相等 —— 即「预览所见 = 实际落盘」。
    /// </summary>
    /// <param name="currentFolder">模拟当前浏览的压缩包内目录（"" = 根目录）。</param>
    /// <param name="preserveFullPath">模拟对话框内「保留完整路径」的勾选值。</param>
    [AvaloniaTheory]
    [InlineData("docs", true)]
    [InlineData("docs", false)]
    [InlineData("", true)]
    [InlineData("", false)]
    public async Task PreviewTreePaths_MatchActualExtraction(
        string currentFolder, bool preserveFullPath)
    {
        var tmp = CreateTempDirectory();
        try
        {
            var entries = await CreateFixtureArchiveAsync(tmp);

            // 预览侧：与对话框 SchedulePreviewRebuild 调用同一函数、同一组参数
            var dest = Path.Combine(tmp, "out");
            var previewRoot = ResultPreviewService.BuildExtractPreview(
                entries, dest,
                checkExists: false,
                preserveFullPath: preserveFullPath,
                currentFolder: currentFolder);
            var previewPaths = CollectFilePaths(previewRoot);

            // 解压侧：真实落盘。
            // 必须 await 而非 GetAwaiter().GetResult()——引擎内部 await Task.Run 的续体要 post 回
            // Avalonia SynchronizationContext，同步阻塞会把调度器线程锁死。
            var service = new SelectedItemsExtractService();
            await service.ExtractEntriesAsync(
                archivePath: Path.Combine(tmp, "a.zip"),
                password: null,
                entries: entries,
                destinationPath: dest,
                conflictAction: "overwrite",
                currentFolder: currentFolder,
                preserveFullPath: preserveFullPath,
                conflictDialog: null,
                progress: new Progress<ArchiveProgress>(),
                cancellationToken: TestContext.Current.CancellationToken);
            var actualPaths = ListRelativeFiles(dest);

            Assert.NotEmpty(previewPaths);
            Assert.Equal(
                previewPaths.OrderBy(p => p, StringComparer.Ordinal),
                actualPaths.OrderBy(p => p, StringComparer.Ordinal));
        }
        finally
        {
            DeleteTempDirectory(tmp);
        }
    }

    /// <summary>
    /// DoD 10 补充：预览树在 currentFolder="docs" 时必须体现前缀裁剪差异，
    /// 且位于 docs 之外的条目（assets/…）因前缀不匹配而保持原路径
    /// —— 这是 <c>TrimCurrentFolderPrefix</c> 的既定语义，界面若显示错误会误导用户。
    /// </summary>
    [AvaloniaFact]
    public async Task PreviewTree_ShowsTrimmedPrefix_AndKeepsNonMatchingPrefix()
    {
        var tmp = CreateTempDirectory();
        try
        {
            var entries = await CreateFixtureArchiveAsync(tmp);
            var root = ResultPreviewService.BuildExtractPreview(
                entries, Path.Combine(tmp, "out"),
                checkExists: false,
                preserveFullPath: false,
                currentFolder: "docs");
            var paths = CollectFilePaths(root);

            Assert.Contains("readme.txt", paths);          // docs/readme.txt → readme.txt
            Assert.Contains("img/logo.png", paths);        // docs/img/logo.png → img/logo.png
            Assert.Contains("assets/data.json", paths);    // 前缀不匹配 → 保持原路径
            Assert.DoesNotContain("docs/readme.txt", paths);
        }
        finally
        {
            DeleteTempDirectory(tmp);
        }
    }

    /// <summary>
    /// DoD 14（决策 A 反向）：在对话框内切换「保留完整路径」参数后，
    /// <c>AppSettings.ExtractPreserveFullPath</c> 必须保持原值 —— 参数区只作用于本次解压。
    /// </summary>
    [AvaloniaFact]
    public void TogglingOption_DoesNotWriteBackToAppSettings()
    {
        var before = MantisZip.UI.Avalonia.Models.AppSettings.Load().ExtractPreserveFullPath;

        var tempDir = CreateTempDirectory();
        try
        {
            EnsureIconResources();
            var dialog = new CustomFilePickerDialog(
                PickerMode.ExtractFolder,
                [new ArchiveItem { Name = "readme.txt", FullPath = "docs/readme.txt", Size = 1, IsDirectory = false }],
                null, tempDir, null,
                currentFolder: "docs", preserveFullPath: true);

            var option = Assert.Single(dialog.Options);

            // 模拟用户点击复选框（CheckBox 双向绑定 → PickerOptionItem.IsChecked）
            option.IsChecked = false;
            Assert.False(option.IsChecked);
            // 字段与参数项保持同步（预览随之重建）
            Assert.False(dialog.SelectedPreserveFullPath);

            // 决策 A：设置项不得被本次操作改写
            Assert.Equal(before, MantisZip.UI.Avalonia.Models.AppSettings.Load().ExtractPreserveFullPath);
        }
        finally
        {
            DeleteTempDirectory(tempDir);
        }
    }

    /// <summary>
    /// DoD 18（三语）：3 个新增 i18n key 必须三语齐备且取值非空。
    /// 空字符串会让参数区出现无标签的孤儿复选框，比缺失更隐蔽。
    /// </summary>
    [Fact]
    public void NewOptionKeys_PresentAndNonEmpty_InAllThreeLanguages()
    {
        var keys = new[]
        {
            "Picker_PreserveFullPath",
            "Picker_PreserveFullPathDisabledHint",
            "Picker_OptionsCaption",
        };
        var root = GetRepoRoot();
        var locales = new[] { "zh-CN", "zh-TW", "en" };

        foreach (var locale in locales)
        {
            var path = Path.Combine(root, "src", "MantisZip.UI.Avalonia",
                "Localization", $"strings.{locale}.json");
            Assert.True(File.Exists(path), $"本地化文件不存在：{path}");

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            foreach (var key in keys)
            {
                Assert.True(doc.RootElement.TryGetProperty(key, out var v),
                    $"{locale} 缺少 key：{key}");
                Assert.False(string.IsNullOrWhiteSpace(v.GetString()),
                    $"{locale} 的 {key} 取值为空");
            }
        }
    }

    // ── 夹具与辅助 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 建夹具压缩包：显式条目名 <c>docs/readme.txt</c> / <c>docs/img/logo.png</c> / <c>assets/data.json</c>。
    /// 刻意<b>不用</b> <c>ZipEngine.CompressAsync</c>——它会给条目名加源目录前缀，条目键将变成
    /// <c>src/docs/…</c>，破坏所有断言。
    /// </summary>
    private static async Task<IReadOnlyList<ArchiveItem>> CreateFixtureArchiveAsync(string tmp)
    {
        var archivePath = Path.Combine(tmp, "a.zip");
        using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "docs/readme.txt", "hi");
            WriteEntry(zip, "docs/img/logo.png", [1, 2, 3]);
            WriteEntry(zip, "assets/data.json", "{}");
        }
        var engine = new ZipEngine();
        return await engine.ListEntriesAsync(archivePath, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] content)
    {
        var entry = zip.CreateEntry(name);
        using var s = entry.Open();
        s.Write(content, 0, content.Length);
    }

    private static void WriteEntry(ZipArchive zip, string name, string content) =>
        WriteEntry(zip, name, System.Text.Encoding.UTF8.GetBytes(content));

    /// <summary>递归收集预览树中的文件节点相对路径（跳过目录节点）。</summary>
    private static List<string> CollectFilePaths(PreviewTreeNode root)
    {
        var result = new List<string>();
        void Walk(PreviewTreeNode node)
        {
            foreach (var child in node.Children.OfType<PreviewTreeNode>())
            {
                if (child.IsDirectory) Walk(child);
                else if (!string.IsNullOrEmpty(child.FullPath)) result.Add(child.FullPath);
            }
        }
        Walk(root);
        return result;
    }

    /// <summary>枚举目标目录下的实际文件，归一化为相对路径（正斜杠、按序）。</summary>
    private static List<string> ListRelativeFiles(string dest)
    {
        if (!Directory.Exists(dest)) return [];
        var prefix = dest.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1;
        return Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories)
            .Select(p => p[prefix..].Replace('\\', '/'))
            .ToList();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mz_pfp_match_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* 清理失败不影响断言结果 */ }
    }

    /// <summary>图标资源合并的幂等标记与锁——并行测试下避免重复合并。</summary>
    private static readonly object IconMergeLock = new();
    private static bool _iconsMerged;

    /// <summary>
    /// 构造对话框前需把图标几何资源并入 <see cref="Application"/>，
    /// 否则 XAML 里的 <c>StaticResource Icon*</c> 解析失败（与 PickerOptionsRegionTests 同一做法）。
    /// </summary>
    private static void EnsureIconResources()
    {
        if (_iconsMerged) return;
        lock (IconMergeLock)
        {
            if (_iconsMerged) return;
            if (Application.Current is not null)
            {
                var icons = (ResourceDictionary)AvaloniaXamlLoader.Load(
                    new Uri("avares://MantisZip.UI.Avalonia/Resources/Icons/AppIcons.axaml"));
                Application.Current.Resources.MergedDictionaries.Add(icons);
            }
            _iconsMerged = true;
        }
    }

    /// <summary>仓库根发现：自测试程序集输出目录上溯，直到找到含<code>src</code> 的目录。
    /// 照抄 <c>tests/MantisZip.Tests/AboutWindowTests.cs</c> 的既有模式。</summary>
    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException(
            "无法定位仓库根（上溯链中未找到 src 目录）。" +
            $"Base directory: {AppContext.BaseDirectory}");
    }
}