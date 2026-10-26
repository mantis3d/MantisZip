using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MantisZip.Core;
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

    public PasswordDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public PasswordDialog(string fileName) : this()
    {
        FileName = fileName;
        FileNameText.Text = fileName;
        LoadSavedPasswords();
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
                PasswordBox.PasswordChar = '●';
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
            PasswordBox.PasswordChar = '●';
            _isUpdatingPasswordSelection = false;
        }
    }

    private void OnRevealToggle(object? sender, RoutedEventArgs e)
    {
        if (PasswordBox.PasswordChar == '\0')
            PasswordBox.PasswordChar = '●';
        else
            PasswordBox.PasswordChar = '\0';
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
        PatternsTextBox.IsReadOnly = AutoGenerateRulesCheck.IsChecked == true;
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
