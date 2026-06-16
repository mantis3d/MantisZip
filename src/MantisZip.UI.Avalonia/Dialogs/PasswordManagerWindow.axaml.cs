using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MantisZip.Core;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Dialogs;

public partial class PasswordManagerWindow : Window
{
    private bool _showPasswords;
    private bool _isEditing; // false = adding, true = editing
    private bool _isDeleteConfirm;
    private string? _editingId;

    // ── Localized string properties (bound via DataContext=self) ──
    public string WinTitle => LocalizationManager.T("PasswordManager_Title");
    public string SearchWatermark => LocalizationManager.T("PasswordManager_Search");
    public string DescColumnHeader => LocalizationManager.T("PasswordManager_Description");
    public string RulesColumnHeader => LocalizationManager.T("PasswordManager_Rules");
    public string PasswordColumnHeader => LocalizationManager.T("PasswordManager_Password");
    public string AddText => LocalizationManager.T("PasswordManager_Add");
    public string EditText => LocalizationManager.T("PasswordManager_Edit");
    public string DeleteText => LocalizationManager.T("PasswordManager_Delete");
    public string ShowPwdText => LocalizationManager.T("PasswordManager_Reveal");
    public string CloseText => LocalizationManager.T("PasswordManager_Close");
    public string DescLabel => LocalizationManager.T("PasswordManager_DescLabel");
    public string PasswordLabel => LocalizationManager.T("PasswordManager_PwdLabel");
    public string RulesLabel => LocalizationManager.T("PasswordManager_RulesLabel");
    public string RulesWatermark => LocalizationManager.T("PasswordManager_RulesWatermark");
    public string SaveText => LocalizationManager.T("PasswordManager_Save");
    public string CancelText => LocalizationManager.T("PasswordManager_Cancel");

    public PasswordManagerWindow()
    {
        InitializeComponent();
        DataContext = this;
        LoadPasswords(showPasswords: false);
    }

    // ── Data loading ──

    private void LoadPasswords(bool showPasswords)
    {
        _showPasswords = showPasswords;
        TogglePwdBtn.Content = showPasswords
            ? LocalizationManager.T("PasswordManager_Hide")
            : LocalizationManager.T("PasswordManager_Reveal");
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        var filter = SearchBox.Text?.Trim() ?? "";
        var list = new ObservableCollection<PasswordEntryView>();

        foreach (var entry in PasswordManager.Instance.GetAllPasswords())
        {
            if (!string.IsNullOrEmpty(filter))
            {
                bool matches = entry.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || entry.PatternsDisplay.Contains(filter, StringComparison.OrdinalIgnoreCase);
                if (!matches) continue;
            }

            list.Add(new PasswordEntryView
            {
                Id = entry.Id,
                Password = entry.Password,
                Description = entry.Description,
                PatternDisplay = entry.PatternsDisplay,
                MaskedPassword = _showPasswords ? entry.Password : "••••••••",
            });
        }

        PasswordGrid.ItemsSource = list;
    }

    // ── Search ──

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshGrid();
    }

    // ── Edit panel helpers ──

    private void ResetEditPanel()
    {
        _isDeleteConfirm = false;
        _isEditing = false;
        _editingId = null;
        EditDesc.IsEnabled = true;
        EditPasswordBox.IsEnabled = true;
        EditRules.IsEnabled = true;
        EditDesc.Text = "";
        EditPasswordBox.Text = "";
        EditRules.Text = "";
    }

    private void ShowEditPanel(string title, string saveBtnText)
    {
        EditTitle.Text = title;
        EditSaveBtn.Content = saveBtnText;
        EditPanel.IsVisible = true;
    }

    // ── Add ──

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (PasswordManager.Instance.EntryCount >= PasswordManager.MaxEntries)
        {
            EditTitle.Text = LocalizationManager.T("PasswordManager_FullWarning");
            return;
        }

        ResetEditPanel();
        _isEditing = false;
        ShowEditPanel(LocalizationManager.T("PasswordManager_AddPanelTitle"), LocalizationManager.T("PasswordManager_Save"));
    }

    // ── Edit ──

    private void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (PasswordGrid.SelectedItem is not PasswordEntryView entry) return;

        ResetEditPanel();
        _isEditing = true;
        _editingId = entry.Id;
        EditDesc.Text = entry.Description;
        EditPasswordBox.Text = entry.Password;
        EditRules.Text = entry.PatternDisplay;
        ShowEditPanel(LocalizationManager.T("PasswordManager_EditPanelTitle"), LocalizationManager.T("PasswordManager_Save"));
    }

    private void OnPasswordGridDoubleTapped(object? sender, RoutedEventArgs e)
    {
        OnEditClick(sender, e);
    }

    // ── Edit panel: reveal toggle ──

    private void OnEditRevealToggle(object? sender, RoutedEventArgs e)
    {
        EditPasswordBox.PasswordChar = EditPasswordBox.PasswordChar == '\0' ? '●' : '\0';
    }

    // ── Edit panel: Save (handles add/edit/delete confirm) ──

    private void OnEditSave(object? sender, RoutedEventArgs e)
    {
        if (_isDeleteConfirm)
        {
            if (_editingId != null)
            {
                try
                {
                    PasswordManager.Instance.DeletePassword(_editingId);
                }
                catch (Exception ex)
                {
                    EditTitle.Text = ex.Message;
                    return;
                }
            }
            EditPanel.IsVisible = false;
            ResetEditPanel();
            RefreshGrid();
            return;
        }

        var desc = EditDesc.Text?.Trim() ?? "";
        var pwd = EditPasswordBox.Text ?? "";
        var rulesText = EditRules.Text?.Trim() ?? "";
        var patterns = string.IsNullOrEmpty(rulesText)
            ? new List<string>()
            : rulesText.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .ToList();

        try
        {
            if (_isEditing && _editingId != null)
            {
                PasswordManager.Instance.UpdatePassword(_editingId, pwd, desc, patterns);
            }
            else
            {
                if (PasswordManager.Instance.EntryCount >= PasswordManager.MaxEntries)
                {
                    EditTitle.Text = LocalizationManager.T("PasswordManager_FullWarning");
                    return;
                }
                PasswordManager.Instance.AddPassword(pwd, desc, patterns);
            }
        }
        catch (Exception ex)
        {
            EditTitle.Text = ex.Message;
            return;
        }

        EditPanel.IsVisible = false;
        ResetEditPanel();
        RefreshGrid();
    }

    // ── Edit panel: Cancel ──

    private void OnEditCancel(object? sender, RoutedEventArgs e)
    {
        EditPanel.IsVisible = false;
        ResetEditPanel();
    }

    // ── Delete ──

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (PasswordGrid.SelectedItem is not PasswordEntryView entry) return;

        ResetEditPanel();
        _isDeleteConfirm = true;
        _editingId = entry.Id;
        EditDesc.Text = "";
        EditPasswordBox.Text = "";
        EditRules.Text = "";
        EditDesc.IsEnabled = false;
        EditPasswordBox.IsEnabled = false;
        EditRules.IsEnabled = false;
        ShowEditPanel(LocalizationManager.T("PasswordManager_DeleteConfirm"), LocalizationManager.T("PasswordManager_ConfirmYes"));
    }

    // ── Toggle password visibility ──

    private void OnToggleShowPasswords(object? sender, RoutedEventArgs e)
    {
        LoadPasswords(!_showPasswords);
    }

    // ── Close ──

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

/// <summary>
/// View model for each DataGrid row.
/// </summary>
public class PasswordEntryView
{
    public string Id { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PatternDisplay { get; set; } = string.Empty;
    public string MaskedPassword { get; set; } = string.Empty;
}
