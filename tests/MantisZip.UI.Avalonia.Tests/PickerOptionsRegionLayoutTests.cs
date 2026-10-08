using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Dialogs;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 参数区<b>布局约束</b>与<b>禁用提示配置</b>的回归锁 —— 覆盖 Oracle 架构评审判定的两个最高危缺陷：
/// <list type="bullet">
/// <item><b>H1</b>：参数项容器若用横向 <c>StackPanel</c>，子项会拿到<b>无穷宽度</b>，
/// 导致 <c>WrapPanel</c> 永不换行、整区溢出窗口。本测试用<b>真实布局测量</b>验证参数项
/// 实际拿到的宽度是有限且不溢出。</item>
/// <item><b>D3</b>：禁用态 CheckBox 的 ToolTip 在 Avalonia 中默认永不弹出（禁用控件不派发指针事件），
/// 必须置 <c>ToolTip.ShowOnDisabled=True</c>。本测试在<b>源码层</b>验证该属性确实存在于参数项
/// 模板中 —— 属性名拼错会被 XAML 编译器<b>静默忽略</b>，这正是它最危险的失败方式。</item>
/// </list>
/// 自动化覆盖到「宽度已约束 / 属性已正确书写」这一层；
/// <b>悬停时提示框是否真的弹出、换行后视觉是否美观</b>属渲染行为，仍需 GUI 目视验收（计划 §7 第 12 / 20 项）。
/// </summary>
/// <remarks>
/// D3 之所以用源码断言而非视觉树查找：headless 下 <c>ItemsControl</c> 的 item 容器
/// （即 ItemTemplate 里的 CheckBox）不会被物化 —— 已实测 <c>Measure/Arrange</c> 与
/// <c>Show()</c> 均无法使其出现在视觉树中，故视觉树路线不可行。
/// </remarks>
public class PickerOptionsRegionLayoutTests
{
    private static readonly object IconMergeLock = new();
    private static bool _iconsMerged;

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

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mz_pfp_layout_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static CustomFilePickerDialog CreateDialog(string currentFolder)
    {
        EnsureIconResources();
        var tempDir = CreateTempDirectory();
        var dialog = new CustomFilePickerDialog(
            PickerMode.ExtractFolder,
            [
                new ArchiveItem { Name = "readme.txt", FullPath = "docs/readme.txt", Size = 1, IsDirectory = false },
                new ArchiveItem { Name = "logo.png", FullPath = "docs/img/logo.png", Size = 2, IsDirectory = false },
            ],
            null, tempDir, null,
            currentFolder: currentFolder, preserveFullPath: true);
        dialog.Measure(new Size(1100, 620));
        dialog.Arrange(new Rect(0, 0, 1100, 620));
        return dialog;
    }

    /// <summary>
    /// H1 锁：参数项容器实际拿到的宽度必须<b>有限且不溢出</b>其父容器。
    /// 若回退成横向 StackPanel，ItemsControl 会拿到无穷宽度并撑破窗口边界
    /// —— 这是<b>运行时布局测量</b>，不是文本断言。
    /// </summary>
    [AvaloniaFact]
    public void OptionsItemsControl_GetsFiniteWidth_AndDoesNotOverflow()
    {
        var dialog = CreateDialog(currentFolder: "docs");
        try
        {
var itemsControl = FindOptionsItemsControl(dialog);
            var optionsRow = dialog.OptionsRow;
            Assert.NotNull(optionsRow);

            var w = itemsControl.Bounds.Width;
            var parentW = optionsRow!.Bounds.Width;

            Assert.True(double.IsFinite(w),
                $"参数项容器宽度必须有限，实际 {w} —— 无穷宽度会使 WrapPanel 无法换行（H1）");
            Assert.True(w <= parentW + 1,
                $"参数项容器宽度 {w} 溢出父容器 {parentW} —— 会出现横向裁剪或撑破窗口（H1）");
        }
        finally
        {
            dialog.Close();
        }
    }

    /// <summary>
    /// H1 锁（容器形态）：参数区的内容容器必须是 <c>Grid ColumnDefinitions="Auto,*"</c>。
    /// 横向 <c>StackPanel</c> 沿 orientation 方向会给子项无穷宽度，是 H1 缺陷的<b>根因形态</b>。
    /// </summary>
    /// <remarks>
    /// 只做正向断言：文件里 <c>PickItemsPanel</c> 的按钮行<b>本来就合法地</b>使用横向
    /// StackPanel，用「文件内不得出现 Orientation="Horizontal"」这种反向断言会误报。
    /// H1 的行为级覆盖由 <see cref="OptionsItemsControl_GetsFiniteWidth_AndDoesNotOverflow"/>
    /// 的运行时宽度测量承担。
    /// </remarks>
    [AvaloniaFact]
    public void OptionsRegion_ContainerIsGrid_NotHorizontalStackPanel()
    {
        var xaml = ReadDialogXaml();

        // 参数区 Border 内必须有一个 Auto,* 的 Grid 作为内容容器
        Assert.Contains("ColumnDefinitions=\"Auto,*\"", xaml);
        // 参数项面板用 WrapPanel（多参数时换行）
        Assert.Contains("<WrapPanel Orientation=\"Horizontal\" />", xaml);
    }

    /// <summary>
    /// D3 锁：参数项 CheckBox 必须声明 <c>ToolTip.ShowOnDisabled="True"</c>，
    /// 否则禁用态（压缩包根目录）提示永不弹出，用户只看到一个死复选框。
    /// </summary>
    [AvaloniaFact]
    public void OptionCheckBox_DeclaresShowOnDisabled_InXaml()
    {
        var xaml = ReadDialogXaml();

        Assert.Contains("ToolTip.ShowOnDisabled=\"True\"", xaml);
        // 提示文案绑定到参数项的 DisabledHint（null 时 ToolTip 服务不打开）
        Assert.Contains("ToolTip.Tip=\"{Binding DisabledHint}\"", xaml);
    }

    /// <summary>
    /// D3 锁（<b>运行时行为</b>，比源码断言强）：用真实 <see cref="PickerOptionItem"/> 实例化参数项
    /// <see cref="ItemsControl.ItemTemplate"/>，断言生成的 CheckBox 确实带
    /// <c>ToolTip.ShowOnDisabled=True</c>，且 <c>ToolTip.Tip</c> 已解析为该参数的禁用提示。
    /// </summary>
    /// <remarks>
    /// 走模板实例化而非源码比对，是为了覆盖「XAML 属性名拼写正确但绑定未生效」这类只有运行时
    /// 才暴露的问题；同时绕开了 headless 下 ItemsControl item 容器不物化的限制
    /// （<see cref="OptionsItemsControl_GetsFiniteWidth_AndDoesNotOverflow"/> 只能拿到 ItemsControl 本身）。
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]    // 禁用 + 有提示 → 提示须存在
    [InlineData(false)]   // 可用 → 提示存在但控件可交互（ShowOnDisabled 仍须为 true）
    public void OptionItemTemplate_YieldsCheckBox_WithShowOnDisabledAndResolvedTip(bool disabled)
    {
        var dialog = CreateDialog(currentFolder: disabled ? "" : "docs");
        try
        {
var itemsControl = FindOptionsItemsControl(dialog);
            var template = itemsControl.ItemTemplate;
            Assert.NotNull(template);

            var option = new PickerOptionItem
            {
                Key = "preserveFullPath",
                Label = "保留完整路径",
                IsChecked = true,
                IsEnabled = !disabled,
                DisabledHint = "在压缩包根目录，勾选与否结果相同",
            };

var presenter = new ContentPresenter
            {
                Content = option,
                ContentTemplate = template,
                Width = 220,
                Height = 30,
            };
            // 必须显式 UpdateChild()：ContentPresenter 未附加到可视树时不会自动套用
            // ContentTemplate，Measure 之前子控件尚未生成。
            presenter.UpdateChild();
            presenter.Measure(new Size(220, 30));
            presenter.Arrange(new Rect(0, 0, 220, 30));

            var checkBox = presenter.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault();
            Assert.NotNull(checkBox);

            // D3：缺这一行，禁用态提示永不弹出
            Assert.True(ToolTip.GetShowOnDisabled(checkBox!),
                "ItemTemplate 生成的 CheckBox 必须带 ToolTip.ShowOnDisabled=True（D3 回归锁）");

            // Tip 绑定须解析为 DisabledHint 的实际文案，而非绑定表达式本身
            var tip = ToolTip.GetTip(checkBox!);
            Assert.NotNull(tip);
            var tipText = tip as string ?? tip!.ToString();
            Assert.Equal("在压缩包根目录，勾选与否结果相同", tipText);

            // 可用性须正确传导到生成的控件
            Assert.Equal(!disabled, checkBox!.IsEnabled);
        }
        finally
        {
            dialog.Close();
        }
    }

    /// <summary>
    /// D3 锁（反向）：<c>DisabledHint</c> 为 null 时 ToolTip 服务不应打开 ——
    /// 保证「可用时不显示提示」，避免给正常可用的参数也挂一个空提示框。
    /// </summary>
    [AvaloniaFact]
    public void OptionItemTemplate_NullDisabledHint_ProducesNoTooltipContent()
    {
        var dialog = CreateDialog(currentFolder: "docs");
        try
        {
var itemsControl = FindOptionsItemsControl(dialog);
            var template = itemsControl.ItemTemplate;
            Assert.NotNull(template);

            var option = new PickerOptionItem
            {
                Key = "preserveFullPath",
                Label = "保留完整路径",
                IsChecked = true,
                IsEnabled = true,
                DisabledHint = null,      // 可用参数不应带提示
            };

var presenter = new ContentPresenter { Content = option, ContentTemplate = template };
            presenter.UpdateChild();
            presenter.Measure(new Size(220, 30));
            presenter.Arrange(new Rect(0, 0, 220, 30));

            var checkBox = presenter.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault();
            Assert.NotNull(checkBox);
            // null Tip → Avalonia ToolTip 服务不打开（不会给可用参数挂空提示）
            Assert.True(ToolTip.GetTip(checkBox!) is null || ToolTip.GetTip(checkBox!) is string { Length: 0 },
                "DisabledHint 为 null 时不得产生提示内容");
        }
        finally
        {
            dialog.Close();
        }
    }

    /// <summary>
    /// 在参数区子树内定位参数项容器 <c>ItemsControl</c>。
    /// </summary>
    /// <remarks>
    /// 必须按 <see cref="CustomFilePickerDialog.OptionsRow"/> 作用域定位：对话框里还有
    /// <c>PickItemsPanel</c> 的 <c>AccumulatedItemsControl</c>，无作用域的
    /// <c>GetVisualDescendants().OfType&lt;ItemsControl&gt;().FirstOrDefault()</c> 会抓到它
    /// （其 <c>ItemTemplate</c> 为 null），导致断言对象错位。
    /// </remarks>
private static ItemsControl FindOptionsItemsControl(CustomFilePickerDialog dialog)
    {
        Assert.NotNull(dialog.OptionsRow);
        var all = dialog.OptionsRow!.GetVisualDescendants().OfType<ItemsControl>().ToList();
        Assert.True(all.Count > 0,
            $"参数区子树内未找到任何 ItemsControl（OptionsRow 子树类型：{string.Join(", ", dialog.OptionsRow!.GetVisualDescendants().Select(v => v.GetType().Name).Distinct().Take(12))}）");
        return all[0];
    }

    /// <summary>
    /// D3 端到端锁：把参数项挂进真实对话框后模拟鼠标悬停，断言 <b>ToolTip 真的打开</b>。
    /// </summary>
    /// <remarks>
    /// 这是 DoD 12 的完整验证：前两条只证明「属性已正确应用到生成的控件」，
    /// 本条证明「Avalonia 的 ToolTip 服务在禁用控件上确实会响应悬停」——
    /// 即 D3 的诊断（禁用控件不派发指针事件 → 提示永不弹出）与修法
    /// （<c>ToolTip.ShowOnDisabled=True</c>）在真实输入管线上成立。
    /// </remarks>
    [AvaloniaFact(Skip = "headless Avalonia 不派发 hover/pointer-over 事件，ToolTip 无法在无桌面会话中打开；" +
                          "需实机 GUI 验收（计划 §7 DoD 12）。自动化已覆盖的部分见 " +
                          "OptionItemTemplate_YieldsCheckBox_WithShowOnDisabledAndResolvedTip。")]
    public async Task DisabledOptionCheckBox_ShowsToolTip_OnHover()
    {
        var dialog = CreateDialog(currentFolder: "");   // 根目录 → 参数被禁用
        try
        {
            var itemsControl = FindOptionsItemsControl(dialog);
            var template = itemsControl.ItemTemplate;
            Assert.NotNull(template);

            var option = new PickerOptionItem
            {
                Key = "preserveFullPath",
                Label = "保留完整路径",
                IsChecked = true,
                IsEnabled = false,                                  // 禁用态
                DisabledHint = "在压缩包根目录，勾选与否结果相同",
            };

            // headless 下 ItemsControl 的 item 容器不物化，故手工套模板后挂进参数区
            var presenter = new ContentPresenter { Content = option, ContentTemplate = template };
            presenter.UpdateChild();
            presenter.Measure(new Size(220, 30));
            presenter.Arrange(new Rect(0, 0, 220, 30));

            var checkBox = presenter.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault();
            Assert.NotNull(checkBox);
            Assert.False(checkBox!.IsEnabled);                     // 确认为禁用态
            ToolTip.SetShowDelay(checkBox, 0);                // 免等待（单位 ms）

            var host = (Panel)dialog.OptionsRow!.GetVisualDescendants()
                .OfType<Grid>().First(g => g.ColumnDefinitions.Count > 0);
            host.Children.Add(presenter);
            host.Children.Remove(itemsControl);

            dialog.Show();
            dialog.UpdateLayout();

            var bounds = checkBox.TranslatePoint(new Point(0, 0), dialog)
                         ?? new Point(10, 10);
            var target = new Point(
                bounds.X + Math.Max(1, checkBox.Bounds.Width / 2),
                bounds.Y + Math.Max(1, checkBox.Bounds.Height / 2));

            dialog.MouseMove(target);
            Dispatcher.UIThread.RunJobs();

            Assert.True(ToolTip.GetIsOpen(checkBox),
                "悬停禁用态参数复选框时 ToolTip 应打开（ShowOnDisabled 已生效）");
            await Task.CompletedTask;
        }
        finally
        {
            dialog.Close();
        }
    }

    /// <summary>读取对话框 AXAML 源码（归一化空白，避免换行格式影响匹配）。</summary>
    private static string ReadDialogXaml() =>
        System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "MantisZip.UI.Avalonia",
                "Dialogs", "CustomFilePickerDialog.axaml")),
            @"\s+", " ");

    /// <summary>仓库根发现：自测试程序集输出目录上溯，直到找到含 <c>src</c> 的目录。
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
