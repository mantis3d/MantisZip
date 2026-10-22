using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
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

        // 对话框 owner：优先取所属窗口（主窗口），headless/无窗口时退化为 null
        var owner = TopLevel.GetTopLevel(this) as Window;

        // AppMessageBox uses static Show() — handle separately
        if (tag == "AppMessageBox")
        {
            await AppMessageBox.Show("这是一个测试消息框\n可用于测试消息弹窗的显示效果。",
                "测试", MessageBoxButton.OKCancel, MessageBoxImage.Information, owner);
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
            "CompressSettingsWindow" => new CompressSettingsWindow(),
            "ExtractSettingsWindow" => new ExtractSettingsWindow(),
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

        if (window != null && owner != null)
        {
            await window.ShowDialog(owner);
        }
    }
}
