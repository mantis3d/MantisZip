using Avalonia.Controls;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 日志隐私模式帮助内容（UserControl）。
/// 配合 HelpDialog 使用。
/// </summary>
public partial class LogPrivacyHelpContent : UserControl
{
    public string WinTitle => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_Title");
    public string IntroText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_Intro");
    public string ModeOffText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_Mode_Off");
    public string ModeFilenameText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_Mode_Filename");
    public string ModeExtensionText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_Mode_Extension");
    public string ModeFullText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_Mode_Full");
    public string WhatText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_What");
    public string WhatNotText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_WhatNot");

    public LogPrivacyHelpContent()
    {
        InitializeComponent();
        DataContext = this;
    }
}
