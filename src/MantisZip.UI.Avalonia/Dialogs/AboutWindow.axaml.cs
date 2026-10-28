using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using MantisZip.Core.Services;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 关于窗口（5 个选项卡：关于 / 作者 / 依赖库 / 致谢 / 更新）。
/// <para>
/// 前 4 个选项卡的文案都是只读表达式属性（构造时 <c>DataContext = this</c>，绑定一次性求值即可）；
/// 第 5 个「更新」页需要动态刷新状态文本，故本类额外实现 <see cref="INotifyPropertyChanged"/>，
/// 仅新属性（Update* 系列）走 SetField 通知，既有只读属性绑定方式保持不变。
/// </para>
/// </summary>
public partial class AboutWindow : Window, INotifyPropertyChanged
{
    /// <summary>
    /// 属性变更通知（仅第 5 个「更新」页的动态属性使用）。
    /// <para>
    /// 用 <c>new</c> 显式隐藏 <see cref="AvaloniaObject.PropertyChanged"/>：基类那个事件是
    /// <c>AvaloniaPropertyChangedEventHandler</c>，用途是 AvaloniaProperty 变更通知；这里要的是
    /// <see cref="INotifyPropertyChanged"/> 的同名事件，供 <c>{Binding}</c> 读取普通属性。
    /// 隐藏后基类事件仍由 Avalonia 内部按自己的字段触发，两者互不影响。
    /// </para>
    /// </summary>
    public new event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 赋值并在值变化时发通知（<c>[CallerMemberName]</c> 自动取属性名）。
    /// </summary>
    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

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

    // Tab headers (bound via TabItemExtensions.TabIcon + compactTab HeaderTemplate)
    public string AboutTabHeader => LocalizationManager.T("About_Tab_About");
    public string AuthorTabHeader => LocalizationManager.T("About_Tab_Author");
    public string DependenciesTabHeader => LocalizationManager.T("About_Tab_Dependencies");
    public string AcknowledgmentsTabHeader => LocalizationManager.T("About_Tab_Acknowledgments");

    // ── 选项卡 5：更新（自动更新检测，2026-10-28 新增）──

    /// <summary>第 5 个页签标题（规则 13：文案一律走 LocalizationManager）。</summary>
    public string UpdatesTabHeader => LocalizationManager.T("Update_TabName");

    /// <summary>「检查更新」按钮文字。</summary>
    public string UpdateCheckNowText => LocalizationManager.T("Update_CheckNow");

    /// <summary>「打开下载页」按钮文字。</summary>
    public string UpdateDownloadText => LocalizationManager.T("Update_Download");

    /// <summary>当前版本行（只读；参与比较的版本，Debug 下可能被调试夹具覆盖）。</summary>
    public string UpdateCurrentVersionText
    {
        get
        {
            // 版本号取「实际参与比较的版本」：Debug 下可能被 fake-current-version.txt 覆盖，
            // 覆盖时追加一句真实版本，免得看到「0.0.1 发现新版本 v0.5.3」误以为版本号坏了。
            var effective = DebugUpdateVersion.Effective;
            var text = LocalizationManager.T("Update_CurrentVersion") + " " + effective;
            return DebugUpdateVersion.IsOverridden
                ? text + LocalizationManager.T("Update_DebugFakeVersionFmt", AppConstants.Version)
                : text;
        }
    }

    /// <summary>最近一次检查发现的新版本；null = 尚未检查或已是最新（供「打开下载页」使用）。</summary>
    private UpdateInfo? _latest;

    // 以下字段由 SetField 触发 PropertyChanged，驱动第 5 页动态文案刷新
    private string _updateLastCheckText = string.Empty;
    private string _updateStatusText = string.Empty;
    private bool _hasUpdateStatus;
    private bool _showLatestVersion;
    private string _updateLatestVersionText = string.Empty;
    private string _updatePublishDateText = string.Empty;

    /// <summary>上次检查时间行（本地时间；无记录时显示「从未检查」）。</summary>
    public string UpdateLastCheckText
    {
        get => _updateLastCheckText;
        private set => SetField(ref _updateLastCheckText, value);
    }

    /// <summary>检查状态文本（正在检查 / 已是最新 / 发现新版本 / 检查失败）。</summary>
    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetField(ref _updateStatusText, value);
    }

    /// <summary>是否显示状态文本（空串时整块隐藏，不留空白占位）。</summary>
    public bool HasUpdateStatus
    {
        get => _hasUpdateStatus;
        private set => SetField(ref _hasUpdateStatus, value);
    }

    /// <summary>是否显示新版本详情区（版本号 / 发布日期 / 打开下载页）。</summary>
    public bool ShowLatestVersion
    {
        get => _showLatestVersion;
        private set => SetField(ref _showLatestVersion, value);
    }

    /// <summary>新版本号行。</summary>
    public string UpdateLatestVersionText
    {
        get => _updateLatestVersionText;
        private set => SetField(ref _updateLatestVersionText, value);
    }

    /// <summary>发布日期行（接口未给出日期时为空串，该行不显示任何文字）。</summary>
    public string UpdatePublishDateText
    {
        get => _updatePublishDateText;
        private set => SetField(ref _updatePublishDateText, value);
    }

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

        // Tab headers 通过 AboutTabHeader/AuthorTabHeader 等属性绑定（compactTab HeaderTemplate）
        LoadContributors();

        // 更新页初始状态：刷新「上次检查」行；状态文本初始为空 → 状态区/新版本区均隐藏
        RefreshUpdateLastCheckText();
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

    // ── 更新页（自动更新检测，2026-10-28 新增）──

    /// <summary>刷新「上次检查」行：有记录时显示本地时间，否则显示「从未检查」。</summary>
    private void RefreshUpdateLastCheckText()
    {
        var lastCheckTime = AppSettings.Load().LastUpdateCheckTime;
        UpdateLastCheckText = lastCheckTime is { } utc
            ? LocalizationManager.T("Update_LastCheckTime", utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
            : LocalizationManager.T("Update_NeverChecked");
    }

    /// <summary>设置状态文本，并同步「是否显示状态区」（空串 → 隐藏，不留空白占位）。</summary>
    private void SetUpdateStatus(string text)
    {
        UpdateStatusText = text;
        HasUpdateStatus = !string.IsNullOrEmpty(text);
    }

    /// <summary>
    /// 「检查更新」按钮：后台请求 GitHub 最新正式发布，刷新本页状态。
    /// <para>
    /// async void 里异常冒泡会直接杀进程，故整个方法包在 try/catch 内：
    /// 失败只写 debug.log + 显示失败文案，绝不抛出。
    /// </para>
    /// </summary>
    private async void OnCheckUpdateClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            // 请求期间先给反馈（网络往返可能数百毫秒）
            SetUpdateStatus(LocalizationManager.T("Update_Checking"));
            _latest = null;
            ShowLatestVersion = false;

            // 与启动检查共用同一解析器：Debug 下允许 fake-current-version.txt 覆盖比较基准
            var result = await UpdateService.CheckForUpdateAsync(DebugUpdateVersion.Effective);

            // 成功（无论有无新版本）才记录检查时间：失败时让用户再点一次就能真的重试，
            // 而不是被节流挡住、看起来像「按钮没反应」（KI-008 的另一面）。
            if (!result.IsFailed)
            {
                var settings = AppSettings.Load();
                settings.LastUpdateCheckTime = DateTime.UtcNow;
                settings.Save();
                RefreshUpdateLastCheckText();
            }

            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when result.Update is { } update:
                    var latestVersion = update.LatestVersion ?? string.Empty;
                    _latest = update;
                    UpdateLatestVersionText = LocalizationManager.T("Update_NewVersion") + " " + latestVersion;
                    // 接口未给发布日期时该行为空串（不显示任何文字）
                    UpdatePublishDateText = string.IsNullOrEmpty(update.PublishDateDisplay)
                        ? string.Empty
                        : LocalizationManager.T("Update_PublishDate", update.PublishDateDisplay);
                    ShowLatestVersion = true;
                    SetUpdateStatus(LocalizationManager.T("Update_NewVersionAvailable", latestVersion));
                    break;

                case UpdateCheckStatus.Failed:
                    // 断网 / 超时 / 服务端异常：必须与「已是最新」区分开，
                    // 否则用户会以为真的没有新版（KI-008 的原始现象）
                    App.DebugLog($"[AboutWindow] 检查更新失败：{result.Reason}");
                    _latest = null;
                    ShowLatestVersion = false;
                    SetUpdateStatus(LocalizationManager.T("Update_CheckFailed"));
                    break;

                default:
                    _latest = null;
                    ShowLatestVersion = false;
                    SetUpdateStatus(LocalizationManager.T("Update_UpToDate"));
                    break;
            }
        }
        catch (Exception ex)
        {
            App.DebugLog($"[AboutWindow] CheckForUpdate failed: {ex.GetType().Name} {ex.Message}");
            _latest = null;
            ShowLatestVersion = false;
            SetUpdateStatus(LocalizationManager.T("Update_CheckFailed"));
        }
    }

    /// <summary>「打开下载页」按钮：把最近一次检查得到的下载地址交给系统浏览器（复用 OpenUrl）。</summary>
    private void OnOpenReleasePageClick(object? sender, RoutedEventArgs e)
    {
        // 按钮仅在有新版本时可见；_latest 为空（或地址缺失）时不动作，避免抛异常
        if (_latest is { } update && !string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            OpenUrl(update.DownloadUrl);
        }
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
