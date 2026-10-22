using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Controls.Menus;

/// <summary>
/// 文件菜单子项（打开 / 打开所在位置 / 关闭 / 最近文件 / 清除最近文件 / 刷新 / 退出）。
///
/// 2026-10-22 启动优化阶段 3：自 <c>MainWindow.axaml</c> 抽出，**首次展开该菜单时才创建**。
/// 根为 <see cref="StackPanel"/>（可枚举 <c>Children</c>）→ <c>MainWindow</c> 在展开时把
/// 子元素搬进对应 <see cref="MenuItem"/> 的 <c>Items</c>。
/// </summary>
public partial class FileMenuItems : StackPanel
{
    public FileMenuItems()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 「最近文件」子项点击 → 打开对应压缩包。
    /// 2026-10-22：随文件菜单视图一并迁入（XAML 事件处理器必须在声明它的类中）。
    /// </summary>
    private void RecentFileMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is string filePath)
        {
            if (DataContext is MainWindowViewModel vm && vm.OpenRecentFileCommand.CanExecute(filePath))
            {
                vm.OpenRecentFileCommand.Execute(filePath);
            }
        }
    }
}
