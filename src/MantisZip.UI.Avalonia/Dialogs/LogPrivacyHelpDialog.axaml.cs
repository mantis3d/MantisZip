using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 日志隐私模式帮助对话框
/// </summary>
public partial class LogPrivacyHelpDialog : Window
{
    public string WinTitle => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_Title");
    public string IntroText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_Intro");
    public string ModeOffText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_Mode_Off");
    public string ModeFilenameText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_Mode_Filename");
    public string ModeExtensionText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_Mode_Extension");
    public string ModeFullText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_Mode_Full");
    public string WhatText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_What");
    public string WhatNotText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_WhatNot");
    public string CloseText => LocalizationManager.T("Settings_Debug_LogPrivacyHelp_Close");

    public LogPrivacyHelpDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
