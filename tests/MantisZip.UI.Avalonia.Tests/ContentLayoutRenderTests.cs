using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Views;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 内容布局行渲染回归（2026-11-07 崩溃，thumbnail-card-badge-and-row-bars T4 引入）。
///
/// 现象：打开压缩包 → 切「内容」模式崩溃（lifecycle.log：DOMAIN EXCEPTION terminating=True
/// InvalidCastException "Unable to cast object of type 'System.Double' to type
/// 'Avalonia.Controls.GridLength'"，栈在 Grid.MeasureCell / DynamicResourceExpression 重求值）。
/// 根因：Content 元数据行把 <c>ColumnDefinition.Width="{DynamicResource ContentRowLabelColumnWidth}"</c>
/// 绑到 <c>sys:Double</c> 资源——ColumnDefinition.Width 是 GridLength 类型，行挂树时
/// （ApplyTemplate → SetInheritanceParent → 主题变体继承传播）同一绑定重推值，
/// double 强转 GridLength 抛异常 → 渲染线程终止、整进程退出。
///
/// headless 里 ItemsControl 项**不会物化**（仓库既有惯例，见 MainArchiveContentLazyLoadTests 注释），
/// 直接从真实 view 拿 Content 布局的 ItemTemplate 构建行、宿主窗口合并 view 级资源字典
/// （StaticResource/DynamicResource 解析路径与生产一致），泵布局后翻转主题变体触发同一继承
/// 传播 → 修复前应复现同款 InvalidCastException，修复后完整布局无异常。用例即钉这两点。
/// </summary>
public class ContentLayoutRenderTests
{
    [AvaloniaFact]
    public void ContentRowTemplate_BuildsAndMeasures_WithoutLayoutException()
    {
        var win = new MainWindow();
        var host = win.FindControl<ContentControl>("ArchiveContentHost");
        Assert.NotNull(host);

        var vm = (MainWindowViewModel)win.DataContext!;
        vm.IsArchiveLoaded = true;
        vm.LayoutMode = FileListLayoutMode.Content;
        vm.CurrentEntries.Add(new ArchiveItemModel { Name = "a.txt", Size = 1024, CompressedSize = 512 });
        vm.CurrentEntries.Add(new ArchiveItemModel { Name = "b.log", Size = 2048, CompressedSize = 1024,
            LastModified = new DateTime(2026, 11, 7) });

        var view = Assert.IsType<ArchiveContentView>(host!.Content);
        var sv = Assert.IsAssignableFrom<ScrollViewer>(view.FindControl<ScrollViewer>("ContentLayoutScrollViewer"));
        var itemsControl = Assert.IsType<ItemsControl>(sv!.Content);   // 逻辑树直取，不依赖视觉树物化时序
        var template = Assert.IsAssignableFrom<IDataTemplate>(itemsControl!.ItemTemplate);

        // 宿主窗口拷贝 view 级资源（ResourceDictionary 不可多 owner，不能整本塞 MergedDictionaries；
        // 本 view 的资源全是平铺项，逐键浅拷贝即可——键值不改写，解析路径与生产一致）
        var hostWin = new Window { Width = 800, Height = 600 };
        foreach (var (key, value) in view.Resources)
        {
            hostWin.Resources[key] = value;
        }

        var row = template.Build(vm.CurrentEntries[1])!;
        hostWin.Content = row;
        hostWin.Show();

        // 泵一轮布局，再翻转主题变体触发 DynamicResource 的继承传播重求值（生产崩溃触发器）
        Exception? caught = TryRender(hostWin, () => hostWin.RequestedThemeVariant = ThemeVariant.Dark);

        Assert.Null(caught);

        hostWin.Close();
        win.Close();
    }

    /// <summary>
    /// 源码守卫（同事故，2026-11-07）：全仓 .axaml 禁止在 **GridLength 型属性**上绑
    /// <c>{DynamicResource}</c>——RowDefinition / ColumnDefinition 的 Width / Height
    /// 是 GridLength，而现有资源体系里这类目标键全是 <c>sys:Double</c>（规则 5 同源问题：
    /// double/Thickness 之外的第三组不互认类型），挂树重求值时强转抛 InvalidCastException
    /// 整进程崩溃且无编译期报警。若确需给 GridLength 排资源，必须新声明 GridLength 型键
    /// 并对本用例豁免注解。
    /// </summary>
    [Fact]
    public void Axaml_ContainsNo_GridLengthProperties_BoundToDynamicResource()
    {
        var repoRoot = AppContext.BaseDirectory;
        while (repoRoot is not null && !Directory.Exists(Path.Combine(repoRoot, "src")))
        {
            repoRoot = Path.GetDirectoryName(repoRoot);
        }
        if (repoRoot is null)
        {
            throw new InvalidOperationException($"无法定位仓库根目录（未找到 'src' 目录；Base: {AppContext.BaseDirectory}）");
        }

        var pattern = "(RowDefinition|ColumnDefinition)[^>\\r\\n]*?(Width|Height)=\"\\{DynamicResource";
        var offenders = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src"), "*.axaml", SearchOption.AllDirectories)
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.Text, pattern))
            .Select(x => x.Path)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "GridLength 属性绑了 DynamicResource（崩溃模式）：\n" + string.Join("\n", offenders));
    }

    /// <summary>常规布局若干轮 → 中途动作（如翻主题变体）→ 再布局若干轮；任何一轮抛异常即捕获返回。</summary>
    private static Exception? TryRender(Window win, Action? midAction = null)
    {
        try
        {
            for (var i = 0; i < 4; i++)
            {
                Dispatcher.UIThread.RunJobs();
                win.Measure(new Size(800, 600));
                win.Arrange(new Rect(0, 0, 800, 600));
                win.UpdateLayout();
            }
            midAction?.Invoke();
            for (var i = 0; i < 4; i++)
            {
                Dispatcher.UIThread.RunJobs();
                win.Measure(new Size(800, 600));
                win.Arrange(new Rect(0, 0, 800, 600));
                win.UpdateLayout();
            }
        }
        catch (Exception e)
        {
            return e;
        }
        return null;
    }
}
