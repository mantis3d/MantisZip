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
        LoadPasswords(showPasswords: false);
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
        if (PasswordManager.Instance.EntryCount >= PasswordManager.MaxEntries)
        {
            AppMessageBox.Show(L.TF(L.PwdMgr_Full, PasswordManager.MaxEntries),
                L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new PasswordEditDialog();
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            try { PasswordManager.Instance.AddPassword(dialog.ResultPassword, dialog.ResultDescription, dialog.ResultPatterns); }
            catch (Exception ex) { AppMessageBox.Show(L.TF(L.Password_SaveFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error); }
            LoadPasswords(_showPasswords);
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordGrid.SelectedItem is not PasswordEntryView entry) return;

        var dialog = new PasswordEditDialog(entry.Id, entry.Password, entry.Description, entry.PatternDisplay);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            try { PasswordManager.Instance.UpdatePassword(entry.Id, dialog.ResultPassword, dialog.ResultDescription, dialog.ResultPatterns); }
            catch (Exception ex) { AppMessageBox.Show(L.TF(L.Password_UpdateFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error); }
            LoadPasswords(_showPasswords);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordGrid.SelectedItem is not PasswordEntryView entry) return;

        var result = AppMessageBox.Show(
            L.TF(L.Password_DeleteConfirm, entry.Password),
            L.T(L.PwdMgr_DeleteTitle),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try { PasswordManager.Instance.DeletePassword(entry.Id); }
            catch (Exception ex) { AppMessageBox.Show(L.TF(L.Password_DeleteFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error); }
            LoadPasswords(_showPasswords);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON 文件|*.json",
            FileName = "passwords-export.json"
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = PasswordManager.Instance.ExportToJson();
                File.WriteAllText(dialog.FileName, json);
                AppMessageBox.Show(L.TF(L.PwdMgr_Export_Success, dialog.FileName),
                    L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppMessageBox.Show(L.TF(L.PwdMgr_ExportFailed, ex.Message),
                    L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON 文件|*.json"
        };
        if (dialog.ShowDialog() == true)
        {
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
                AppMessageBox.Show(L.TF(L.PwdMgr_ImportFailed, ex.Message),
                    L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (entryCount == 0)
            {
                AppMessageBox.Show(L.T(L.PwdMgr_Import_Empty),
                    L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 预检查：导入后是否超过上限
            var currentCount = PasswordManager.Instance.EntryCount;
            if (currentCount + entryCount > PasswordManager.MaxEntries)
            {
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
                    AppMessageBox.Show(L.TF(L.PwdMgr_Import_Success, entryCount),
                        L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
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