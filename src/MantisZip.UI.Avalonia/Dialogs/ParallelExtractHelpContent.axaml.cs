using Avalonia.Controls;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 并行解压说明帮助内容（UserControl）：3 章节（并行解压/智能并行度/大包预测试）。
/// 配合 HelpDialog 使用。从设置窗口解压 Tab 的 [?] 按钮打开。
/// </summary>
public partial class ParallelExtractHelpContent : UserControl
{
    /// <summary>章节正文上方的标题（本地化）。</summary>
    public string WinTitle => LocalizationManager.T("ParallelHelp_Title");
    public string EnableSectionText => LocalizationManager.T("ParallelHelp_EnableSection");
    public string SmartSectionText => LocalizationManager.T("ParallelHelp_SmartSection");
    public string PreTestSectionText => LocalizationManager.T("ParallelHelp_PreTestSection");
    /// <summary>章节 1 标题（本地化）。</summary>
    public string EnableSectionTitle => LocalizationManager.T("HelpSection_ParallelExtract");
    /// <summary>章节 2 标题（本地化）。</summary>
    public string SmartSectionTitle => LocalizationManager.T("HelpSection_SmartDegree");
    /// <summary>章节 3 标题（本地化）。</summary>
    public string PreTestSectionTitle => LocalizationManager.T("HelpSection_PreTest");

    public ParallelExtractHelpContent()
    {
        InitializeComponent();
        DataContext = this;
    }
}
