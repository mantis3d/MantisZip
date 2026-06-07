using System.IO;
using System.Windows;
using System.Collections.ObjectModel;
using MantisZip.Core;
using MantisZip.UI.Localization;
using Microsoft.Win32;

namespace MantisZip.UI;

/// <summary>
/// 密码管理器窗口。密码默认以掩码显示，可切换明文。
/// </summary>
public partial class PasswordManagerWindow : Window
{
    private bool _showPasswords;

    public PasswordManagerWindow()
    {
        InitializeComponent();
        App.ApplyTextRenderingMode(this);
        var showByDefault = AppSettings.Instance.PasswordRevealByDefault;
        LoadPasswords(showPasswords: showByDefault);
        if (showByDefault)
            ShowPwdBtn.Content = L.T(L.PwdMgr_HidePwd);
    }

    private void LoadPasswords(bool showPasswords)
    {
        _showPasswords = showPasswords;
        var passwords = new ObservableCollection<PasswordEntryView>();

        foreach (var entry in PasswordManager.Instance.GetAllPasswords())
        {
            passwords.Add(new PasswordEntryView
            {
                Id = entry.Id,
                Password = showPasswords ? entry.Password : "••••••••",
                Description = entry.Description,
                PatternDisplay = string.Join(", ", entry.Patterns),
                CreatedAt = entry.CreatedAt,
                LastUsed = entry.LastUsed
            });
        }

        PasswordGrid.ItemsSource = passwords;
        PwdCounter.Text = $"{PasswordManager.Instance.EntryCount} / {PasswordManager.MaxEntries}";
    }

    /// <summary>
    /// 切换密码明文/掩码显示。
    /// </summary>
    private void ToggleShowPasswords_Click(object sender, RoutedEventArgs e)
    {
        var show = !_showPasswords;
        LoadPasswords(show);
        ShowPwdBtn.Content = show ? L.T(L.PwdMgr_HidePwd) : L.T(L.PwdMgr_ShowPwd);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("PasswordManagerWindow: Add_Click, current count={0}", PasswordManager.Instance.EntryCount);
        if (PasswordManager.Instance.EntryCount >= PasswordManager.MaxEntries)
        {
            App.LogDebug("PasswordManagerWindow: add rejected, max entries ({0}) reached", PasswordManager.MaxEntries);
            AppMessageBox.Show(L.TF(L.PwdMgr_Full, PasswordManager.MaxEntries),
                L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new PasswordEditDialog();
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            try
            {
                PasswordManager.Instance.AddPassword(dialog.ResultPassword, dialog.ResultDescription, dialog.ResultPatterns);
                App.LogDebug("PasswordManagerWindow: password added, desc='{0}'", dialog.ResultDescription);
            }
            catch (Exception ex) { App.LogDebug("PasswordManagerWindow: add failed: {0}", ex.Message); AppMessageBox.Show(L.TF(L.Password_SaveFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error); }
            LoadPasswords(_showPasswords);
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordGrid.SelectedItem is not PasswordEntryView entry) return;
        App.LogDebug("PasswordManagerWindow: Edit_Click, id={0}, desc='{1}'", entry.Id, entry.Description);

        var dialog = new PasswordEditDialog(entry.Id, entry.Password, entry.Description, entry.PatternDisplay);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            try
            {
                PasswordManager.Instance.UpdatePassword(entry.Id, dialog.ResultPassword, dialog.ResultDescription, dialog.ResultPatterns);
                App.LogDebug("PasswordManagerWindow: password updated, id={0}, desc='{1}'", entry.Id, dialog.ResultDescription);
            }
            catch (Exception ex) { App.LogDebug("PasswordManagerWindow: update failed: {0}", ex.Message); AppMessageBox.Show(L.TF(L.Password_UpdateFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error); }
            LoadPasswords(_showPasswords);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordGrid.SelectedItem is not PasswordEntryView entry) return;
        App.LogDebug("PasswordManagerWindow: Delete_Click, id={0}, desc='{1}'", entry.Id, entry.Description);

        var result = AppMessageBox.Show(
            L.TF(L.Password_DeleteConfirm, entry.Password),
            L.T(L.PwdMgr_DeleteTitle),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                PasswordManager.Instance.DeletePassword(entry.Id);
                App.LogDebug("PasswordManagerWindow: password deleted, id={0}", entry.Id);
            }
            catch (Exception ex) { App.LogDebug("PasswordManagerWindow: delete failed: {0}", ex.Message); AppMessageBox.Show(L.TF(L.Password_DeleteFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error); }
            LoadPasswords(_showPasswords);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("PasswordManagerWindow: Export_Click");
        var dialog = new SaveFileDialog
        {
            Filter = "JSON 文件|*.json",
            FileName = "passwords-export.json"
        };
        if (dialog.ShowDialog() == true)
        {
            App.LogDebug("PasswordManagerWindow: exporting to '{0}'", dialog.FileName);
            try
            {
                var json = PasswordManager.Instance.ExportToJson();
                File.WriteAllText(dialog.FileName, json);
                App.LogDebug("PasswordManagerWindow: export done, {0} entries", PasswordManager.Instance.EntryCount);
                AppMessageBox.Show(L.TF(L.PwdMgr_Export_Success, dialog.FileName),
                    L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                App.LogDebug("PasswordManagerWindow: export failed: {0}", ex.Message);
                AppMessageBox.Show(L.TF(L.PwdMgr_ExportFailed, ex.Message),
                    L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("PasswordManagerWindow: Import_Click");
        var dialog = new OpenFileDialog
        {
            Filter = "JSON 文件|*.json"
        };
        if (dialog.ShowDialog() == true)
        {
            App.LogDebug("PasswordManagerWindow: importing from '{0}'", dialog.FileName);
            // 先解析确认条目数
            string importedJson;
            int entryCount;
            try
            {
                importedJson = File.ReadAllText(dialog.FileName);
                var data = System.Text.Json.JsonSerializer.Deserialize<PasswordData>(importedJson);
                entryCount = data?.Passwords?.Count ?? 0;
            }
            catch (Exception ex)
            {
                App.LogDebug("PasswordManagerWindow: import parse failed: {0}", ex.Message);
                AppMessageBox.Show(L.TF(L.PwdMgr_ImportFailed, ex.Message),
                    L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (entryCount == 0)
            {
                App.LogDebug("PasswordManagerWindow: import file has no entries");
                AppMessageBox.Show(L.T(L.PwdMgr_Import_Empty),
                    L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 预检查：导入后是否超过上限
            var currentCount = PasswordManager.Instance.EntryCount;
            if (currentCount + entryCount > PasswordManager.MaxEntries)
            {
                App.LogDebug("PasswordManagerWindow: import rejected: {0} + {1} > {2} max", currentCount, entryCount, PasswordManager.MaxEntries);
                AppMessageBox.Show(L.TF(L.PwdMgr_Import_Overflow, PasswordManager.MaxEntries - currentCount, entryCount),
                    L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = AppMessageBox.Show(
                L.TF(L.PwdMgr_Import_Confirm, entryCount),
                L.T(L.PwdMgr_Import_Title),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    PasswordManager.Instance.ImportFromJson(importedJson);
                    LoadPasswords(_showPasswords);
                    App.LogDebug("PasswordManagerWindow: import done, {0} entries imported, total={1}", entryCount, PasswordManager.Instance.EntryCount);
                    AppMessageBox.Show(L.TF(L.PwdMgr_Import_Success, entryCount),
                        L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    App.LogDebug("PasswordManagerWindow: import failed: {0}", ex.Message);
                    AppMessageBox.Show(L.TF(L.PwdMgr_ImportFailed, ex.Message),
                        L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PasswordHelpDialog();
        dialog.Owner = this;
        dialog.ShowDialog();
    }
}

public class PasswordEntryView
{
    public string Id { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PatternDisplay { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsed { get; set; }
}