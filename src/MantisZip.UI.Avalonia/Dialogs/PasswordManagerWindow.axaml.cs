using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MantisZip.Core;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
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
    public string CreatedColumnHeader => LocalizationManager.T("PasswordManager_Created");
    public string LastUsedColumnHeader => LocalizationManager.T("PasswordManager_LastUsed");
    public string AddText => LocalizationManager.T("PasswordManager_Add");
    public string EditText => LocalizationManager.T("PasswordManager_Edit");
    public string DeleteText => LocalizationManager.T("PasswordManager_Delete");
    public string ShowPwdText => LocalizationManager.T("PasswordManager_Reveal");
    public string CloseText => LocalizationManager.T("PasswordManager_Close");
    public string ExportText => LocalizationManager.T("PasswordManager_Export");
    public string ImportText => LocalizationManager.T("PasswordManager_Import");
    public string DescLabel => LocalizationManager.T("PasswordManager_DescLabel");
    public string PasswordLabel => LocalizationManager.T("PasswordManager_PwdLabel");
    public string RulesLabel => LocalizationManager.T("PasswordManager_RulesLabel");
    public string RulesWatermark => LocalizationManager.T("PasswordManager_RulesWatermark");
    public string SaveText => LocalizationManager.T("PasswordManager_Save");
    public string CancelText => LocalizationManager.T("PasswordManager_Cancel");
    public string TipText => LocalizationManager.T("PasswordManager_Tip");
    public string HelpTooltip => LocalizationManager.T("PasswordManager_HelpTooltip");

    public PasswordManagerWindow()
    {
        InitializeComponent();
        DataContext = this;
        var settings = AppSettings.Load();
        var showByDefault = settings.PasswordRevealByDefault;
        LoadPasswords(showPasswords: showByDefault);
    }

    // ── Data loading ──

    private void LoadPasswords(bool showPasswords)
    {
        _showPasswords = showPasswords;
        TogglePwdIcon.Data = showPasswords
            ? (Geometry?)Application.Current?.FindResource("IconEyeOff")
            : (Geometry?)Application.Current?.FindResource("IconEye");
        TogglePwdLabel.Text = showPasswords
            ? LocalizationManager.T("PasswordManager_Hide")
            : LocalizationManager.T("PasswordManager_Reveal");
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        var filter = SearchBox.Text?.Trim() ?? "";
        var list = new ObservableCollection<PasswordEntryView>();
        var allPasswords = PasswordManager.Instance.GetAllPasswords();

        foreach (var entry in allPasswords)
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
                CreatedAt = entry.CreatedAt.ToString("yyyy-MM-dd"),
                LastUsed = entry.LastUsed?.ToString("yyyy-MM-dd") ?? "",
            });
        }

        PasswordGrid.ItemsSource = list;
        PwdCounter.Text = string.Format(
            LocalizationManager.T("PasswordManager_EntryCounter"),
            PasswordManager.Instance.EntryCount, PasswordManager.MaxEntries);
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
        LogDebug("PasswordManagerWindow: Add_Click, current count={0}", PasswordManager.Instance.EntryCount);
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
        LogDebug("PasswordManagerWindow: Edit_Click, id={0}, desc='{1}'", entry.Id, entry.Description);

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
                    LogDebug("PasswordManagerWindow: password deleted, id={0}", _editingId);
                }
                catch (Exception ex)
                {
                    LogDebug("PasswordManagerWindow: delete failed: {0}", ex.Message);
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
                LogDebug("PasswordManagerWindow: password updated, id={0}, desc='{1}'", _editingId, desc);
            }
            else
            {
                if (PasswordManager.Instance.EntryCount >= PasswordManager.MaxEntries)
                {
                    EditTitle.Text = LocalizationManager.T("PasswordManager_FullWarning");
                    return;
                }
                PasswordManager.Instance.AddPassword(pwd, desc, patterns);
                LogDebug("PasswordManagerWindow: password added, desc='{0}'", desc);
            }
        }
        catch (Exception ex)
        {
            LogDebug("PasswordManagerWindow: save failed: {0}", ex.Message);
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
        LogDebug("PasswordManagerWindow: Delete_Click, id={0}, desc='{1}'", entry.Id, entry.Description);

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
        var show = !_showPasswords;
        LoadPasswords(show);
    }

    // ── Export ──

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        LogDebug("PasswordManagerWindow: Export_Click");

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.T("PasswordManager_Export"),
            DefaultExtension = "json",
            SuggestedFileName = "passwords-export.json",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSON files (*.json)") { Patterns = new[] { "*.json" } }
            }
        });

        if (file == null) return;

        var fullPath = file.TryGetLocalPath();
        if (fullPath == null) return;

        LogDebug("PasswordManagerWindow: exporting to '{0}'", fullPath);
        try
        {
            var json = PasswordManager.Instance.ExportToJson();
            await File.WriteAllTextAsync(fullPath, json);
            LogDebug("PasswordManagerWindow: export done, {0} entries", PasswordManager.Instance.EntryCount);

            await AppMessageBox.Show(
                string.Format(LocalizationManager.T("PasswordManager_ExportSuccess"), fullPath),
                LocalizationManager.T("PasswordManager_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LogDebug("PasswordManagerWindow: export failed: {0}", ex.Message);
            await AppMessageBox.Show(
                string.Format(LocalizationManager.T("PasswordManager_ExportFailed"), ex.Message),
                LocalizationManager.T("PasswordManager_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ── Import ──

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        LogDebug("PasswordManagerWindow: Import_Click");

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.T("PasswordManager_Import"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("JSON files (*.json)") { Patterns = new[] { "*.json" } }
            }
        });

        if (files == null || files.Count == 0) return;
        var file = files[0];

        var filePath = file.TryGetLocalPath();
        if (filePath == null) return;

        LogDebug("PasswordManagerWindow: importing from '{0}'", filePath);

        string importedJson;
        int entryCount;
        try
        {
            importedJson = await File.ReadAllTextAsync(filePath);
            var data = JsonSerializer.Deserialize<PasswordData>(importedJson);
            entryCount = data?.Passwords?.Count ?? 0;
        }
        catch (Exception ex)
        {
            LogDebug("PasswordManagerWindow: import parse failed: {0}", ex.Message);
            await AppMessageBox.Show(
                string.Format(LocalizationManager.T("PasswordManager_ImportFailed"), ex.Message),
                LocalizationManager.T("PasswordManager_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (entryCount == 0)
        {
            LogDebug("PasswordManagerWindow: import file has no entries");
            await AppMessageBox.Show(
                LocalizationManager.T("PasswordManager_ImportEmpty"),
                LocalizationManager.T("PasswordManager_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Pre-check: would import exceed limit?
        var currentCount = PasswordManager.Instance.EntryCount;
        if (currentCount + entryCount > PasswordManager.MaxEntries)
        {
            LogDebug("PasswordManagerWindow: import rejected: {0} + {1} > {2} max", currentCount, entryCount, PasswordManager.MaxEntries);
            await AppMessageBox.Show(
                string.Format(LocalizationManager.T("PasswordManager_ImportOverflow"),
                    PasswordManager.MaxEntries - currentCount, entryCount),
                LocalizationManager.T("PasswordManager_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Confirm import
        var confirmResult = await AppMessageBox.Show(
            string.Format(LocalizationManager.T("PasswordManager_ImportConfirm"), entryCount),
            LocalizationManager.T("PasswordManager_ImportTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmResult == MessageBoxResult.Yes)
        {
            try
            {
                PasswordManager.Instance.ImportFromJson(importedJson);
                RefreshGrid();
                LogDebug("PasswordManagerWindow: import done, {0} entries imported, total={1}", entryCount, PasswordManager.Instance.EntryCount);
                await AppMessageBox.Show(
                    string.Format(LocalizationManager.T("PasswordManager_ImportSuccess"), entryCount),
                    LocalizationManager.T("PasswordManager_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogDebug("PasswordManagerWindow: import failed: {0}", ex.Message);
                await AppMessageBox.Show(
                    string.Format(LocalizationManager.T("PasswordManager_ImportFailed"), ex.Message),
                    LocalizationManager.T("PasswordManager_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    // ── Help ──

    private void OnHelpClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new PasswordHelpDialog();
        dialog.ShowDialog(this);
    }

    // ── Close ──

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    // ── Logging ──

    private static void LogDebug(string format, params object?[] args)
    {
        var msg = string.Format(format, args);
        System.Diagnostics.Debug.WriteLine(msg);
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
    public string CreatedAt { get; set; } = string.Empty;
    public string LastUsed { get; set; } = string.Empty;
}