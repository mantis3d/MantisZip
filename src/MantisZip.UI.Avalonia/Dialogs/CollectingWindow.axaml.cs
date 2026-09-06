using Avalonia.Controls;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// IPC 收集阶段显示的纯文字提示弹窗（无边框、无按钮）。
/// 用于 --compress 多实例收集路径期间给用户即时反馈，
/// 避免用 ProgressWindow（按钮/进度条会让用户误以为压缩已开始）。
/// </summary>
public partial class CollectingWindow : Window
{
    public CollectingWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>弹窗文案（本地化，code-behind 绑定）。</summary>
    public string CollectingText => LocalizationManager.T("App_CompressCollecting");
}
