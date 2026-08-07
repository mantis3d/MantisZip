using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using MantisZip.UI.Avalonia.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MantisZip.UI.Avalonia.Dialogs;

public partial class AboutWindow : Window
{
    public string VersionDisplay => string.Format(LocalizationManager.T("About_Version"), AppConstants.Version);

    // Localized string properties (direct binding instead of dictionary indexer)
    public string AboutTitle => LocalizationManager.T("About_Title");
    public string AboutClose => LocalizationManager.T("About_Close");
    public string AboutLabelIntro => LocalizationManager.T("About_Label_Intro");
    public string AboutIntro => LocalizationManager.T("About_Intro");
    public string AboutLabelDescription => LocalizationManager.T("About_Label_Description");
    public string AboutDescription => LocalizationManager.T("About_Description");
    public string AboutLabelFormat => LocalizationManager.T("About_Label_Format");
    public string AboutFormats => LocalizationManager.T("About_Formats");
    public string AboutLabelLicense => LocalizationManager.T("About_Label_License");
    public string AboutLicense => LocalizationManager.T("About_License");
    public string AboutLabelGitHub => LocalizationManager.T("About_Label_GitHub");
    public string AboutLabelGitee => LocalizationManager.T("About_Label_Gitee");
    public string AboutLabelQQ => LocalizationManager.T("About_Label_QQ");
    public string AboutLabelBilibili => LocalizationManager.T("About_Label_Bilibili");
    public string AboutLabelAuthorName => LocalizationManager.T("About_Label_AuthorName");
    public string AboutLabelEmail => LocalizationManager.T("About_Label_Email");
    public string AboutLibraryName => LocalizationManager.T("About_Library_Name");
    public string AboutLibraryVersion => LocalizationManager.T("About_Library_Version");
    public string AboutLibraryLicense => LocalizationManager.T("About_Library_License");
    public string AboutLibraryPurpose => LocalizationManager.T("About_Library_Purpose");
    public string AboutThanksOSS => LocalizationManager.T("About_Thanks_OSS");
    public string AboutThanks7Zip => LocalizationManager.T("About_Thanks_7Zip");
    public string AboutThanksAI => LocalizationManager.T("About_Thanks_AI");
    public string AboutContributorsFinancial => LocalizationManager.T("About_Contributors_Financial");
    public string AboutContributorsTechnical => LocalizationManager.T("About_Contributors_Technical");
    public string AboutContributorsNone => LocalizationManager.T("About_Contributors_None");

    // Dependency purposes (About → Dependencies tab)
    public string AboutDepAvalonia => LocalizationManager.T("About_Dep_Avalonia");
    public string AboutDepWebView => LocalizationManager.T("About_Dep_WebView");
    public string AboutDepDataGrid => LocalizationManager.T("About_Dep_DataGrid");
    public string AboutDepMvvm => LocalizationManager.T("About_Dep_Mvvm");
    public string AboutDepSharpCompress => LocalizationManager.T("About_Dep_SharpCompress");
    public string AboutDepSevenZip => LocalizationManager.T("About_Dep_SevenZip");
    public string AboutDepSqlite => LocalizationManager.T("About_Dep_Sqlite");
    public string AboutDepSkia => LocalizationManager.T("About_Dep_Skia");
    public string AboutDepMarkdig => LocalizationManager.T("About_Dep_Markdig");
    public string AboutDepUde => LocalizationManager.T("About_Dep_Ude");

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;

        // 窗口图标：从嵌入资源加载（与 WPF 版 Icon="/Resources/App.ico" 一致）
        try
        {
            using var iconStream = AssetLoader.Open(new Uri("avares://MantisZip.UI.Avalonia/Resources/App.ico"));
            Icon = new WindowIcon(iconStream);
        }
        catch (Exception ex)
        {
            App.DebugLog($"[AboutWindow] Failed to load window icon: {ex.Message}");
        }

        // Tab headers — direct property set to avoid binding issues with dictionary indexer
        TabAboutHeader.Text = LocalizationManager.T("About_Tab_About");
        TabAuthorHeader.Text = LocalizationManager.T("About_Tab_Author");
        TabDependenciesHeader.Text = LocalizationManager.T("About_Tab_Dependencies");
        TabAcknowledgmentsHeader.Text = LocalizationManager.T("About_Tab_Acknowledgments");

        LoadContributors();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OpenUrl(string url)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher != null)
        {
            await launcher.LaunchUriAsync(new Uri(url));
        }
    }

    private void OnLicenseClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/mantis3d/MantisZip");
    }

    private void OnGitHubClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/mantis3d/MantisZip");
    }

    private void OnGiteeClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://gitee.com/mantis3d/MantisZip");
    }

    private void OnQQClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://qm.qq.com/cgi-bin/qm/qr?k=778347352");
    }

    private void OnAuthorGitHubClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/mantis3d");
    }

    private void OnAuthorGiteeClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://gitee.com/mantis3d");
    }

    private void OnAuthorBilibiliClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://space.bilibili.com/44202554");
    }

    private void OnEmailClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl("mailto:micheal.liu@163.com");
    }

    #region Contributors

    private void LoadContributors()
    {
        LoadContributorList("contributors-technical.csv", ContributorsTechnicalList, ContributorsTechnicalEmpty);
        LoadContributorList("contributors-financial.csv", ContributorsFinancialList, ContributorsFinancialEmpty);
    }

    private void LoadContributorList(string fileName, ItemsControl listControl, TextBlock emptyControl)
    {
        try
        {
            var csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (!File.Exists(csvPath))
            {
                ShowEmptyState(listControl, emptyControl);
                return;
            }

            var contributors = new List<Contributor>();
            var lines = File.ReadAllLines(csvPath, System.Text.Encoding.UTF8);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
                    continue;

                var parts = line.Split(',');
                if (parts.Length < 2)
                    continue;

                var name = parts[0].Trim();
                if (string.IsNullOrEmpty(name))
                    continue;

                if (int.TryParse(parts[1].Trim(), out var score))
                {
                    contributors.Add(new Contributor { Name = name, Score = score });
                }
            }

            if (contributors.Count == 0)
            {
                ShowEmptyState(listControl, emptyControl);
                return;
            }

            contributors = contributors
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.Name, System.StringComparer.Ordinal)
                .ToList();

            listControl.ItemsSource = contributors;
            listControl.IsVisible = true;
            emptyControl.IsVisible = false;
        }
        catch (IOException)
        {
            ShowEmptyState(listControl, emptyControl);
        }
        catch (UnauthorizedAccessException)
        {
            ShowEmptyState(listControl, emptyControl);
        }
    }

    private static void ShowEmptyState(ItemsControl listControl, TextBlock emptyControl)
    {
        listControl.IsVisible = false;
        emptyControl.IsVisible = true;
    }

    private class Contributor
    {
        public string Name { get; init; } = "";
        public int Score { get; init; }
    }

    #endregion
}
