using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 并行解压说明帮助弹窗：3 章节（并行解压/智能并行度/大包预测试）。
/// 从设置窗口解压 Tab 的 [?] 按钮打开。
/// </summary>
public partial class ParallelExtractHelpDialog : Window
{
    public ParallelExtractHelpDialog()
    {
        InitializeComponent();
        WinTitle = LocalizationManager.T("ParallelHelp_Title");
        EnableSectionText = LocalizationManager.T("ParallelHelp_EnableSection");
        SmartSectionText = LocalizationManager.T("ParallelHelp_SmartSection");
        PreTestSectionText = LocalizationManager.T("ParallelHelp_PreTestSection");
        CloseText = LocalizationManager.T("MsgBox_Close");
        DataContext = this;
    }

    public string WinTitle { get; }
    public string EnableSectionText { get; }
    public string SmartSectionText { get; }
    public string PreTestSectionText { get; }
    public string CloseText { get; }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
