using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 密码管理器帮助对话框 — 显示匹配规则说明
/// </summary>
public partial class PasswordHelpDialog : Window
{
    public string WinTitle => LocalizationManager.T("PwdHelp_Title");
    public string SectionTitle => LocalizationManager.T("PwdHelp_Section_Title");
    public string IntroText => LocalizationManager.T("PwdHelp_Intro");
    public string GlobTitle => LocalizationManager.T("PwdHelp_Glob_Title");
    public string GlobColWildcard => LocalizationManager.T("PwdHelp_Glob_Col_Wildcard");
    public string GlobColMeaning => LocalizationManager.T("PwdHelp_Glob_Col_Meaning");
    public string GlobMatchAny => LocalizationManager.T("PwdHelp_Glob_MatchAny");
    public string GlobMatchSingle => LocalizationManager.T("PwdHelp_Glob_MatchSingle");
    public string GlobMatchDot => LocalizationManager.T("PwdHelp_Glob_MatchDot");
    public string GlobOtherChars => LocalizationManager.T("PwdHelp_Glob_OtherChars");
    public string GlobMatchLiteral => LocalizationManager.T("PwdHelp_Glob_MatchLiteral");
    public string GlobExamplesTitle => LocalizationManager.T("PwdHelp_Glob_Examples_Title");
    public string GlobExAllZip => LocalizationManager.T("PwdHelp_Glob_Example_AllZip");
    public string GlobExDocStar => LocalizationManager.T("PwdHelp_Glob_Example_DocStar");
    public string GlobExDocStarDesc => LocalizationManager.T("PwdHelp_Glob_Example_DocStarDesc");
    public string GlobExThreeChars => LocalizationManager.T("PwdHelp_Glob_Example_ThreeChars");
    public string GlobExBackup => LocalizationManager.T("PwdHelp_Glob_Example_Backup");
    public string GlobExBackupDesc => LocalizationManager.T("PwdHelp_Glob_Example_BackupDesc");

    public string RegexTitle => LocalizationManager.T("PwdHelp_Regex_Title");
    public string RegexCommonTitle => LocalizationManager.T("PwdHelp_Regex_Common_Title");
    public string RegexDigit => LocalizationManager.T("PwdHelp_Regex_Digit");
    public string RegexWord => LocalizationManager.T("PwdHelp_Regex_Word");
    public string RegexCharSet => LocalizationManager.T("PwdHelp_Regex_CharSet");
    public string RegexAnchors => LocalizationManager.T("PwdHelp_Regex_Anchors");
    public string RegexOr => LocalizationManager.T("PwdHelp_Regex_Or");
    public string RegexExamplesTitle => LocalizationManager.T("PwdHelp_Regex_Examples_Title");
    public string RegexExFinance => LocalizationManager.T("PwdHelp_Regex_Ex_Finance");
    public string RegexExFinanceDesc => LocalizationManager.T("PwdHelp_Regex_Ex_FinanceDesc");
    public string RegexExRar => LocalizationManager.T("PwdHelp_Regex_Ex_Rar");
    public string RegexExYear => LocalizationManager.T("PwdHelp_Regex_Ex_Year");
    public string RegexExProject => LocalizationManager.T("PwdHelp_Regex_Ex_Project");
    public string RegexExProjectDesc => LocalizationManager.T("PwdHelp_Regex_Ex_ProjectDesc");

    public string MatchTitle => LocalizationManager.T("PwdHelp_Match_Title");
    public string MatchStep1 => LocalizationManager.T("PwdHelp_Match_Step1");
    public string MatchStep2 => LocalizationManager.T("PwdHelp_Match_Step2");
    public string MatchStep3 => LocalizationManager.T("PwdHelp_Match_Step3");
    public string MatchStep4 => LocalizationManager.T("PwdHelp_Match_Step4");
    public string MatchStep5 => LocalizationManager.T("PwdHelp_Match_Step5");

    public string CloseText => LocalizationManager.T("PwdHelp_Close");

    public PasswordHelpDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
