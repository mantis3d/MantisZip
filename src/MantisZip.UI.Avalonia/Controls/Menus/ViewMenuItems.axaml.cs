using Avalonia.Controls;

namespace MantisZip.UI.Avalonia.Controls.Menus;

/// <summary>
/// 视图菜单子项（主题 / 进度条 / 目录基线 / 预览面板 / 信息面板 / 保存布局）。
///
/// 2026-10-22 启动优化阶段 3：自 <c>MainWindow.axaml</c> 抽出，**首次展开该菜单时才创建**。
/// 本菜单的开关项图标用到 <c>BoolToToggleBgBrushConverter</c> → 转换器已随迁到本视图的
/// <c>&lt;StackPanel.Resources&gt;</c>（静态资源查找作用域只到本视图）。
/// </summary>
public partial class ViewMenuItems : StackPanel
{
    public ViewMenuItems()
    {
        InitializeComponent();
    }
}
