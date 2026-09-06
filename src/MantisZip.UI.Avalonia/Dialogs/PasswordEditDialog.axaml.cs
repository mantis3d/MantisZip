using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 密码编辑对话框 — 添加或编辑密码条目
/// </summary>
public partial class PasswordEditDialog : Window
{
    /// <summary>输入的密码</summary>
    public string? PasswordResult { get; private set; }

    /// <summary>输入的描述</summary>
    public string? DescriptionResult { get; private set; }

    /// <summary>输入的匹配规则（多行字符串）</summary>
    public string? RulesResult { get; private set; }

    // ── Localized string properties ──
    public string WinTitle => LocalizationManager.T("PwdEdit_Title");
    public string PwdLabel => LocalizationManager.T("PwdEdit_PasswordLabel");
    public string DescLabel => LocalizationManager.T("PwdEdit_DescriptionLabel");
    public string RulesLabel => LocalizationManager.T("PwdEdit_RulesLabel");
    public string SaveText => LocalizationManager.T("PwdEdit_Save");
    public string CancelText => LocalizationManager.T("PasswordManager_Cancel");

    public PasswordEditDialog() : this(null) { }

    /// <param name="entry">已有的密码条目（编辑模式），null 表示新建</param>
    public PasswordEditDialog(Core.PasswordEntry? entry)
    {
        InitializeComponent();
        DataContext = this;

        if (entry != null)
        {
            PasswordBox.Text = entry.Password;
            DescriptionBox.Text = entry.Description;
            PatternsBox.Text = string.Join("\n", entry.Patterns);
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Text;
        if (string.IsNullOrWhiteSpace(password))
        {
            return; // No validation dialog needed for now; caller checks
        }

        PasswordResult = password;
        DescriptionResult = DescriptionBox.Text?.Trim() ?? string.Empty;
        RulesResult = PatternsBox.Text;

        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
