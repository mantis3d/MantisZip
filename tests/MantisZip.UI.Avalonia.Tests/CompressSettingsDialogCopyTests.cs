using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 「压缩设置对话框（独立 VM）→ 主窗口执行 VM」执行侧设置的**拷贝漂移守卫**。
/// <para>
/// 背景：这段拷贝原先是 <c>Views/MainWindow.axaml.cs</c> 里手写的逐属性清单，已经漏过三次
/// （多线程/智能检测/并行度恒 false、分卷大小恒 0、文件冲突策略恒「每次询问」），
/// 每次都是「字段加了、清单没加」，编译期无感、只有用户实测才暴露。
/// 现在清单收敛到 <see cref="CompressSettingsViewModel.CopyExecutionSettingsFrom"/>，
/// 本测试用反射枚举 VM 的全部可写属性，要求每个属性必须落在两张表之一：
/// <list type="bullet">
/// <item><c>Covered</c> —— 必须被拷贝：逐个赋特定值，拷贝后断言相等</item>
/// <item><c>Excluded</c> —— 明确不拷贝：必须写清理由（派生只读 / UI 瞬时态 / View 注入）</item>
/// </list>
/// 新增可写属性若两处都没登记，本测试直接失败 —— 逼开发者当场决定「要不要拷贝」。
/// </para>
/// </summary>
public class CompressSettingsDialogCopyTests
{
    private const string SeedPath = "mz-copy-seed.txt";

    /// <summary>必须被拷贝的属性 → 测试用值（值取得刻意「不像默认值」，便于断言真的被拷贝）。</summary>
    private static readonly Dictionary<string, object?> Covered = new()
    {
        [nameof(CompressSettingsViewModel.SelectedPaths)] = new[] { "mz-copy-a.txt", "mz-copy-b.txt" },
        [nameof(CompressSettingsViewModel.DefaultFormat)] = ".7z",
        [nameof(CompressSettingsViewModel.CompressionLevel)] = 7,
        [nameof(CompressSettingsViewModel.OutputMode)] = CompressOutputMode.Manual,
        [nameof(CompressSettingsViewModel.OutputPath)] = @"C:\mz-copy-probe\out.7z",
        [nameof(CompressSettingsViewModel.KeepOriginalExtension)] = true,
        [nameof(CompressSettingsViewModel.Password)] = "pw-source",
        [nameof(CompressSettingsViewModel.Encrypt)] = true,
        [nameof(CompressSettingsViewModel.IsPasswordLibraryMode)] = true,
        [nameof(CompressSettingsViewModel.SelectedPasswordEntry)] = new PasswordEntry
        {
            Id = "mz-copy-entry",
            Password = "pw-entry",
            Description = "desc-entry",
        },
        [nameof(CompressSettingsViewModel.SaveToLibrary)] = true,
        [nameof(CompressSettingsViewModel.PasswordDescription)] = "desc-source",
        [nameof(CompressSettingsViewModel.RulesText)] = "rule-source",
        [nameof(CompressSettingsViewModel.Comment)] = "comment-source",
        [nameof(CompressSettingsViewModel.CommentDistribution)] = CommentDistribution.PerLine,
        [nameof(CompressSettingsViewModel.ConflictAction)] = "rename",
        [nameof(CompressSettingsViewModel.FileNameEncoding)] = "gbk",
        [nameof(CompressSettingsViewModel.ZipCompressionMethod)] = "bzip2",
        [nameof(CompressSettingsViewModel.ZipEncryptionMethod)] = "aes256",
        [nameof(CompressSettingsViewModel.SevenZipCompressionMethod)] = "PPMd",
        [nameof(CompressSettingsViewModel.SevenZipSolid)] = false,
        [nameof(CompressSettingsViewModel.SevenZipSolidBlockSize)] = "64m",
        [nameof(CompressSettingsViewModel.SevenZipDictionarySize)] = "32m",
        [nameof(CompressSettingsViewModel.SevenZipNumFastBytes)] = 64,
        [nameof(CompressSettingsViewModel.SevenZipMatchFinder)] = "bt4",
        [nameof(CompressSettingsViewModel.SevenZipMultithreaded)] = false,
        [nameof(CompressSettingsViewModel.SevenZipEncryptHeaders)] = true,
        [nameof(CompressSettingsViewModel.AdaptiveCompression)] = true,
        [nameof(CompressSettingsViewModel.AdaptiveSmartDetect)] = true,
        [nameof(CompressSettingsViewModel.MultiThreadedCompression)] = true,
        [nameof(CompressSettingsViewModel.SelectedSplitSizeOption)] =
            new CompressionOptionData.ComboOption("1048576", "1 MB"),
        [nameof(CompressSettingsViewModel.CustomSplitSizeText)] = "123 MB",
    };

    /// <summary>明确**不**拷贝的属性 → 理由（新增可写属性必须在此登记或加进 Covered）。</summary>
    private static readonly Dictionary<string, string> Excluded = new()
    {
        ["SelectedLevelOption"] = "派生：由 CompressionLevel 单向同步回下拉",
        ["OutputDirectory"] = "派生：与 OutputPath / OutputMode 联动",
        ["OutputFileName"] = "派生：与 OutputPath / OutputMode 联动",
        ["ConfirmPassword"] = "仅 UI 二次确认，执行侧不读",
        ["SelectedZipEncryptionMethodOption"] = "派生：由 ZipEncryptionMethod 同步",
        ["SelectedConflictActionOption"] = "派生：由 ConflictAction 同步",
        ["PasswordSearchText"] = "密码库搜索框（UI 输入态）",
        ["AutoGenerateRules"] = "UI 开关；执行侧只读 RulesText",
        ["IsPasswordRevealed"] = "UI 显示态",
        ["WindowTitle"] = "由调用方设置，不属于执行设置",
        ["CommentAllSame"] = "注释分布单选辅助（写 CommentDistribution）",
        ["CommentFirstOnly"] = "注释分布单选辅助（写 CommentDistribution）",
        ["CommentPerLine"] = "注释分布单选辅助（写 CommentDistribution）",
        ["PreviewRoot"] = "预览 UI 状态",
        ["PreviewCompactMode"] = "预览 UI 状态",
        ["ShowFilteredGhosts"] = "预览 UI 状态",
        ["IsPreviewBuilding"] = "预览 UI 状态",
        ["IsBuildPending"] = "预览 UI 状态",
        ["IsPreparingCompress"] = "预览 UI 状态",
        ["PreviewBuildProgress"] = "预览 UI 状态",
        ["BrowseOutput"] = "View 注入的委托（浏览输出路径）",
        ["PickFiles"] = "View 注入的委托（选择源文件）",
        ["FileFilter"] = "由 View 从 FileFilterEditor 读取，调用方 showDialog 内单独赋值",
        ["CloseAction"] = "对话框自身的关闭结果（窗口 UI），不是执行设置",
        ["ShowMessage"] = "对话框自身的提示区（窗口 UI），不是执行设置",
        ["IsSeparateMode"] = "派生：OutputMode 的判断结果（axaml 显隐用）",
        ["IsCombinedMode"] = "派生：OutputMode 的判断结果（axaml 显隐用）",
        ["IsManualMode"] = "派生：OutputMode 的判断结果（axaml 显隐用）",
    };

    /// <summary>
    /// 守卫①：每个可写属性都必须显式分类 —— 防止「新增执行侧字段忘了拷贝」这类历史事故。
    /// </summary>
    [Fact]
    public void AllWritableProperties_AreClassifiedAsCoveredOrExcluded()
    {
        var writable = GetWritableExecutionPropertyNames();

        var unclassified = writable
            .Where(n => !Covered.ContainsKey(n) && !Excluded.ContainsKey(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unclassified.Count == 0,
            "以下可写属性既未登记到 Covered（必须拷贝），也未登记到 Excluded（明确不拷贝）：" +
            Environment.NewLine + string.Join(Environment.NewLine, unclassified) +
            Environment.NewLine +
            "若它是执行侧设置，请在 CompressSettingsViewModel.CopyExecutionSettingsFrom 里补拷贝并加进 Covered；" +
            "若只是 UI 瞬时态/派生值，请加进 Excluded 并写清理由。");

        // 注册表里不能有拼错的、或已经删掉的属性名
        var all = writable.ToHashSet(StringComparer.Ordinal);
        var staleCovered = Covered.Keys.Where(k => !all.Contains(k)).ToList();
        var staleExcluded = Excluded.Keys.Where(k => !all.Contains(k)).ToList();
        Assert.True(
            staleCovered.Count == 0 && staleExcluded.Count == 0,
            "注册表引用了不存在（或已删除/不再可写）的属性：" +
            string.Join(", ", staleCovered.Concat(staleExcluded)));
    }

    /// <summary>
    /// 守卫②：Covered 里的每个属性都要真的被 CopyExecutionSettingsFrom 拷贝过去。
    /// 用「同参数的另一个空白实例」当基线，只有当测试值确实区别于默认值时才断言（避免依赖本机 settings.json）。
    /// </summary>
    [Fact]
    public void CopyExecutionSettingsFrom_CopiesEveryCoveredProperty()
    {
        var source = new CompressSettingsViewModel(new[] { SeedPath });
        var baseline = new CompressSettingsViewModel(new[] { SeedPath });
        var target = new CompressSettingsViewModel(new[] { SeedPath });

        ApplyCoveredValues(source);
        target.CopyExecutionSettingsFrom(source);

        var failures = new List<string>();
        var proven = 0;

        foreach (var name in Covered.Keys)
        {
            var expected = Read(source, name);
            var defaultish = Read(baseline, name);
            if (Equals(defaultish, expected))
                continue; // 本机默认值恰好相同，本属性无法证明；其余属性仍然提供覆盖

            proven++;
            if (!Equals(expected, Read(target, name)))
                failures.Add($"{name}: 期望 {Describe(expected)}，实际 {Describe(Read(target, name))}");
        }

        Assert.True(failures.Count == 0, "以下执行侧设置没有被拷贝到目标 VM：" +
                                        Environment.NewLine + string.Join(Environment.NewLine, failures));
        Assert.True(proven >= 20, $"只有 {proven} 个属性得到有效验证，Covered 表里的测试值可能和默认值撞了");
    }

    /// <summary>把 Covered 里的值写到 source 上（顺序按字典声明顺序，密码相关有依赖关系）。</summary>
    private static void ApplyCoveredValues(CompressSettingsViewModel source)
    {
        foreach (var (name, value) in Covered)
        {
            if (name == nameof(CompressSettingsViewModel.SelectedPaths))
            {
                source.SelectedPaths.Clear();
                foreach (var p in (string[])value!)
                    source.SelectedPaths.Add(p);
                continue;
            }

            var prop = typeof(CompressSettingsViewModel).GetProperty(
                name, BindingFlags.Public | BindingFlags.Instance);
            prop!.SetValue(source, value);
        }
    }

    private static object? Read(CompressSettingsViewModel vm, string name)
    {
        var prop = typeof(CompressSettingsViewModel).GetProperty(
            name, BindingFlags.Public | BindingFlags.Instance)!;
        var value = prop.GetValue(vm);
        // 集合属性按内容比较（ObservableCollection 的引用永远不等）
        return value is System.Collections.IEnumerable enumerable and not string
            ? string.Join("|", enumerable.Cast<object?>().Select(o => o?.ToString()))
            : value;
    }

    private static string Describe(object? value) => value switch
    {
        null => "<null>",
        string s => $"\"{s}\"",
        _ => value.ToString() ?? "<null>",
    };

    /// <summary>枚举「执行侧可写」属性：带 public setter 的，外加只读但可增删的 SelectedPaths。</summary>
    private static List<string> GetWritableExecutionPropertyNames()
    {
        var names = typeof(CompressSettingsViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod() != null && p.GetIndexParameters().Length == 0)
            .Select(p => p.Name)
            .ToList();

        names.Add(nameof(CompressSettingsViewModel.SelectedPaths));
        return names;
    }
}
