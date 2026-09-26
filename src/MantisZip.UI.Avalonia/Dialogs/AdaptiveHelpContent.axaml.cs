using Avalonia.Controls;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 自适应压缩级别帮助内容（UserControl）。
/// 配合 HelpDialog 使用。
/// </summary>
public partial class AdaptiveHelpContent : UserControl
{
    public string TitleText => LocalizationManager.T("Adaptive_Help_Title");
    public string IntroText => LocalizationManager.T("Adaptive_Help_Intro");
    public string DisabledTitle => LocalizationManager.T("Adaptive_Help_Disabled_Title");
    public string DisabledDesc => LocalizationManager.T("Adaptive_Help_Disabled_Desc");
    public string BasicTitle => LocalizationManager.T("Adaptive_Help_Basic_Title");
    public string BasicDesc => LocalizationManager.T("Adaptive_Help_Basic_Desc");
    public string SmartTitle => LocalizationManager.T("Adaptive_Help_Smart_Title");
    public string SmartDesc => LocalizationManager.T("Adaptive_Help_Smart_Desc");
    public string MultiThreadedTitle => LocalizationManager.T("Adaptive_Help_MultiThreaded_Title");
    public string MultiThreadedDesc => LocalizationManager.T("Adaptive_Help_MultiThreaded_Desc");
    public string MultiThreadedWarning => LocalizationManager.T("Adaptive_Help_MultiThreaded_Warning");

    public AdaptiveHelpContent()
    {
        InitializeComponent();
        DataContext = this;
    }
}
