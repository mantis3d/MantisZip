using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 压缩设置窗内嵌选择器（v0.5.5「文件选择器去弹窗化」第二步）的回归用例。
///
/// 覆盖三件事：
/// ① 六个页签都在（输出设置 / 添加文件 / 压缩设置 / 密码 / 注释 / 文件过滤）；
/// ② 两个选择器按内嵌用法配置（输出=PickFolder、源=PickItems，都是简单模式）；
/// ③ 打开「高级模式」后源选择器切回跨目录累积，且预选源文件已经灌进 VM。
///
/// 之所以用无头窗口而不是纯 VM 测试：这些行为全在 code-behind 的事件与
/// <c>_syncingSelection</c> 闸门里，VM 层看不到。
/// </summary>
public sealed class CompressSettingsWindowEmbeddedPickerTests
{
    [AvaloniaFact]
    public void Window_HasSixTabs_WithNonEmptyHeaders()
    {
        var window = CreateWindow(@"C:\data\a.txt");
        try
        {
            window.Show();
            Pump(window);

            var tabs = window.GetVisualDescendants().OfType<TabControl>().FirstOrDefault();
            Assert.NotNull(tabs);
            Assert.Equal(6, tabs!.ItemCount);

            var headers = tabs.Items.OfType<TabItem>()
                .Select(t => t.Header as string)
                .ToList();
            Assert.Equal(6, headers.Count);
            Assert.All(headers, h => Assert.False(string.IsNullOrWhiteSpace(h)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Pickers_AreConfiguredForEmbeddedUse()
    {
        var window = CreateWindow(@"C:\data\a.txt");
        try
        {
            window.Show();
            Pump(window);

            var output = window.FindControl<CustomFilePickerControl>("OutputPicker");
            var source = window.FindControl<CustomFilePickerControl>("SourcePicker");
            Assert.NotNull(output);
            Assert.NotNull(source);

            // 输出目录：PickFolder + 简单模式（当前目录选中即结果，不需要复选框）
            Assert.Equal(PickerMode.PickFolder, output!.Mode);
            Assert.True(output.IsSimpleMode);

            // 源文件：PickItems + 简单模式（默认）；勾「高级模式」后切回累积模式
            Assert.Equal(PickerMode.PickItems, source!.Mode);
            Assert.True(source.IsSimpleMode);

            var advanced = window.FindControl<CheckBox>("AdvancedModeCheck");
            Assert.NotNull(advanced);
            advanced!.IsChecked = true;
            Pump(window);
            Assert.False(source.IsSimpleMode);

            advanced.IsChecked = false;
            Pump(window);
            Assert.True(source.IsSimpleMode);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PreselectedSources_ReachBothPickerAndViewModel()
    {
        var window = CreateWindow(@"C:\data\a.txt", @"C:\data\b.txt");
        try
        {
            window.Show();
            Pump(window);

            var source = window.FindControl<CustomFilePickerControl>("SourcePicker");
            Assert.NotNull(source);

            // 初始化时 VM 里已有的源路径要灌进选择器（否则用户看不到「已选」）
            Assert.Equal(new[] { @"C:\data\a.txt", @"C:\data\b.txt" }, source!.SelectedPaths.ToArray());

            // 反向：VM 的 SelectedPaths 不应被初始化事件清空（_syncingSelection 闸门的作用）
            var vm = Assert.IsType<CompressSettingsViewModel>(window.DataContext);
            Assert.Equal(new[] { @"C:\data\a.txt", @"C:\data\b.txt" }, vm.SelectedPaths.ToArray());
        }
        finally
        {
            window.Close();
        }
    }

    private static CompressSettingsWindow CreateWindow(params string[] sourcePaths)
    {
        var context = new CompressRunContext(
            Owner: null,
            Presenter: new NullPresenter(),
            ExitPolicy: ExitPolicy.None);
        return new CompressSettingsWindow(new NullSession(), context, sourcePaths);
    }

    private static void Pump(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private sealed class NullSession : ICompressSession
    {
        public Task<CompressOutcome> RunAsync(
            CompressSettings settings,
            CompressRunContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult<CompressOutcome>(CompressOutcome.Completed);
    }

    /// <summary>
    /// PickItems 模式下宿主给定的扩展名白名单必须真的生效：解压窗「添加压缩包」只该看到压缩包，
    /// 同目录的普通文件不该出现（2026-10-30 真机发现只传了 fileExtensions 却没过滤）。
    /// </summary>
    [AvaloniaFact]
    public void PickItems_WithExtensionWhitelist_HidesOtherFiles()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mz-pick-filter-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "keep.zip"), "zip");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "skip.txt"), "txt");

            var picker = new Controls.CustomFilePickerControl
            {
                Mode = PickerMode.PickItems,
                IsSimpleMode = true,
                HideAccumulatedPanel = true
            };
            var window = new Window { Width = 800, Height = 600, Content = picker };
            window.Show();
            Pump(window);

            picker.Initialize(initialPath: dir, fileExtensions: [".zip"]);
            Pump(window);

            var list = picker.FindControl<ListBox>("FileList");
            Assert.NotNull(list);
            var names = list!.ItemsSource!.Cast<object>().OfType<FileBrowserItem>()
                .Select(i => i.DisplayName).ToList();
            Assert.Contains("keep.zip", names);
            Assert.DoesNotContain("skip.txt", names);

            window.Close();
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { /* 清理失败不影响断言 */ }
        }
    }
    private sealed class NullPresenter : IProgressPresenter
    {
        public ProgressWindow? ActiveWindow => null;

        public Task<bool> RunAsync(ProgressRunRequest request) => Task.FromResult(true);
    }
}
