using Avalonia.Controls;

namespace MantisZip.UI.Avalonia.Controls.Menus;

/// <summary>
/// 工具菜单子项（密码管理器 / 收藏管理器 / 设置）。
///
/// 2026-10-22 启动优化阶段 3：自 <c>MainWindow.axaml</c> 抽出，**首次展开该菜单时才创建**。
/// </summary>
public partial class ToolsMenuItems : StackPanel
{
    public ToolsMenuItems()
    {
        InitializeComponent();
    }
}
