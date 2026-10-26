using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.Views;

namespace MantisZip.UI.Avalonia.Controls.Menus;

/// <summary>
/// 开发者诊断菜单（「测试」顶级菜单 + 其 28 个子项）。
///
/// 2026-10-22 启动优化阶段 3：原先内联在 <c>MainWindow.axaml</c> 的 <c>&lt;Menu&gt;</c> 中。
/// Release 构建中 <see cref="AppConstants.ShowTestMenu"/> 为 false（菜单不可见），
/// 但这 28 个子项及其图标**仍会被实例化** → 白付启动成本。
/// 抽到独立视图后，仅在 <c>ShowTestMenu</c> 为 true 时才创建并挂进菜单（见
/// <c>MainWindow</c> 构造函数），Release 构建完全不加载这批元素。
/// </summary>
public partial class TestMenuView : UserControl
{
    public TestMenuView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 取出承载的「测试」菜单项（并从本视图解绑），供 <c>MainWindow</c> 加入主菜单。
    /// </summary>
    public MenuItem CreateMenuItem()
    {
        var item = TestMenuRoot;
        Content = null;   // 解绑：同一控件不能同时挂在两个父级上
        return item;
    }

    /// <summary>
    /// 诊断菜单各项 → 打开对应测试窗口/对话框。
    /// 2026-10-22：从 <c>MainWindow.axaml.cs</c> 迁入（随视图一起延迟加载）。
    /// </summary>
    private async void TestWindow_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not string tag)
            return;

        // 对话框 owner：**必须优先从被点击的 MenuItem 取**（取不到再退回本视图）。
        // 2026-10-26 修复（86e5139a 引入的遗留缺陷）：CreateMenuItem() 会把 TestMenuRoot 从本视图
        // 摘下（Content = null），而 TestMenuView 自身从未进入任何可视树 → `TopLevel.GetTopLevel(this)`
        // 恒为 null，于是 `owner != null` 这道判据对 28 个诊断窗口全部静默跳过，
        // 表现为「测试菜单里的窗口都打不开」。MenuItem 本身挂在主菜单上，从它取得到 MainWindow。
        var owner = ResolveOwner(mi, this);

        // AppMessageBox uses static Show() — handle separately
        if (tag == "AppMessageBox")
        {
            await AppMessageBox.Show("这是一个测试消息框\n可用于测试消息弹窗的显示效果。",
                "测试", MessageBoxButton.OKCancel, MessageBoxImage.Information, owner);
            return;
        }

        // 设置窗口属于「确认即载荷」入口：由诊断菜单直接注入会话与上下文，
        // 与主窗口/CLI 走同一条链路，而不是 new 出裸窗口再让调用方回读状态。
        if (tag is "CompressSettingsWindow" or "ExtractSettingsWindow")
        {
            await ShowSettingsEntryAsync(tag, owner);
            return;
        }

        Window? window = tag switch
        {
            "IconTestWindow" => new Dialogs.IconTestWindow(),
            "UiTestWindow" => new Views.UiTestWindow(),
            "AboutWindow" => new AboutWindow(),
            "SettingsWindow" => new Views.SettingsWindow(),
            "PasswordManagerWindow" => new PasswordManagerWindow(),
            "DonationDialog" => new DonationDialog(),
            "LogPrivacyHelpDialog" => new HelpDialog(
                LocalizationManager.T("Settings_Debug_LogPrivacyHelp_Title"),
                new MarkdownHelpContent(LocalizationManager.T("LogPrivacyHelp_Markdown")),
                LocalizationManager.T("Settings_Debug_LogPrivacyHelp_Close"))
            { Width = 520, Height = 500 },
            "PasswordHelpDialog" => new HelpDialog(
                LocalizationManager.T("PwdHelp_Title"),
                new MarkdownHelpContent(LocalizationManager.T("PasswordHelp_Markdown")),
                LocalizationManager.T("PwdHelp_Close"))
            { Width = 560, Height = 500 },
            "CommentDialog" => new CommentDialog(),
            "PasswordEditDialog" => new PasswordEditDialog(),
            "PasswordDialog" => new PasswordDialog("test.7z"),
            "ProgressWindow" => new ProgressWindow("测试进度窗口"),
            "ErrorDialog" => new ErrorDialog(new FileErrorInfo { FilePath = @"C:\test\test.zip", ErrorMessage = "这是一个测试错误信息\n可用于测试错误对话框的显示效果。" }),
            "CompressConflictDialog" => new CompressConflictDialog(@"C:\test\file.txt", "file.txt"),
            "ConflictDialog" => new ConflictDialog(new FileConflictInfo { FilePath = @"C:\existing\file.txt" }),
            "MatchedPasswordDialog" => new MatchedPasswordDialog(new PasswordEntry { Description = "测试密码", Patterns = { "*.zip" }, Password = "test123" }, "test.zip"),
            "ElevationDialog" => new ElevationDialog(new[] { @"C:\Protected\Dir" }),
            "ElevationFailedDialog" => new ElevationFailedDialog(new[] { @"C:\Protected\Dir" }),
            "ElevationInfoDialog" => new ElevationInfoDialog(new[] { @"C:\Protected\Dir" }),
            "AddFavoriteDialog" => new AddFavoriteDialog(),
            "FavoriteManagerWindow" => new FavoriteManagerWindow(),
            "ArchiveCommentDialog" => new ArchiveCommentDialog(@"C:\test.zip", ArchiveFormat.Zip, "测试注释"),
            _ => null
        };

        // 取不到 owner（headless / 无主窗口）时**降级为 Show()**，不再静默丢弃 ——
        // 诊断菜单的意义就是「点了要有反应」，静默失败会再次掩盖真实缺陷。
        if (window is null)
            return;

        if (owner is null)
        {
            window.Show();
            return;
        }

        await window.ShowDialog(owner);
    }

    /// <summary>
    /// 打开设置窗口并执行确认结果（诊断入口与主窗口共用同一条「确认即载荷 → 会话 → 结局」链路）。
    /// 取不到宿主窗口时降级为非模态 <see cref="Window.Show"/>：诊断菜单只验证界面，不真正执行。
    /// </summary>
    private static async Task ShowSettingsEntryAsync(string tag, Window? owner)
    {
        if (tag == "CompressSettingsWindow")
        {
            var session = new CompressSession();
            var context = new CompressRunContext(
                Owner: owner,
                Presenter: new MainWindowProgressPresenter(),
                ExitPolicy: ExitPolicy.None,
                IsCompressFlow: true);
            var dialog = new CompressSettingsWindow(session, context, Array.Empty<string>());
            if (owner is null)
            {
                dialog.Show();
                return;
            }

            var confirmation = await dialog.ShowDialog<ConfirmOutcome<CompressSettings>>(owner);
            if (confirmation is not Confirmed<CompressSettings> confirmed)
                return;

            var outcome = await dialog.RunConfirmedAsync(confirmed.Value);
            await ReportNotStartedAsync(outcome, owner, LocalizationManager.T("Compress_Title"));
            return;
        }

        var extractSession = new ExtractSession();
        var extractContext = new ExtractRunContext(
            Owner: owner,
            Presenter: new MainWindowProgressPresenter(),
            ExitPolicy: ExitPolicy.None);
        var extractDialog = new ExtractSettingsWindow(extractSession, extractContext, Array.Empty<string>());
        if (owner is null)
        {
            extractDialog.Show();
            return;
        }

        var extractConfirmation = await extractDialog.ShowDialog<ConfirmOutcome<ExtractSettings>>(owner);
        if (extractConfirmation is not Confirmed<ExtractSettings> extractConfirmed)
            return;

        var extractOutcome = await extractDialog.RunConfirmedAsync(extractConfirmed.Value);
        await ReportNotStartedAsync(extractOutcome, owner, LocalizationManager.T("Progress_Title_Extract"));
    }

    /// <summary>
    /// 结局展示：<c>NotStarted</c> 没有进度窗承载原因，必须显式提示；
    /// <c>Failed</c> 的错误摘要已由进度呈现器展示，不重复弹窗；取消静默。
    /// </summary>
    private static async Task ReportNotStartedAsync(RunOutcome outcome, Window owner, string title)
    {
        if (outcome.IsNotStarted)
        {
            await AppMessageBox.Show(outcome.Reason, title,
                MessageBoxButton.OK, MessageBoxImage.Warning, owner);
        }
    }

    /// <summary>
    /// 解析对话框 owner：优先从被点击的 <see cref="MenuItem"/> 取所属窗口（它挂在主窗口的
    /// <c>Menu</c> 上），取不到再退回本视图。
    ///
    /// ⚠ 顺序不能反：本视图经 <see cref="CreateMenuItem"/> 交出内容后从未进入任何可视树，
    /// 只看 <c>this</c> 会恒为 <c>null</c> —— 正是 2026-10-22 引入、2026-10-26 修复的回归根因。
    ///
    /// 子菜单项的真实可视父级是 <c>PopupRoot</c>（弹出层，不是 <see cref="Window"/>），
    /// 所以只靠 <c>TopLevel.GetTopLevel</c> 仍可能取不到窗口 —— 再沿逻辑父级回溯一层兜底：
    /// 子项 → 根「测试」菜单项 → 主菜单 → 主窗口。
    /// internal 以便回归测试直接断言解析结果（比驱动真实点击更稳定）。
    /// </summary>
    internal static Window? ResolveOwner(MenuItem menuItem, Control view)
        => TopLevel.GetTopLevel(menuItem) as Window
           ?? FindOwnerWindow(menuItem)
           ?? TopLevel.GetTopLevel(view) as Window
           ?? FindOwnerWindow(view);

    /// <summary>沿逻辑父级向上找第一个 <see cref="Window"/>（弹出层里的菜单项只能这样回溯）。</summary>
    private static Window? FindOwnerWindow(ILogical? node)
    {
        while (node is not null)
        {
            if (node is Window window)
                return window;

            node = node.LogicalParent;
        }

        return null;
    }
}
