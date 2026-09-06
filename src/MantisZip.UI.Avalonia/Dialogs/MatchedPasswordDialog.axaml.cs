using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using MantisZip.Core;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 已匹配密码查看对话框。
/// 显示已匹配的密码、描述和规则，支持显示/隐藏和复制。
/// </summary>
public partial class MatchedPasswordDialog : Window
{
    private readonly string _password = string.Empty;
    private bool _isRevealed;

    public Core.PasswordEntry? SelectedEntry { get; private set; }

    // ── Localized string properties ──
    public string WinTitle => LocalizationManager.T("PwdMatched_Title");
    public string UseText => LocalizationManager.T("PwdMatched_Use");
    public string CancelText => LocalizationManager.T("PwdMatched_Cancel");
    public string RevealTooltip => LocalizationManager.T("PwdMatched_RevealTooltip");
    public string CopyTooltip => LocalizationManager.T("PwdMatched_CopyTooltip");

    /// <summary>
    /// 设计时 / XAML 编译器需要的无参构造函数
    /// </summary>
    public MatchedPasswordDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>
    /// 参数化构造函数 — 通过代码创建对话框时使用
    /// </summary>
    public MatchedPasswordDialog(Core.PasswordEntry entry, string archiveName) : this()
    {
        _password = entry.Password;
        HeaderText.Text = LocalizationManager.T("PwdMatched_Header", archiveName);
        PwdPlainText.Text = entry.Password;

        if (entry.Patterns.Count > 0)
        {
            RulesSection.IsVisible = true;
            RulesText.Text = $"{LocalizationManager.T("PwdMatched_RulesLabel")}: {string.Join(", ", entry.Patterns)}";
        }

        if (!string.IsNullOrEmpty(entry.Description))
        {
            DescriptionSection.IsVisible = true;
            DescriptionText.Text = $"{LocalizationManager.T("PwdMatched_DescriptionLabel")}: {entry.Description}";
        }
    }

    private void OnRevealClick(object? sender, RoutedEventArgs e)
    {
        _isRevealed = !_isRevealed;
        PwdMaskedText.IsVisible = !_isRevealed;
        PwdPlainText.IsVisible = _isRevealed;
        RevealEyeIcon.IsVisible = !_isRevealed;
        RevealEyeOffIcon.IsVisible = _isRevealed;
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var clipboard = topLevel?.Clipboard;
            if (clipboard != null)
            {
                var transfer = new global::Avalonia.Input.DataTransfer();
                var item = new global::Avalonia.Input.DataTransferItem();
                item.SetText(_password);
                transfer.Add(item);
                await clipboard.SetDataAsync(transfer);
            }

            // Save original icon reference
            var copyIcon = PwdCopyBtn.Content;
            // Create a temporary checkmark icon
            PwdCopyBtn.Content = new PathIcon
            {
                Data = (Geometry?)global::Avalonia.Application.Current?.FindResource("IconCheckmark"),
                Width = 14,
                Height = 14,
                Foreground = (Brush?)global::Avalonia.Application.Current?.FindResource("ThemeTextPrimaryBrush")
            };

            await Task.Delay(1500);
            PwdCopyBtn.Content = copyIcon;
        }
        catch
        {
            // Log silently - best-effort clipboard copy
        }
    }

    private void OnUseClick(object? sender, RoutedEventArgs e)
    {
        SelectedEntry = new Core.PasswordEntry
        {
            Password = _password,
            Description = DescriptionText.Text?.Replace($"{LocalizationManager.T("PwdMatched_DescriptionLabel")}: ", "") ?? "",
        };
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
