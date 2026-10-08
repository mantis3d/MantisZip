using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Views;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 回归测试：用户反馈「打开 attachment-management-0.12.1.zip 后点击目录项闪退」
/// （反馈环境：Win11 日文版 + v0.5.0 正式版）。
///
/// 根因（由用户 lifecycle.log + 本仓库 headless 复现共同确认）：
/// <c>PreviewPanel.axaml</c> 曾把 <c>ac:NativeWebView</c> 直接声明在活动 XAML 树中。
/// NativeWebView 一旦 attach 就初始化 WebView2 Runtime，而 WebView2 创建失败
/// （Runtime 缺失 / E_ACCESSDENIED / COM 线程模型冲突）时，异常由 Avalonia 内部 Task
/// 重新抛到 Dispatcher（NativeWebView.OnAttached → Task.ThrowAsync），位于
/// ShowPreviewAsync 的 try/catch 之外 → 传到 AppDomain → 进程终止。
///
/// 由于 PreviewViewModel.ShowLoading() 对**任意**条目都会把 IsPreviewVisible 置 true，
/// 旧实现下「点任意条目」就会让预览面板可见并触发 WebView2 初始化 —— 与是否真的预览
/// HTML 无关，这正是「点目录闪退」却没有任何 ShowPreviewAsync 异常日志的原因。
///
/// 修复：NativeWebView 改为惰性创建（仅在真正预览 HTML 时才实例化）+ 创建失败降级
/// ReverseMarkdown。本类锁定该行为不再回退。
/// </summary>
public class PreviewWebViewLazyInitTests
{
    private readonly ITestOutputHelper _out;

    public PreviewWebViewLazyInitTests(ITestOutputHelper output) => _out = output;

    private const string ReproZipName = "attachment-management-0.12.1.zip";

    /// <summary>从测试程序集位置向上找仓库根，定位 TestPreview 下的真实压缩包。</summary>
    private static string RepoFile(params string[] rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "TestPreview")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName }.Concat(rel).ToArray());
    }

    /// <summary>直接 await 私有的 ShowPreviewAsync，让异常在测试里可观测
    /// （生产代码是 fire-and-forget）。</summary>
    private static async Task<Exception?> RunPreviewAsync(MainWindowViewModel vm, object entry)
    {
        var mi = typeof(MainWindowViewModel).GetMethod(
            "ShowPreviewAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ShowPreviewAsync not found");

        var task = (Task?)mi.Invoke(vm, new[] { entry });
        Assert.NotNull(task);
        try
        {
            await task!;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static void Pump()
    {
        for (int i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>反射读取 XAML 生成的 WebViewHost 容器内容：null 表示从未创建过 NativeWebView。</summary>
    private static object? GetWebViewHostContent(PreviewPanel panel)
    {
        var field = typeof(PreviewPanel).GetField(
            "WebViewHost", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        var host = field?.GetValue(panel) as ContentControl;
        return host?.Content;
    }

    private static async Task<MainWindowViewModel> LoadReproArchive()
    {
        var zipPath = RepoFile("TestPreview", ReproZipName);
        Assert.True(File.Exists(zipPath), $"zip not found: {zipPath}");
        return await LoadArchive(zipPath);
    }

    /// <summary>必须 await 而非 GetAwaiter().GetResult()：headless 下只有一个 UI 线程，
    /// LoadArchiveAsync 的 await 需要调度器推进，阻塞等待会死锁。</summary>
    private static async Task<MainWindowViewModel> LoadArchive(string archivePath)
    {
        var vm = new MainWindowViewModel();
        await vm.LoadArchiveAsync(archivePath);
        Pump();
        Assert.True(vm.IsArchiveLoaded, $"LoadArchiveAsync failed: {archivePath}");
        return vm;
    }

    /// <summary>
    /// 核心回归：预览面板首次变为可见时**不得**触发 WebView2 初始化。
    /// 旧实现在此抛 COMException / UnauthorizedAccessException（RPC_E_CHANGED_MODE / E_ACCESSDENIED）。
    /// </summary>
    [AvaloniaFact]
    public async Task PreviewPanel_FirstVisible_DoesNotInitializeWebView2()
    {
        var vm = await LoadReproArchive();

        // 显式复位到「尚未预览任何条目」状态，使阶段一真正处于隐藏态。
        // 不能依赖加载后的初始值：实测 IsPreviewVisible 已经是 True，
        // 那样阶段一实际上并不是隐藏态，「首次可见」的语义就被架空了。
        vm.Preview.Clear();
        Pump();
        Assert.False(vm.Preview.IsPreviewVisible,
            "precondition: preview must start hidden before the panel is attached");

        var panel = new PreviewPanel { DataContext = vm.Preview };
        var host = new ContentControl
        {
            Content = panel,
            // 与 MainWindow.axaml 一致：预览宿主初始不可见，由 IsPreviewVisible 驱动
            IsVisible = false,
        };
        var window = new Window { Width = 1000, Height = 700, Content = host };
        window.Show();

        // 阶段一：面板已挂载、尚未点击任何条目
        Exception? startupEx = null;
        try { Pump(); }
        catch (Exception ex) { startupEx = ex; }
        var phase1Content = GetWebViewHostContent(panel);

        // 阶段二：点击条目 → ShowLoading() 把 IsPreviewVisible 置 true → 面板变为可见
        Exception? clickEx = null;
        try
        {
            var dir = vm.CurrentEntries.First(e => e.IsDirectory);
            vm.SelectedEntry = dir;
            Pump();

            // 复刻 MainWindow.axaml 的绑定：PreviewPanelHost.IsVisible="{Binding IsPreviewVisible}"
            host.IsVisible = vm.Preview.IsPreviewVisible;
            Pump();
        }
        catch (Exception ex) { clickEx = ex; }

        _out.WriteLine($"[phase1] attached IsPreviewVisible={vm.Preview.IsPreviewVisible} " +
                       $"WebViewHost.Content={phase1Content?.GetType().Name ?? "null"} " +
                       $"threw={startupEx?.GetType().Name ?? "none"}");
        _out.WriteLine($"[phase2] visible IsPreviewVisible={vm.Preview.IsPreviewVisible} " +
                       $"PreviewType={vm.Preview.PreviewType} threw={clickEx?.GetType().Name ?? "none"}");
        _out.WriteLine($"[phase2] WebViewHost.Content={GetWebViewHostContent(panel)?.GetType().Name ?? "null"}");
        if (clickEx != null) _out.WriteLine($"[phase2] STACK: {clickEx}");

        Assert.True(startupEx == null, $"startup threw: {startupEx?.GetType().Name}: {startupEx?.Message}");
        Assert.True(clickEx == null,
            $"preview panel becoming visible must not initialize WebView2, but threw: " +
            $"{clickEx?.GetType().Name}: {clickEx?.Message}");

        // 阶段二必须真的把面板变成可见，否则本用例就退化成「什么都没发生」
        Assert.True(vm.Preview.IsPreviewVisible,
            "phase2 precondition: selecting a directory must make the preview panel visible");

        // 面板挂载期间（隐藏态）不得创建 NativeWebView
        Assert.True(phase1Content == null,
            $"no NativeWebView should exist while the panel is attached but hidden, got {phase1Content?.GetType().Name}");

        // 非 HTML 预览绝不应创建 NativeWebView
        Assert.True(GetWebViewHostContent(panel) == null,
            "no NativeWebView should be created for a non-HTML preview");

        window.Close();
        Pump();
    }

    /// <summary>目录条目：短路魔数提取，直接显示「不支持预览」，且不创建 WebView。</summary>
    [AvaloniaFact]
    public async Task SelectingDirectory_ShowsUnsupported_WithoutCreatingWebView()
    {
        var vm = await LoadReproArchive();

        var dir = vm.CurrentEntries.FirstOrDefault(e => e.IsDirectory);
        Assert.NotNull(dir);
        _out.WriteLine($"entry Name='{dir!.Name}' Size={dir.Size} IsDir={dir.IsDirectory} " +
                       $"SizeDisplay='{dir.SizeDisplay}' LastModifiedDisplay='{dir.LastModifiedDisplay}'");

        // 关键：面板必须在预览**之前**就挂载，EnsureWebViewForHtml 才有机会在属性变化时被触发。
        // 若先跑完预览再建面板，惰性创建钩子根本不会被调用，WebView 断言就成了空断言。
        var panel = new PreviewPanel { DataContext = vm.Preview };
        var window = new Window { Width = 1000, Height = 700, Content = panel };
        window.Show();
        Pump();

        var ex = await RunPreviewAsync(vm, dir);
        Pump();

        _out.WriteLine($"dir PreviewType={vm.Preview.PreviewType} " +
                       $"IsPreviewVisible={vm.Preview.IsPreviewVisible} " +
                       $"threw={ex?.GetType().Name ?? "none"} " +
                       $"WebViewHost.Content={GetWebViewHostContent(panel)?.GetType().Name ?? "null"}");
        if (ex != null) _out.WriteLine($"dir STACK: {ex}");

        Assert.True(ex == null, $"ShowPreviewAsync threw for directory entry: {ex?.GetType().Name}: {ex?.Message}");
        Assert.Equal(PreviewType.Unsupported, vm.Preview.PreviewType);
        Assert.False(vm.Preview.IsLoadingPreview, "loading overlay should be dismissed");

        // 用户报告的场景：点击目录项不得触发 WebView2 初始化（旧实现在此终止进程）
        Assert.True(GetWebViewHostContent(panel) == null,
            "clicking a directory entry must not create a NativeWebView");

        window.Close();
        Pump();
    }

    /// <summary>对照组：目录下普通文件（styles.css）同样不得创建 WebView。</summary>
    [AvaloniaFact]
    public async Task SelectingFile_ShowsCssPreview_WithoutCreatingWebView()
    {
        var vm = await LoadReproArchive();

        // 根目录下只有目录项，需要先进入该目录才能取到文件条目
        vm.NavigateToFolderPath("attachment-management");
        Pump();

        var file = vm.CurrentEntries.FirstOrDefault(e => !e.IsDirectory && e.Name.EndsWith(".css", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(file);
        _out.WriteLine($"control entry Name='{file!.Name}' IsDir={file.IsDirectory}");

        var panel = new PreviewPanel { DataContext = vm.Preview };
        var window = new Window { Width = 1000, Height = 700, Content = panel };
        window.Show();

        var ex = await RunPreviewAsync(vm, file);
        Pump();

        _out.WriteLine($"control PreviewType={vm.Preview.PreviewType} threw={ex?.GetType().Name ?? "none"} " +
                       $"WebViewHost.Content={GetWebViewHostContent(panel)?.GetType().Name ?? "null"}");

        Assert.True(ex == null, $"ShowPreviewAsync threw for file entry: {ex?.GetType().Name}: {ex?.Message}");
        Assert.True(GetWebViewHostContent(panel) == null,
            "no NativeWebView should be created for a non-HTML preview");

        window.Close();
        Pump();
    }

    /// <summary>
    /// HTML 预览在 WebView2 不可用时必须降级而不是崩溃。
    /// Headless 环境下 WebView2 创建必然失败，正好覆盖降级路径：
    /// 期望 PreviewType 仍为 Html，且异常被守卫拦截（不会冒泡成未处理异常）。
    /// </summary>
    [AvaloniaFact]
    public async Task HtmlPreview_WhenWebView2Unavailable_FallsBackWithoutCrashing()
    {
        var tmpZip = Path.Combine(Path.GetTempPath(), "MantisZip", "Tests",
                                  $"html_{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(tmpZip)!);
        try
        {
            using (var fs = File.Create(tmpZip))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("page.html");
                await using var s = entry.Open();
                await using var w = new StreamWriter(s);
                await w.WriteAsync("<html><body><h1>hello</h1></body></html>");
            }

            var vm = await LoadArchive(tmpZip);
            var html = vm.CurrentEntries.FirstOrDefault(e => e.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(html);

            var panel = new PreviewPanel { DataContext = vm.Preview };
            var window = new Window { Width = 1000, Height = 700, Content = panel };
            window.Show();

            Exception? ex = null;
            try
            {
                // 走生产路径（fire-and-forget），异常若未被守卫拦截会在 Pump 中冒泡
                vm.SelectedEntry = html!;
                for (int i = 0; i < 10; i++) Pump();
            }
            catch (Exception caught) { ex = caught; }

            _out.WriteLine($"html PreviewType={vm.Preview.PreviewType} " +
                           $"IsWebViewVisible={vm.Preview.IsWebViewVisible} " +
                           $"IsFallbackActive={vm.Preview.IsFallbackActive} " +
                           $"WebViewHost.Content={GetWebViewHostContent(panel)?.GetType().Name ?? "null"} " +
                           $"threw={ex?.GetType().Name ?? "none"}");
            if (ex != null) _out.WriteLine($"html STACK: {ex}");

            Assert.True(ex == null,
                $"WebView2 unavailable must be handled, but escaped as: {ex?.GetType().Name}: {ex?.Message}");
            Assert.Equal(PreviewType.Html, vm.Preview.PreviewType);

            // WebView2 不可用时必须真正降级到 ReverseMarkdown 控件树，而不是留下一个空白预览
            Assert.True(vm.Preview.IsFallbackActive,
                "WebView2 unavailable should fall back to the ReverseMarkdown control tree");
            Assert.False(vm.Preview.IsWebViewVisible,
                "WebView should be torn down when WebView2 is unavailable");
            Assert.True(GetWebViewHostContent(panel) == null,
                "broken NativeWebView should be removed from the host");

            window.Close();
            Pump();
        }
        finally
        {
            try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch (IOException) { /* best-effort */ }
        }
    }

    /// <summary>日文环境（用户报告的locale）：目录预览行为与其它语言一致，且不创建 WebView。</summary>
    [AvaloniaFact]
    public async Task SelectingDirectory_UnderJapaneseCulture_DoesNotCreateWebView()
    {
        var prevCulture = CultureInfo.CurrentCulture;
        var prevUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var ja = new CultureInfo("ja-JP");
            CultureInfo.CurrentCulture = ja;
            CultureInfo.CurrentUICulture = ja;

            var vm = await LoadReproArchive();
            var dir = vm.CurrentEntries.First(e => e.IsDirectory);

            var panel = new PreviewPanel { DataContext = vm.Preview };
            var window = new Window { Width = 1000, Height = 700, Content = panel };
            window.Show();

            var ex = await RunPreviewAsync(vm, dir);
            Pump();

            _out.WriteLine($"ja culture={CultureInfo.CurrentCulture.Name} PreviewType={vm.Preview.PreviewType} " +
                           $"TextContent='{vm.Preview.TextContent}' threw={ex?.GetType().Name ?? "none"} " +
                           $"WebViewHost.Content={GetWebViewHostContent(panel)?.GetType().Name ?? "null"}");

            Assert.True(ex == null, $"ja: ShowPreviewAsync threw: {ex?.GetType().Name}: {ex?.Message}");
            Assert.Equal(PreviewType.Unsupported, vm.Preview.PreviewType);
            Assert.True(GetWebViewHostContent(panel) == null,
                "no NativeWebView should be created for a non-HTML preview under ja-JP");

            window.Close();
            Pump();
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
            CultureInfo.CurrentUICulture = prevUiCulture;
        }
    }

    /// <summary>把预览面板真正挂到窗口做 measure/arrange，确认 Unsupported 状态渲染无异常。</summary>
    [AvaloniaFact]
    public async Task PreviewPanel_RendersDirectoryUnsupportedState()
    {
        var vm = await LoadReproArchive();

        var dir = vm.CurrentEntries.First(e => e.IsDirectory);

        // 先挂载面板再跑预览：否则惰性创建钩子不会被触发，本用例将失去回归价值。
        var panel = new PreviewPanel { DataContext = vm.Preview };
        var window = new Window { Width = 1000, Height = 700, Content = panel };
        window.Show();
        Pump();

        var ex = await RunPreviewAsync(vm, dir);
        Assert.True(ex == null, $"preview threw before render: {ex?.GetType().Name}: {ex?.Message}");
        Pump();

        panel.Measure(new Size(1000, 700));
        panel.Arrange(new Rect(0, 0, 1000, 700));
        Pump();

        _out.WriteLine($"render ok. Bounds={panel.Bounds} PreviewType={vm.Preview.PreviewType} " +
                       $"WebViewHost.Content={GetWebViewHostContent(panel)?.GetType().Name ?? "null"}");

        Assert.True(panel.Bounds.Width > 0 && panel.Bounds.Height > 0,
            "panel should have been measured/arranged");
        Assert.True(GetWebViewHostContent(panel) == null,
            "rendering the Unsupported state must not create a NativeWebView");

        window.Close();
        Pump();
    }
}
