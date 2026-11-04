namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 文件列表布局模式（详情 / 平铺 / 内容）。
/// <para>
/// 与 <see cref="MainWindowViewModel.FileListViewMode"/> 区分：那个枚举是**内容筛选**
/// （显示哪些行：全部 / 只看文件 / 只看目录），本枚举决定**每行怎么画**，两者互不影响。
/// 持久化在 <see cref="AppSettings.FileListLayoutMode"/>（JSON 里存 0/1/2，无法识别时落回详情）。
/// </para>
/// </summary>
public enum FileListLayoutMode { Details = 0, Tile = 1, Content = 2 }
