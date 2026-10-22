using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Controls.Menus;

/// <summary>
/// 编辑菜单子项（解压 / 原地解压 / 解压到同名文件夹 / 智能解压 / 添加 / 删除 / 测试 / 注释 / 新建）。
///
/// 2026-10-22 启动优化阶段 3：自 <c>MainWindow.axaml</c> 抽出，**首次展开该菜单时才创建**。
/// </summary>
public partial class EditMenuItems : StackPanel
{
    public EditMenuItems()
    {
        InitializeComponent();
    }
}
