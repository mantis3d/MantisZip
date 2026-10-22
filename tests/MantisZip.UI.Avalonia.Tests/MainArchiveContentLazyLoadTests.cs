using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Views;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 压缩包内容区延迟加载回归测试（2026-10-21 启动优化，阶段 2）。
///
/// 内容区（目录树 / 文件列表 DataGrid / 地址栏 / 预览面板宿主 / 拖拽覆层）共 ~153 个元素，
/// 压缩包未打开时不可见 → 改为首次加载压缩包时才实例化
/// （<c>MainWindow.EnsureArchiveContent()</c>）。本测试锁定「默认不创建、加载后创建且只创建一次」。
/// </summary>
public class MainArchiveContentLazyLoadTests
{
    [AvaloniaFact]
    public void ArchiveContent_IsNotCreated_Until_ArchiveLoaded()
    {
        var win = new MainWindow();
        var host = win.FindControl<ContentControl>("ArchiveContentHost");
        Assert.NotNull(host);
        Assert.Null(host!.Content);

        var vm = (MainWindowViewModel)win.DataContext!;
        Assert.False(vm.IsArchiveLoaded);

        // 加载压缩包 → 内容区才被创建
        vm.IsArchiveLoaded = true;

        Assert.IsType<ArchiveContentView>(host.Content);
        win.Close();
    }

    [AvaloniaFact]
    public void ArchiveContent_IsCreated_OnlyOnce()
    {
        var win = new MainWindow();
        var host = win.FindControl<ContentControl>("ArchiveContentHost");
        Assert.NotNull(host);

        var vm = (MainWindowViewModel)win.DataContext!;
        vm.IsArchiveLoaded = true;
        var first = host!.Content;

        vm.IsArchiveLoaded = false;
        vm.IsArchiveLoaded = true;

        Assert.Same(first, host.Content);
        win.Close();
    }

    [AvaloniaFact]
    public void ArchiveContentView_Contains_FileListGrid_And_PreviewHost()
    {
        var win = new MainWindow();
        var host = win.FindControl<ContentControl>("ArchiveContentHost");
        Assert.NotNull(host);

        var vm = (MainWindowViewModel)win.DataContext!;
        vm.IsArchiveLoaded = true;

        var view = Assert.IsType<ArchiveContentView>(host!.Content);
        // 内容区关键控件由视图公开（MainWindow 通过这些属性做布局数学 / 列状态 / 拖拽覆层）
        Assert.NotNull(view.FileListGridControl);
        Assert.NotNull(view.PreviewPanelHostControl);
        Assert.NotNull(view.FolderTreeBorderControl);
        Assert.NotNull(view.DragAddOverlayControl);
        win.Close();
    }

    /// <summary>
    /// **列模板构建回归**（2026-10-21 崩溃修复，运行时行为级）：
    /// DataGrid 各列的 <c>CellTemplate</c> 在**渲染出行**时才会被构建，
    /// 其中宽度/压缩率列的模板引用了 <c>RatioToWidthConverter</c>（原声明在
    /// <c>MainWindow.axaml</c> 的 <c>Window.Resources</c>）。拆成 UserControl 后静态资源
    /// 作用域只到本视图 → 打开压缩包渲染首行即抛
    /// <c>KeyNotFoundException: Static resource 'RatioToWidthConverter' not found</c>（闪退）。
    ///
    /// headless 下 DataGrid 因无主题模板而不生成行 → 直接构建各列 <c>CellTemplate</c>
    /// 复现同一条 XAML 构建路径（已实证：缺资源时抛同一异常）。
    /// </summary>
    [AvaloniaFact]
    public void ArchiveContentView_ColumnTemplates_Build_WithoutMissingStaticResource()
    {
        var win = new MainWindow();
        var host = win.FindControl<ContentControl>("ArchiveContentHost");
        Assert.NotNull(host);

        var vm = (MainWindowViewModel)win.DataContext!;
        vm.IsArchiveLoaded = true;
        var view = Assert.IsType<ArchiveContentView>(host!.Content);

        var item = new ArchiveItemModel
        {
            Name = "a.txt",
            DisplayName = "a.txt",
            FullPath = "dir/a.txt",
            Size = 1024,
            CompressedSize = 512,
        };

        var failures = new List<string>();
        foreach (var col in view.FileListGridControl.Columns.OfType<DataGridTemplateColumn>())
        {
            if (col.CellTemplate is not IDataTemplate tpl) continue;
            try
            {
                tpl.Build(item);
            }
            catch (Exception ex)
            {
                failures.Add($"{col.Header}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            $"列模板构建失败（静态资源缺失）：{string.Join(" | ", failures)}");
        win.Close();
    }

    /// <summary>
    /// **静态资源可解析性守卫**（2026-10-21 崩溃修复 / 2026-10-22 菜单视图扩展）：
    /// 拆出 UserControl 时，DataGrid 列的 CellTemplate 引用了原声明在
    /// <c>MainWindow.axaml</c> 的 <c>Window.Resources</c> 里的 <c>RatioToWidthConverter</c>；
    /// 静态资源查找作用域只到本视图 → 真正生成行时抛
    /// <c>KeyNotFoundException: Static resource 'RatioToWidthConverter' not found</c>（打开压缩包即闪退）。
    ///
    /// 该异常只在**渲染出行**时暴露（空集合时 CellTemplate 永不构建），
    /// headless 下又无法驱动 DataGrid 虚拟化行生成 → 改为纯文本守卫：
    /// 「视图引用的每个 StaticResource key，必须能在【视图局部资源】或【应用级资源】中找到」。
    /// 应用级资源 = App.axaml + 主题字典（ThemeLight/ThemeDark）+ 图标字典（AppIcons）。
    ///
    /// 同一守卫覆盖阶段 3 抽出的 4 个菜单视图（如 ViewMenuItems 的
    /// <c>BoolToToggleBgBrushConverter</c> —— 它原声明在 MainWindow 的 Window.Resources）。
    /// </summary>
    [Fact]
    public void ArchiveContentView_ReferencedStaticResources_AreResolvable()
    {
        var viewKeys = LocalResourceKeys(ViewAxamlRelPath, "UserControl.Resources");
        var appKeys = AppLevelResourceKeys();
        var viewRefs = ReferencedStaticResources(ViewAxamlRelPath);

        var missing = viewRefs
            .Where(key => !viewKeys.Contains(key) && !appKeys.Contains(key))
            .ToList();

        Assert.True(missing.Count == 0,
            $"内容区引用的静态资源既不在视图局部资源也不在应用级资源中：{string.Join(", ", missing)}");
    }

    /// <summary>
    /// 同款守卫覆盖 <c>MainWindow.axaml</c> 自身：其引用的 key 必须在
    /// <c>Window.Resources</c> 或应用级资源中（防止搬资源时误删 → MainWindow 侧同样崩溃）。
    /// </summary>
    [Fact]
    public void MainWindow_ReferencedStaticResources_AreResolvable()
    {
        var mainWindowKeys = LocalResourceKeys(MainWindowAxamlRelPath, "Window.Resources");
        var appKeys = AppLevelResourceKeys();
        var refs = ReferencedStaticResources(MainWindowAxamlRelPath);

        var missing = refs
            .Where(key => !mainWindowKeys.Contains(key) && !appKeys.Contains(key))
            .ToList();

        Assert.True(missing.Count == 0,
            $"MainWindow.axaml 引用的静态资源无法解析：{string.Join(", ", missing)}");
    }

    /// <summary>
    /// 同款守卫覆盖**阶段 3 抽出的 4 个菜单视图**（File/Edit/View/Tools MenuItems）：
    /// 它们的 <c>{StaticResource}</c> 查找作用域只到各自视图 → 局部资源必须随迁
    /// （如 ViewMenuItems 的 <c>BoolToToggleBgBrushConverter</c>）。
    /// </summary>
    [Fact]
    public void MenuViews_ReferencedStaticResources_AreResolvable()
    {
        var appKeys = AppLevelResourceKeys();
        var problems = new List<string>();

        foreach (var rel in MenuViewRelPaths)
        {
            var localKeys = LocalResourceKeys(rel, "StackPanel.Resources");
            var missing = ReferencedStaticResources(rel)
                .Where(key => !localKeys.Contains(key) && !appKeys.Contains(key))
                .ToList();
            if (missing.Count > 0)
                problems.Add($"{Path.GetFileName(rel)}: {string.Join(", ", missing)}");
        }

        Assert.True(problems.Count == 0,
            $"菜单视图引用的静态资源无法解析：{string.Join(" | ", problems)}");
    }

    // ── 辅助 ──

    private const string ViewAxamlRelPath = "src/MantisZip.UI.Avalonia/Controls/ArchiveContentView.axaml";
    private const string MainWindowAxamlRelPath = "src/MantisZip.UI.Avalonia/Views/MainWindow.axaml";

    /// <summary>阶段 3 抽出的菜单视图（局部资源作用域各自独立）。</summary>
    private static readonly string[] MenuViewRelPaths =
    {
        "src/MantisZip.UI.Avalonia/Controls/Menus/FileMenuItems.axaml",
        "src/MantisZip.UI.Avalonia/Controls/Menus/EditMenuItems.axaml",
        "src/MantisZip.UI.Avalonia/Controls/Menus/ViewMenuItems.axaml",
        "src/MantisZip.UI.Avalonia/Controls/Menus/ToolsMenuItems.axaml",
    };

    /// <summary>应用级资源字典（App.axaml 内联 + 主题 + 图标）里声明的全部 x:Key。</summary>
    private static HashSet<string> AppLevelResourceKeys()
    {
        var keys = new HashSet<string>();
        foreach (var rel in new[]
                 {
                     "src/MantisZip.UI.Avalonia/App.axaml",
                     "src/MantisZip.UI.Avalonia/Themes/ThemeLight.axaml",
                     "src/MantisZip.UI.Avalonia/Themes/ThemeDark.axaml",
                     "src/MantisZip.UI.Avalonia/Resources/Icons/AppIcons.axaml",
                 })
        {
            var path = SourcePath(rel);
            if (!File.Exists(path)) continue;
            var text = File.ReadAllText(path);
            foreach (Match m in Regex.Matches(text, @"x:Key\s*=\s*""([^""]+)"""))
                keys.Add(m.Groups[1].Value);
        }
        return keys;
    }

    /// <summary>提取 XAML 中某个资源字典段（如 <c>Window.Resources</c>）里声明的全部 x:Key。</summary>
    private static List<string> LocalResourceKeys(string relativePath, string dictionaryTag)
    {
        var text = File.ReadAllText(SourcePath(relativePath));
        var open = $"<{dictionaryTag}>";
        var close = $"</{dictionaryTag}>";
        var start = text.IndexOf(open, StringComparison.Ordinal);
        if (start < 0) return new List<string>();
        var end = text.IndexOf(close, start, StringComparison.Ordinal);
        var section = end < 0 ? text[start..] : text[start..end];

        return Regex.Matches(section, @"x:Key\s*=\s*""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
    }

    /// <summary>提取 XAML 中引用的全部 <c>{StaticResource key}</c>（去重）。</summary>
    private static List<string> ReferencedStaticResources(string relativePath)
    {
        var text = File.ReadAllText(SourcePath(relativePath));
        return Regex.Matches(text, @"\{StaticResource\s+([A-Za-z_][A-Za-z0-9_.]*)\}")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
    }

    /// <summary>从测试程序集输出目录向上遍历定位仓库根（含 'src' 目录的祖先）。</summary>
    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException(
            $"无法定位仓库根（祖先目录链中未找到 'src' 目录）。Base directory: {AppContext.BaseDirectory}");
    }

    private static string SourcePath(string relativePath) =>
        Path.Combine(GetRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
}



