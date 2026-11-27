using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using MantisZip.Core;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Views;

/// <summary>
/// 密码对话框的确认载荷：一次关闭即交出全部输入。<c>Password == null</c> 表示取消/空密码。
/// 调用方只读 <see cref="PasswordDialog.Completion"/>，不再「先看 bool 再回读窗口属性」。
/// </summary>
public readonly record struct PasswordDialogOutcome(
    string? Password,
    bool RememberInSession,
    bool SavePermanently,
    string? Description,
    IReadOnlyList<string> Patterns);

public partial class PasswordDialog : Window
{
    private readonly TaskCompletionSource<PasswordDialogOutcome> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PasswordDialogOutcome? _outcome;

    /// <summary>关窗即完成的确认载荷（模态 ShowDialog 与非模态 Show 两条路径共用）。</summary>
    public Task<PasswordDialogOutcome> Completion => _completion.Task;

    public string? Password { get; private set; }
    public bool RememberInSession { get; private set; } = true;
    public bool SavePermanently { get; private set; }
    public string? Description { get; private set; }
    public List<string> Patterns { get; private set; } = new();

    public string FileName { get; set; } = "";

    // ── Localized strings ──
    public string DialogTitle => LocalizationManager.T("Password_Title");
    public string PasswordPlaceholder => LocalizationManager.T("Password_Placeholder");
    public string RememberText => LocalizationManager.T("Password_Remember");
    public string SavePermanentlyText => LocalizationManager.T("Password_SavePermanently");
    public string DescriptionLabelText => LocalizationManager.T("Password_DescriptionLabel");
    public string DescriptionWatermark => LocalizationManager.T("Password_DescriptionWatermark");
    public string MatchRulesLabelText => LocalizationManager.T("Password_MatchRulesLabel");
    public string MatchRulesWatermark => LocalizationManager.T("Password_MatchRulesWatermark");
    public string AutoGenerateRulesText => LocalizationManager.T("Compress_Pwd_AutoRules");
    public string NewPasswordOptionText => LocalizationManager.T("Password_NewPasswordOption");
    public string OkText => LocalizationManager.T("Password_Ok");
    public string CancelText => LocalizationManager.T("Password_Cancel");
    public string RevealTooltip => LocalizationManager.T("Password_RevealTooltip");

    private bool _isUpdatingPasswordSelection;

    /// <summary>当前是否明文显示（初始态取「默认显示密码」设置，之后由 👁 按钮临时切换）。</summary>
    private bool _isRevealed;

    public PasswordDialog()
    {
        InitializeComponent();
        DataContext = this;

        // 初始显示态跟随设置；只决定打开时的状态，不锁死切换。
        var settings = AppSettings.Load();
        _isRevealed = settings.PasswordRevealByDefault;
        ApplyRevealState();

        // 两个勾选框的初值跟随设置（issue #87）。必须在 InitializeComponent 之后显式赋值：
        // XAML 里的 IsChecked 是硬编码的，留着它们当「默认值」等于设置项形同虚设。
        ApplyDefaultOptions(settings);
    }

    /// <summary>
    /// 按设置写入两个勾选框的初值。这里刻意只写初值、不写回设置 ——
    /// issue #87 的约定是「窗内临时改动只对本次生效，不回写全局默认值」。
    /// </summary>
    private void ApplyDefaultOptions(AppSettings settings)
    {
        // 赋值本身会触发 IsCheckedChanged 处理器（同步 PatternsTextBox 的只读态与自动规则文本），
        // 所以这里不需要再手工复刻一份逻辑。此时 FileName 可能还是空的，
        // 带文件名的构造里会在 FileName 赋值后再补一次自动规则。
        SavePermanentlyCheck.IsChecked = settings.SavePermanentlyByDefault;
        AutoGenerateRulesCheck.IsChecked = settings.AutoGenerateRulesByDefault;

        // 值没变化时 IsCheckedChanged 不会触发（XAML 里 AutoGenerateRulesCheck 的初值就是 True），
        // 只依赖事件会让「默认勾上自动规则」这条路径漏掉只读态 —— 显式补一次。
        SyncPatternsReadOnly();
    }

    /// <summary>规则框的只读态跟随「自动生成规则」：勾上就由程序生成、不许手改。</summary>
    private void SyncPatternsReadOnly()
    {
        PatternsTextBox.IsReadOnly = AutoGenerateRulesCheck.IsChecked == true;
    }

    public PasswordDialog(string fileName) : this()
    {
        FileName = fileName;
        FileNameText.Text = fileName;
        LoadSavedPasswords();

        // 上面 ApplyDefaultOptions 里 FileName 还是空的，RefreshAutoRules 会提前返回；
        // 拿到文件名后补一次，否则「默认勾选自动生成规则」看不到规则文本（issue #87）。
        if (SavePermanentlyCheck.IsChecked == true && AutoGenerateRulesCheck.IsChecked == true)
            RefreshAutoRules();
    }

    private void LoadSavedPasswords()
    {
        try
        {
            var entries = PasswordManager.Instance.GetAllPasswords();
            foreach (var entry in entries)
            {
                var display = !string.IsNullOrEmpty(entry.Description)
                    ? $"{entry.Description} — {entry.Password}"
                    : entry.Password;
                var item = new ComboBoxItem
                {
                    Content = display,
                    Tag = entry
                };
                PasswordSelector.Items.Add(item);
            }
        }
        catch
        {
            // PasswordManager unavailable — skip saved passwords list
        }
    }

    private void PasswordSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingPasswordSelection) return;
        if (PasswordSelector.SelectedItem is ComboBoxItem item && item.Tag is not string tagStr)
        {
            // Saved password selected: fill in the password and mark as "remember permanently"
            if (item.Tag is PasswordEntry entry)
            {
                _isUpdatingPasswordSelection = true;
                PasswordBox.Text = entry.Password;
                SavePermanentlyCheck.IsChecked = true;
                RememberCheck.IsChecked = true;
                DescTextBox.Text = entry.Description ?? "";
                PatternsTextBox.Text = string.Join(", ", entry.Patterns);
                _isUpdatingPasswordSelection = false;
            }
        }
        else
        {
            // "New password" option selected — clear fields
            _isUpdatingPasswordSelection = true;
            PasswordBox.Text = "";
            _isUpdatingPasswordSelection = false;
        }
    }

    private void OnRevealToggle(object? sender, RoutedEventArgs e)
    {
        _isRevealed = !_isRevealed;
        ApplyRevealState();
    }

    /// <summary>把当前显示态落到密码框与 👁 图标上（初始态与切换共用一处）。</summary>
    private void ApplyRevealState()
    {
        PasswordBox.PasswordChar = _isRevealed ? '\0' : '●';
        RevealEyeIcon.Data = (Geometry?)this.FindResource(_isRevealed ? "IconEyeOff" : "IconEye");
    }

    private void OnRememberChanged(object? sender, RoutedEventArgs e)
    {
        // 「保存到密码库」与「记住会话中」完全独立——取消会话记忆不影响持久化保存
    }

    private void OnSavePermanentlyChanged(object? sender, RoutedEventArgs e)
    {
        if (SavePermanentlyCheck.IsChecked == true && AutoGenerateRulesCheck.IsChecked == true)
            RefreshAutoRules();
        else if (SavePermanentlyCheck.IsChecked != true)
            PatternsTextBox.Text = "";
    }

    private void OnAutoGenerateRulesChanged(object? sender, RoutedEventArgs e)
    {
        if (AutoGenerateRulesCheck.IsChecked == true && SavePermanentlyCheck.IsChecked == true)
            RefreshAutoRules();
        SyncPatternsReadOnly();
    }

    private void RefreshAutoRules()
    {
        if (string.IsNullOrEmpty(FileName)) return;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(FileName);
        if (!string.IsNullOrEmpty(nameWithoutExt))
            PatternsTextBox.Text = $"{nameWithoutExt}*";
    }

    private void PasswordBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnOkClick(sender, e);
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Password = PasswordBox.Text;
        RememberInSession = RememberCheck.IsChecked == true;
        SavePermanently = SavePermanentlyCheck.IsChecked == true;
        Description = SavePermanently ? DescTextBox.Text?.Trim() : null;
        var patternsText = SavePermanently ? PatternsTextBox.Text?.Trim() : null;
        Patterns = !string.IsNullOrEmpty(patternsText)
            ? patternsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : new List<string>();

        if (string.IsNullOrEmpty(Password))
        {
            Password = null;
            _outcome = new PasswordDialogOutcome(null, RememberInSession, SavePermanently, Description, Patterns);
            Close(false);
            return;
        }

        _outcome = new PasswordDialogOutcome(Password, RememberInSession, SavePermanently, Description, Patterns);
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Password = null;
        _outcome = new PasswordDialogOutcome(null, RememberInSession, SavePermanently: false, Description: null, Patterns: Array.Empty<string>());
        Close(false);
    }

    protected override void OnClosed(EventArgs e)
    {
        // 兜底：非按钮关闭（X / Alt+F4）也给出完整载荷——等价于「取消」，绝不让调用方读到 null
        _completion.TrySetResult(_outcome ?? new PasswordDialogOutcome(null, RememberInSession, false, null, Array.Empty<string>()));
        base.OnClosed(e);
    }
}
