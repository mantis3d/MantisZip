using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.Core.Services;
using MantisZip.UI.Avalonia.Services;
using System.Threading.Tasks;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>用户在「发现新版本」提示对话框中的选择。</summary>
public enum UpdatePromptChoice
{
    /// <summary>下载：调用方负责用系统浏览器打开下载页。</summary>
    Download,

    /// <summary>稍后提醒：本次什么都不做，24h 后会再次询问。</summary>
    RemindLater,

    /// <summary>不再提示此版本：调用方把该版本号记为已跳过。</summary>
    SkipVersion
}

/// <summary>
/// 「发现新版本」提示对话框（启动时后台检查命中后弹出）。
/// <para>
/// 完成契约（规则 20）：构造函数只吃不可变数据，用户选择统一经 <see cref="Completion"/> 返回；
/// 本类不定义 DialogResult 属性、也不接回调委托，避免「关窗后回读窗口状态」的双轨回传。
/// </para>
/// </summary>
public partial class UpdateAvailableDialog : Window
{
    private readonly TaskCompletionSource<UpdatePromptChoice> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>用户选择：点按钮后完成；直接关窗 / 按 Esc 视为「稍后提醒」（见 <see cref="OnClosed"/>）。</summary>
    public Task<UpdatePromptChoice> Completion => _completion.Task;

    // ── 只读文案（DataContext = this；x:CompileBindings="False" 走反射绑定）──

    /// <summary>主提示「发现新版本」。</summary>
    public string DialogTitleText => LocalizationManager.T("Update_DialogTitle");

    /// <summary>
    /// 版本对比行「当前版本 X → 最新版本 Y」。
    /// <para>
    /// 旧版把「发现新版本 vX」和「最新版本 vX」分成两行，信息完全重复；
    /// 现在合并成一行，并把「当前版本」也带上，用户才知道差在哪。
    /// 当前版本取 <see cref="DebugUpdateVersion.Effective"/>（Debug 下可能被调试夹具覆盖）。
    /// </para>
    /// </summary>
    public string VersionCompareText { get; }

    /// <summary>「发布于 yyyy-MM-dd」行；无发布日期时为空串。</summary>
    public string PublishDateText { get; }

    /// <summary>是否显示发布日期行（接口可能不返回日期）。</summary>
    public bool HasPublishDate => !string.IsNullOrEmpty(PublishDateText);

    /// <summary>「更新说明」小标题。</summary>
    public string ReleaseNotesLabelText => LocalizationManager.T("Update_ReleaseNotes");

    /// <summary>
    /// 发布说明正文（GitHub release body 的 Markdown 原文）。
    /// 由 <see cref="MarkdownHelpContent"/> 渲染进 <c>NotesHost</c>，不走数据绑定。
    /// </summary>
    public string ReleaseNotesMarkdown { get; }

    /// <summary>接口带回了说明正文时显示 Markdown 区。</summary>
    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotesMarkdown);

    /// <summary>说明正文缺失时的回退文案（发布页地址），避免该区域整块空白。</summary>
    public bool HasNoReleaseNotes => !HasReleaseNotes;

    /// <summary>
    /// 回退用的发布页地址：接口没给 body 时直接展示它，
    /// 由用户在浏览器里看全文（对话框自身不发网络请求）。
    /// </summary>
    public string ReleaseNotesText { get; }

    /// <summary>「下载」按钮文字。</summary>
    public string DownloadText => LocalizationManager.T("Update_Download");

    /// <summary>「稍后提醒」按钮文字。</summary>
    public string RemindLaterText => LocalizationManager.T("Update_RemindLater");

    /// <summary>「不再提示此版本」按钮文字。</summary>
    public string SkipVersionText => LocalizationManager.T("Update_SkipThisVersion");

    /// <summary>
    /// XAML 编译器要求 x:Class 类型必须有公共无参构造（否则 AVLN3000），这里转调默认值实例。
    /// 无参构造只在设计器/反射路径使用，<c>default(UpdateInfo)</c> 的空字段已在下方做兜底。
    /// </summary>
    public UpdateAvailableDialog() : this(default) { }

    /// <summary>用后台检查拿到的更新信息构造对话框。</summary>
    public UpdateAvailableDialog(UpdateInfo info)
    {
        // default(UpdateInfo) 的字符串字段为 null，统一兜底为空串（避免绑定到 null 抛异常）
        var latestVersion = info.LatestVersion ?? string.Empty;
        var releaseNotesUrl = info.ReleaseNotesUrl ?? string.Empty;

        // 只读文案必须在 DataContext = this 之前算好：窗口显示后没有变更通知机制
        var currentVersion = DebugUpdateVersion.Effective;
        var versionCompare = LocalizationManager.T(
            "Update_VersionCompare",
            currentVersion,
            latestVersion);
        // Debug 下「当前版本」可能被调试夹具覆盖（见 Services/DebugUpdateVersion.cs）。
        // 不补这句话，测试时会看到「当前版本 0.0.1→ 最新版本 0.5.3」而误以为版本号坏了。
        if (DebugUpdateVersion.IsOverridden)
        {
            versionCompare += LocalizationManager.T("Update_DebugFakeVersionFmt", AppConstants.Version);
        }

        VersionCompareText = versionCompare;
        PublishDateText = string.IsNullOrEmpty(info.PublishDateDisplay)
            ? string.Empty
            : LocalizationManager.T("Update_PublishDate", info.PublishDateDisplay);
        ReleaseNotesMarkdown = info.ReleaseNotes ?? string.Empty;
        // 没有发布页地址时退回「下载」文案，避免该区域出现空白
        ReleaseNotesText = releaseNotesUrl.Length > 0
            ? releaseNotesUrl
            : LocalizationManager.T("Update_Download");

        InitializeComponent();

        // Markdown 正文直接塞进宿主：MarkdownPreviewBuilder 产出原生控件树，不需要绑定/转换器
        if (HasReleaseNotes)
        {
            NotesHost.Content = new MarkdownHelpContent(ReleaseNotesMarkdown);
        }

        DataContext = this;
    }

    /// <summary>下载：先完成契约再关窗（打开浏览器由调用方负责，对话框不做宿主侧动作）。</summary>
    private void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        _completion.TrySetResult(UpdatePromptChoice.Download);
        Close();
    }

    /// <summary>稍后提醒：24h 后由启动检查再次询问。</summary>
    private void OnRemindLaterClick(object? sender, RoutedEventArgs e)
    {
        _completion.TrySetResult(UpdatePromptChoice.RemindLater);
        Close();
    }

    /// <summary>不再提示此版本：由调用方记录 LastSkippedVersion。</summary>
    private void OnSkipVersionClick(object? sender, RoutedEventArgs e)
    {
        _completion.TrySetResult(UpdatePromptChoice.SkipVersion);
        Close();
    }

    /// <summary>
    /// 关窗兜底：用户点标题栏 X 或按 Esc（未点任何按钮）时按「稍后提醒」完成契约，
    /// 否则调用方的 <c>await dlg.Completion</c> 会永久挂起。
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _completion.TrySetResult(UpdatePromptChoice.RemindLater);
        base.OnClosed(e);
    }
}
