// ============================================================================
// PickerOptionsRegionTests.cs — CustomFilePickerDialog 通用参数区（OptionsRow）契约测试
//
// 锁定「参数注册表驱动参数区」的四条契约：
//   1. ExtractFolder 模式注册 preserveFullPath 且初值写入选项项（回归锁：初值漏播种 bug）
//   2. 档案根目录（currentFolder 为空）时禁用开关（两设置输出相同 → 禁用决策）
//   3. 位于子目录时开关可用
//   4. 不注册任何参数的模式 → 空注册表整区隐藏（规则 6）
//   5. 注册任一参数后参数区显示
//
// 构造注意：对话框构造函数做真实工作（InitializeComponent / NavigateTo / IconService /
// AppSettings.Load），XAML 内有 {StaticResource IconXxx} 引用 AppIcons 资源，故须先
// 把 AppIcons 合并进无头测试的 Application（同 ResultTreeViewFilterTests.EnsureIconResources）。
// ============================================================================
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Dialogs;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// CustomFilePickerDialog 参数区（OptionsRow）结构契约测试：
/// 注册表驱动渲染、初值播种、根目录禁用、空注册表整区隐藏。
/// </summary>
public class PickerOptionsRegionTests
{
    /// <summary>AppIcons 合并锁（测试类可能并行执行，Application.Current 为共享单例）。</summary>
    private static readonly object IconMergeLock = new();
    private static bool _iconsMerged;

    /// <summary>
    /// 将 AppIcons.axaml 资源合并进无头测试的 Application。
    /// 对话框 XAML 的 {StaticResource IconChevronLeft} 等 8 处引用必须在构造前可解析，
    /// 否则 InitializeComponent 抛「Unable to find resource」。
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

    /// <summary>创建真实临时目录（构造函数的 NavigateTo / ResolveInitialPath 需要存在的路径）。</summary>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "mantiszip_picker_opts_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>删除临时目录（best-effort，清理失败不影响测试结论）。</summary>
    private static void DeleteTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // 清理失败不影响断言结果
        }
    }

    /// <summary>示例压缩包条目：docs/ 子目录下两个文件（用于 ExtractFolder 模式）。</summary>
    private static List<ArchiveItem> CreateSampleEntries() =>
    [
        new ArchiveItem { Name = "readme.txt", FullPath = "docs/readme.txt", Size = 100, IsDirectory = false },
        new ArchiveItem { Name = "logo.png", FullPath = "docs/img/logo.png", Size = 200, IsDirectory = false },
    ];

    /// <summary>
    /// 契约 1（回归锁）：ExtractFolder 模式须注册唯一参数 preserveFullPath，
    /// 且调用方传入的初值（true）必须写入选项项的 IsChecked —— 锁定「初值漏播种」
    /// 历史 bug（复选框显示未勾选而真实值为已勾选）。
    /// 同时断言 SelectedPreserveFullPath 与选项项一致（字段与注册表不脱节）。
    /// </summary>
    [AvaloniaFact]
    public void ExtractFolder_RegistersPreserveFullPath_WithInitialValue()
    {
        EnsureIconResources();
        var tempDir = CreateTempDirectory();
        try
        {
            var dialog = new CustomFilePickerDialog(
                PickerMode.ExtractFolder, CreateSampleEntries(), null, tempDir, null,
                currentFolder: "docs", preserveFullPath: true);

            var option = Assert.Single(dialog.Options);
            Assert.Equal("preserveFullPath", option.Key);
            Assert.True(option.IsChecked,
                "preserveFullPath 选项的 IsChecked 必须播种为调用方传入的初值 true（初值漏播种回归锁）");
            Assert.True(dialog.SelectedPreserveFullPath,
                "SelectedPreserveFullPath 字段值须与选项项 IsChecked 一致");
        }
        finally
        {
            DeleteTempDirectory(tempDir);
        }
    }

    /// <summary>
    /// 契约 2：currentFolder 为空（压缩包根目录）时，「保留完整路径」无前缀可裁、
    /// 两模式输出完全相同 → 参数项禁用且带禁用提示，IsPreserveFullPathToggleAvailable 为 false。
    /// </summary>
    [AvaloniaFact]
    public void ExtractFolder_RegistersOptionDisabled_AtArchiveRoot()
    {
        EnsureIconResources();
        var tempDir = CreateTempDirectory();
        try
        {
            var dialog = new CustomFilePickerDialog(
                PickerMode.ExtractFolder, CreateSampleEntries(), null, tempDir, null,
                currentFolder: "", preserveFullPath: true);

            var option = Assert.Single(dialog.Options);
            Assert.False(option.IsEnabled,
                "压缩包根目录时保留完整路径开关应禁用（两设置输出相同）");
            Assert.False(string.IsNullOrWhiteSpace(option.DisabledHint),
                "禁用态必须携带非空的禁用提示文案");
            Assert.False(dialog.IsPreserveFullPathToggleAvailable,
                "根目录时 IsPreserveFullPathToggleAvailable 应为 false");
        }
        finally
        {
            DeleteTempDirectory(tempDir);
        }
    }

    /// <summary>
    /// 契约 3：currentFolder 指向子目录（如 "docs"）时存在前缀可裁，
    /// 参数项可用且 IsPreserveFullPathToggleAvailable 为 true。
    /// </summary>
    [AvaloniaFact]
    public void ExtractFolder_RegistersOptionEnabled_WhenInsideSubfolder()
    {
        EnsureIconResources();
        var tempDir = CreateTempDirectory();
        try
        {
            var dialog = new CustomFilePickerDialog(
                PickerMode.ExtractFolder, CreateSampleEntries(), null, tempDir, null,
                currentFolder: "docs", preserveFullPath: true);

            var option = Assert.Single(dialog.Options);
            Assert.True(option.IsEnabled,
                "位于子目录时保留完整路径开关应可用");
            Assert.True(dialog.IsPreserveFullPathToggleAvailable,
                "位于子目录时 IsPreserveFullPathToggleAvailable 应为 true");
        }
        finally
        {
            DeleteTempDirectory(tempDir);
        }
    }

    /// <summary>
    /// 契约 4（规则 6）：PickFolder / SaveFile / OpenFile / PickItems 均不注册任何参数
    /// → 注册表为空 → OptionsRow 整区隐藏（不占布局空间）。
    /// SaveFile / OpenFile 走文件名区初始化（defaultExtension=".zip"），PickFolder / PickItems 仅需初始路径。
    /// </summary>
    [AvaloniaFact]
    public void OptionsRow_IsHidden_ForModesThatRegisterNoOptions()
    {
        EnsureIconResources();

        // SaveFile 模式需要 defaultExtension 以初始化文件类型下拉与文件名预填
        var cases = new (PickerMode Mode, string? DefaultExtension)[]
        {
            (PickerMode.PickFolder, null),
            (PickerMode.SaveFile, ".zip"),
            (PickerMode.OpenFile, null),
            (PickerMode.PickItems, null),
        };

        foreach (var (mode, defaultExtension) in cases)
        {
            var tempDir = CreateTempDirectory();
            try
            {
                var dialog = new CustomFilePickerDialog(
                    mode, entries: null, defaultExtension, tempDir, fileExtensions: null);

                Assert.Empty(dialog.Options);
                Assert.False(dialog.OptionsRow.IsVisible,
                    $"{mode} 模式不应注册参数，空注册表时 OptionsRow 须整区隐藏");
            }
            finally
            {
                DeleteTempDirectory(tempDir);
            }
        }
    }

    /// <summary>
    /// 契约 5：ExtractFolder 注册首个参数（AddOption）后，OptionsRow 由 XAML 默认的
    /// IsVisible="False" 翻转为 true —— 锁定「注册即显示」的接线。
    /// </summary>
    [AvaloniaFact]
    public void OptionsRow_IsVisible_AfterFirstAddOption()
    {
        EnsureIconResources();
        var tempDir = CreateTempDirectory();
        try
        {
            var dialog = new CustomFilePickerDialog(
                PickerMode.ExtractFolder, CreateSampleEntries(), null, tempDir, null,
                currentFolder: "docs", preserveFullPath: true);

            Assert.True(dialog.OptionsRow.IsVisible,
                "注册任一参数后 OptionsRow 应显示");
        }
        finally
        {
            DeleteTempDirectory(tempDir);
        }
    }
}
