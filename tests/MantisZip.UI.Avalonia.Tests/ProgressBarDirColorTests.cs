using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Views;
using Xunit;
using IOPath = System.IO.Path;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 「目录基线」目录条着色回归测试（2026-10-22）。
///
/// WPF 原版：进度条 Rectangle 用 <c>Style</c> + <c>DataTrigger</c>，按
/// <c>UseDirProgressColor</c> 在「文件色 / 目录色」之间切换
/// （<c>ProgressBarSizeDirBg</c> 等）；Avalonia 迁移时丢成硬编码 <c>Fill</c> →
/// 目录的条与文件的条同色，模型算出的 <c>UseDirProgressColor</c> 无人使用。
///
/// 修复：大小 / 压缩后大小两列改为**双 Rectangle 叠加**（文件色 + 目录色），
/// 用 <c>IsVisible="{Binding UseDirProgressColor}"</c> 切换（保留 DynamicResource 随主题联动）。
/// </summary>
public class ProgressBarDirColorTests
{
    private const string ViewAxamlRelPath = "src/MantisZip.UI.Avalonia/Controls/ArchiveContentView.axaml";

    /// <summary>
    /// 目录条着色：<c>UseDirProgressColor=true</c> 时目录色 Rectangle 可见、文件色不可见；
    /// 反之亦然（两条 Rectangle 互斥显示 → 同一时刻只有一个颜色生效）。
    /// </summary>
    [AvaloniaFact]
    public void SizeColumn_SwitchesBarVisibility_ByUseDirProgressColor()
    {
        var win = new MainWindow();
        var host = win.FindControl<ContentControl>("ArchiveContentHost");
        Assert.NotNull(host);

        var vm = (MainWindowViewModel)win.DataContext!;
        vm.IsArchiveLoaded = true;
        var view = Assert.IsType<ArchiveContentView>(host!.Content);

        var sizeColumn = view.FileListGridControl.Columns
            .OfType<DataGridTemplateColumn>()
            .First(c => c.CellTemplate != null && c.SortMemberPath == "Size");
        var template = (IDataTemplate)sizeColumn.CellTemplate!;

        // 目录条目 + 目录基线开启 → UseDirProgressColor = true
        var dirItem = new ArchiveItemModel { Name = "dir", IsDirectory = true, UseDirProgressColor = true };
        var fileItem = new ArchiveItemModel { Name = "file.txt", IsDirectory = false, UseDirProgressColor = false };

        var dirRects = BuildRectangles(template, dirItem);
        var fileRects = BuildRectangles(template, fileItem);

        // 两个 Rectangle（文件色 + 目录色）→ 任一时刻只有一个可见
        Assert.Equal(2, dirRects.Count);
        Assert.True(dirRects.Count(r => r.IsVisible) == 1, Describe(dirRects));
        Assert.True(fileRects.Count(r => r.IsVisible) == 1, Describe(fileRects));

        win.Close();
    }

    /// <summary>
    /// 纯文本守卫：大小 / 压缩后大小两列必须各用「文件色键」与「目录色键」两个不同的
    /// DynamicResource 键。运行时无法断言颜色值（DynamicResource 需控件在视觉树内
    /// 才能解析到 Application 资源），故用文本核对锁定「两个键不写成同一个」。
    /// </summary>
    [Fact]
    public void SizeColumns_Use_DistinctFileAndDirBrushKeys()
    {
        var text = File.ReadAllText(SourcePath(ViewAxamlRelPath));

        AssertDistinctBrushKeys(text, "ProgressBarSizeBrush", "ProgressBarSizeDirBrush");
        AssertDistinctBrushKeys(text, "ProgressBarCompressedSizeBrush", "ProgressBarCompressedSizeDirBrush");
    }

    /// <summary>断言两个 brush 键都出现（不是同一个键被写两遍）。</summary>
    private static void AssertDistinctBrushKeys(string text, string fileKey, string dirKey)
    {
        var fileCount = Regex.Matches(text, $@"DynamicResource\s+{Regex.Escape(fileKey)}\b").Count;
        var dirCount = Regex.Matches(text, $@"DynamicResource\s+{Regex.Escape(dirKey)}\b").Count;
        Assert.True(fileCount >= 1, $"{fileKey} 未在 {ViewAxamlRelPath} 中使用");
        Assert.True(dirCount >= 1, $"{dirKey} 未在 {ViewAxamlRelPath} 中使用");
    }

    /// <summary>
    /// 构建列模板并取出其中的 Rectangle（按声明顺序）。
    /// 注：<c>IDataTemplate.Build(param)</c> **不会**把 param 设为 DataContext → 需手动设置，
    /// 否则模板内的绑定不会求值（IsVisible 恒为默认值）。
    /// </summary>
    private static List<Rectangle> BuildRectangles(IDataTemplate template, ArchiveItemModel item)
    {
        var root = template.Build(item);
        Assert.NotNull(root);
        root!.DataContext = item;      // 绑定求值的前提
        root.Measure(new Size(240, 24));
        root.Arrange(new Rect(0, 0, 240, 24));
        return root.GetVisualDescendants().OfType<Rectangle>().ToList();
    }

    private static string Describe(List<Rectangle> rects) =>
        $"vis=[{string.Join(",", rects.Select(r => r.IsVisible))}] count={rects.Count}";

    /// <summary>从测试程序集输出目录向上遍历定位仓库根（含 'src' 目录的祖先）。</summary>
    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(IOPath.Combine(dir, "src")))
            dir = IOPath.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException(
            $"无法定位仓库根（祖先目录链中未找到 'src' 目录）。Base directory: {AppContext.BaseDirectory}");
    }

    private static string SourcePath(string relativePath) =>
        IOPath.Combine(GetRepoRoot(), relativePath.Replace('/', IOPath.DirectorySeparatorChar));
}
