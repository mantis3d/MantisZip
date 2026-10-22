using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 「解压设置对话框（独立 VM）→ 主窗口执行 VM」执行侧设置的**拷贝漂移守卫**。
/// <para>
/// 与 <see cref="CompressSettingsDialogCopyTests"/> 同构：这类「对话框一个 VM、执行侧一个 VM、
/// 关窗后手抄属性清单」的结构已经漏过多次（压缩侧三次：多线程/并行度恒 false、分卷大小恒 0、
/// 文件冲突策略恒「每次询问」），解压侧此前是完整的、只是没有守卫。
/// 现在清单收敛到 <see cref="ExtractSettingsViewModel.CopyExecutionSettingsFrom"/>，
/// 本测试要求 VM 的每个可写属性必须登记为 <c>Covered</c>（必须拷贝）或 <c>Excluded</c>（明确不拷贝并写理由）。
/// </para>
/// </summary>
public class ExtractSettingsDialogCopyTests
{
    private static readonly string[] SeedArchives = { "mz-copy-extract-seed.zip" };

    /// <summary>必须被拷贝的属性 → 测试用值。</summary>
    private static readonly Dictionary<string, object?> Covered = new()
    {
        [nameof(ExtractSettingsViewModel.DestinationPath)] = @"C:\mz-copy-extract-probe",
        [nameof(ExtractSettingsViewModel.ConflictAction)] = "rename",
        [nameof(ExtractSettingsViewModel.OpenFolderAfterExtract)] = true,
        [nameof(ExtractSettingsViewModel.DeleteArchiveAfterExtract)] = true,
        [nameof(ExtractSettingsViewModel.EnableParallelExtract)] = true,
        [nameof(ExtractSettingsViewModel.SmartParallelDegree)] = false,
        [nameof(ExtractSettingsViewModel.SmartSsdDegree)] = 9,
        [nameof(ExtractSettingsViewModel.SmartHddDegree)] = 7,
        [nameof(ExtractSettingsViewModel.ManualParallelDegree)] = 5,
        [nameof(ExtractSettingsViewModel.LargeArchivePreTest)] = false,
    };

    /// <summary>明确**不**拷贝的属性 → 理由。</summary>
    private static readonly Dictionary<string, string> Excluded = new()
    {
        ["SelectedConflictActionOption"] = "派生：由 ConflictAction 同步",
        ["SelectedSourceItem"] = "源压缩包列表的选中项（列表 UI 态）",
        ["IsListingPending"] = "列表异步构建中（UI 态）",
        ["PreviewErrorTitle"] = "预览 UI 状态",
        ["PreviewErrorDetail"] = "预览 UI 状态",
        ["PreviewNeedsPassword"] = "预览 UI 状态",
        ["CanUnlockSelected"] = "预览 UI 状态（派生）",
        ["PreviewRoot"] = "预览 UI 状态",
        ["PreviewCompactMode"] = "预览 UI 状态",
        ["ShowFilteredGhosts"] = "预览 UI 状态",
        ["IsPreviewBuilding"] = "预览 UI 状态",
        ["IsBuildPending"] = "预览 UI 状态",
        ["PreviewBuildProgress"] = "预览 UI 状态",
        ["BrowseFolder"] = "View 注入的委托（浏览目标目录）",
        ["CloseAction"] = "View 注入的委托（对话框关闭）",
        ["FilterProvider"] = "View 注入的委托（读取过滤条件）",
        ["ShowUnlockDialog"] = "View 注入的委托（手动解锁密码框）",
        ["FilteredEntryKeys"] = "由 View 从过滤编辑器读出后回传，调用方 showDialog 内单独赋值",
    };

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
            "若它是执行侧设置，请在 ExtractSettingsViewModel.CopyExecutionSettingsFrom 里补拷贝并加进 Covered；" +
            "若只是 UI 瞬时态/派生值，请加进 Excluded 并写清理由。");

        var all = writable.ToHashSet(StringComparer.Ordinal);
        var stale = Covered.Keys.Concat(Excluded.Keys).Where(k => !all.Contains(k)).ToList();
        Assert.True(
            stale.Count == 0,
            "注册表引用了不存在（或已不再可写）的属性：" + string.Join(", ", stale));
    }

    [Fact]
    public void CopyExecutionSettingsFrom_CopiesEveryCoveredProperty()
    {
        var source = new ExtractSettingsViewModel(SeedArchives);
        var baseline = new ExtractSettingsViewModel(SeedArchives);
        var target = new ExtractSettingsViewModel(SeedArchives);

        foreach (var (name, value) in Covered)
        {
            var prop = typeof(ExtractSettingsViewModel).GetProperty(
                name, BindingFlags.Public | BindingFlags.Instance);
            prop!.SetValue(source, value);
        }

        target.CopyExecutionSettingsFrom(source);

        var failures = new List<string>();
        var proven = 0;

        foreach (var name in Covered.Keys)
        {
            var expected = Read(source, name);
            if (Equals(Read(baseline, name), expected))
                continue; // 本机默认值恰好相同，本属性无法证明；其余属性仍提供覆盖

            proven++;
            if (!Equals(expected, Read(target, name)))
                failures.Add($"{name}: 期望 {Describe(expected)}，实际 {Describe(Read(target, name))}");
        }

        Assert.True(failures.Count == 0, "以下执行侧设置没有被拷贝到目标 VM：" +
                                        Environment.NewLine + string.Join(Environment.NewLine, failures));
        Assert.True(proven >= 8, $"只有 {proven} 个属性得到有效验证，Covered 表里的测试值可能和默认值撞了");
    }

    private static object? Read(ExtractSettingsViewModel vm, string name)
    {
        var prop = typeof(ExtractSettingsViewModel).GetProperty(
            name, BindingFlags.Public | BindingFlags.Instance)!;
        return prop.GetValue(vm);
    }

    private static string Describe(object? value) => value switch
    {
        null => "<null>",
        string s => $"\"{s}\"",
        _ => value.ToString() ?? "<null>",
    };

    private static List<string> GetWritableExecutionPropertyNames() =>
        typeof(ExtractSettingsViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod() != null && p.GetIndexParameters().Length == 0)
            .Select(p => p.Name)
            .ToList();
}
