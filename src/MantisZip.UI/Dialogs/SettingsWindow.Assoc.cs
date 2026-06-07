using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

partial class SettingsWindow
{
    // ── 文件关联 ──────────────────────────────────────────────

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    /// <summary>内置扩展名映射表（扩展名、描述键、AppSettings 属性名）。</summary>
    private static readonly (string Extension, string DescriptionKey, string SettingsProperty)[] BuiltinAssocFormats = new[]
    {
        (".zip",    nameof(L.Settings_Assoc_FormatDesc_Zip),   nameof(AppSettings.AssocZip)),
        (".7z",     nameof(L.Settings_Assoc_FormatDesc_7z),    nameof(AppSettings.Assoc7z)),
        (".rar",    nameof(L.Settings_Assoc_FormatDesc_Rar),   nameof(AppSettings.AssocRar)),
        (".tar",    nameof(L.Settings_Assoc_FormatDesc_Tar),   nameof(AppSettings.AssocTar)),
        (".tar.gz", nameof(L.Settings_Assoc_FormatDesc_TarGz), nameof(AppSettings.AssocTarGz)),
        (".gz",     nameof(L.Settings_Assoc_FormatDesc_Gz),    nameof(AppSettings.AssocGz)),
        (".iso",    nameof(L.Settings_Assoc_FormatDesc_Iso),   nameof(AppSettings.AssocIso)),
    };

    #region L.T(L.Settings_Tab_FileAssoc)

    /// <summary>
    /// 构建并刷新文件关联列表。
    /// </summary>
    private void PopulateAssocList()
    {
        var s = AppSettings.Instance;
        AssocFormatList.Items.Clear();

        // 内置扩展名
        foreach (var fmt in BuiltinAssocFormats)
        {
            var prop = typeof(AppSettings).GetProperty(fmt.SettingsProperty);
            var isEnabled = prop?.GetValue(s) is true;
            var item = new FormatAssocItem
            {
                Extension = fmt.Extension,
                Description = L.T(fmt.DescriptionKey),
                Icon = SystemIconHelper.GetFileIcon(fmt.Extension),
                SettingsProperty = fmt.SettingsProperty,
                IsEnabled = isEnabled,
                IsCustom = false,
                CurrentHandler = ShellIntegration.GetCurrentHandler(fmt.Extension),
                Status = ShellIntegration.GetAssociationStatus(fmt.Extension)
            };
            AssocFormatList.Items.Add(item);
        }

        // 自定义扩展名
        foreach (var ext in s.CustomAssocExtensions)
        {
            var item = new FormatAssocItem
            {
                Extension = ext,
                Description = string.Format(L.T(L.Settings_Assoc_UserCustom), ext),
                Icon = SystemIconHelper.GetFileIcon(ext),
                IsEnabled = true,
                IsCustom = true,
                CurrentHandler = ShellIntegration.GetCurrentHandler(ext),
                Status = ShellIntegration.GetAssociationStatus(ext)
            };
            AssocFormatList.Items.Add(item);
        }

        UpdateAssocButtonState();
        UpdateAssocStatus();
    }

    /// <summary>
    /// 更新安装按钮文字和启用状态（按勾选数量）。
    /// </summary>
    private void UpdateAssocButtonState()
    {
        int checkedCount = 0;
        foreach (var item in AssocFormatList.Items)
            if (item is FormatAssocItem fi && fi.IsEnabled)
                checkedCount++;

        InstallAssocBtn.Content = checkedCount > 0
            ? string.Format(L.T(L.Settings_Assoc_InstallBtnText), checkedCount)
            : L.T(L.Settings_Assoc_Install);
        InstallAssocBtn.IsEnabled = checkedCount > 0;
    }

    /// <summary>
    /// 更新已安装关联数量的状态文本。
    /// </summary>
    private void UpdateAssocStatus()
    {
        int installed = ShellIntegration.GetInstalledExtensionCount();
        int total = BuiltinAssocFormats.Length;
        AssocStatusText.Text = string.Format(L.T(L.Settings_Assoc_StatusText), installed, total);
        UninstallAssocBtn.IsEnabled = installed > 0;
    }

    /// <summary>
    /// 行点击切换勾选。
    /// </summary>
    private void FormatRow_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe && fe.DataContext is FormatAssocItem item)
        {
            item.IsEnabled = !item.IsEnabled;
            UpdateAssocButtonState();
        }
    }

    private void SelectAllBtn_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in AssocFormatList.Items)
            if (item is FormatAssocItem fi)
                fi.IsEnabled = true;
        UpdateAssocButtonState();
    }

    private void DeselectAllBtn_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in AssocFormatList.Items)
            if (item is FormatAssocItem fi)
                fi.IsEnabled = false;
        UpdateAssocButtonState();
    }

    /// <summary>
    /// 弹出对话框添加自定义扩展名。
    /// </summary>
    private void AddCustomBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = L.T(L.Settings_Assoc_CustomInputTitle),
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = this.Background
        };
        var stack = new StackPanel { Margin = new Thickness(12) };
        var promptText = new TextBlock
        {
            Text = L.T(L.Settings_Assoc_CustomInputPrompt),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        var inputBox = new TextBox { Margin = new Thickness(0, 0, 0, 12) };
        var errorText = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.Red,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var okBtn = new Button
        {
            Content = "OK",
            Width = 70,
            Height = 24,
            IsDefault = true,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var cancelBtn = new Button
        {
            Content = L.T(L.MsgBox_Cancel),
            Width = 70,
            Height = 24,
            IsCancel = true
        };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        stack.Children.Add(promptText);
        stack.Children.Add(inputBox);
        stack.Children.Add(errorText);
        stack.Children.Add(btnPanel);
        dialog.Content = stack;

        okBtn.Click += (_, _) =>
        {
            var ext = inputBox.Text?.Trim().ToLowerInvariant() ?? "";
            if (!ext.StartsWith(".")) ext = "." + ext;

            // 验证格式
            if (ext.Length > 10 || ext.Length < 2 || ext.Contains(' ') || ext.Count(c => c == '.') > 1 || ext.Any(c => !char.IsLetterOrDigit(c) && c != '.'))
            {
                errorText.Text = L.T(L.Settings_Assoc_CustomInvalid);
                return;
            }

            // 检查是否已存在（内置 + 自定义）
            bool isDuplicate = false;
            foreach (var item in AssocFormatList.Items)
            {
                if (item is FormatAssocItem fi && fi.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase))
                {
                    isDuplicate = true;
                    break;
                }
            }
            if (isDuplicate)
            {
                errorText.Text = L.T(L.Settings_Assoc_CustomAlreadyExists);
                return;
            }

            // 检查自定义扩展名数量上限
            int customCount = 0;
            foreach (var item in AssocFormatList.Items)
                if (item is FormatAssocItem fi && fi.IsCustom) customCount++;
            if (customCount >= 20)
            {
                errorText.Text = L.T(L.Settings_Assoc_CustomMaxReached);
                return;
            }

            dialog.DialogResult = true;
        };

        if (dialog.ShowDialog() == true)
        {
            var ext = inputBox.Text?.Trim().ToLowerInvariant() ?? "";
            if (!ext.StartsWith(".")) ext = "." + ext;

            var newItem = new FormatAssocItem
            {
                Extension = ext,
                Description = string.Format(L.T(L.Settings_Assoc_UserCustom), ext),
                Icon = SystemIconHelper.GetFileIcon(ext),
                IsEnabled = true,
                IsCustom = true,
                CurrentHandler = ShellIntegration.GetCurrentHandler(ext)
            };
            AssocFormatList.Items.Add(newItem);
            UpdateAssocButtonState();
        }
    }

    /// <summary>
    /// 删除自定义扩展名。
    /// </summary>
    private void DeleteCustomBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is FormatAssocItem item && item.IsCustom)
        {
            AssocFormatList.Items.Remove(item);
            UpdateAssocButtonState();
        }
    }

    private void InstallAssoc_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("SettingsWindow: InstallAssoc_Click");
        try
        {
            // 1. 预备工作：清理旧版单 ProgId + 注册 Applications 条目
            ShellIntegration.PrepareAssocRegistration();

            // 2. 只安装用户勾选的扩展名（内置 + 自定义）
            foreach (var item in AssocFormatList.Items)
            {
                if (item is FormatAssocItem fi && fi.IsEnabled)
                {
                    ShellIntegration.InstallAssociationForExtension(fi.Extension);
                }
            }

            // 3. 卸载已安装但未勾选的扩展名（清理旧关联）
            foreach (var item in AssocFormatList.Items)
            {
                if (item is FormatAssocItem fi && !fi.IsEnabled)
                {
                    ShellIntegration.UninstallAssociationForExtension(fi.Extension);
                }
            }

            // 4. 刷新所有项的当前关联程序显示和关联状态
            foreach (var item in AssocFormatList.Items)
            {
                if (item is FormatAssocItem fi)
                {
                    fi.CurrentHandler = ShellIntegration.GetCurrentHandler(fi.Extension);
                    fi.Status = ShellIntegration.GetAssociationStatus(fi.Extension);
                }
            }

            // 5. 保存勾选状态到 settings.json
            SaveAssocSettings();
            AppSettings.Instance.Save();

            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            UpdateAssocStatus();
            App.LogDebug("SettingsWindow: file associations installed for selected formats");
            AppMessageBox.ShowWithAction(
                L.T(L.Settings_Assoc_InstalledMsg) + "\n\n" + L.T(L.Settings_Assoc_SetDefaultHint),
                L.T(L.App_MantisZipTitle),
                L.T(L.Settings_Assoc_OpenDefaultApps),
                () =>
                {
                    try { Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }); }
                    catch (Exception ex) { App.LogDebug("SettingsWindow: failed to open default apps: {0}", ex.Message); }
                });
        }
        catch (Exception ex)
        {
            App.LogDebug("SettingsWindow: assoc install failed: {0}", ex.Message);
            AppMessageBox.Show(L.TF(L.Settings_Menu_InstallFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UninstallAssoc_Click(object sender, RoutedEventArgs e)
    {
        App.LogDebug("SettingsWindow: UninstallAssoc_Click");
        try
        {
            ShellIntegration.UninstallAssociations();

            // 刷新所有项的当前关联程序显示和关联状态
            foreach (var item in AssocFormatList.Items)
            {
                if (item is FormatAssocItem fi)
                {
                    fi.CurrentHandler = ShellIntegration.GetCurrentHandler(fi.Extension);
                    fi.Status = ShellIntegration.GetAssociationStatus(fi.Extension);
                }
            }

            // 保存勾选状态
            SaveAssocSettings();
            AppSettings.Instance.Save();

            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            UpdateAssocStatus();
            App.LogDebug("SettingsWindow: file associations uninstalled");
            AppMessageBox.Show(L.T(L.Settings_Assoc_UninstalledMsg), L.T(L.App_MantisZipTitle), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.LogDebug("SettingsWindow: assoc uninstall failed: {0}", ex.Message);
            AppMessageBox.Show(L.TF(L.Settings_Menu_UninstallFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 打开系统默认应用设置。
    /// </summary>
    private void OpenDefaultAppsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>
    /// 保存文件关联设置到 AppSettings。
    /// </summary>
    private void SaveAssocSettings()
    {
        var s = AppSettings.Instance;
        s.CustomAssocExtensions.Clear();
        foreach (var item in AssocFormatList.Items)
        {
            if (item is FormatAssocItem fi)
            {
                if (!fi.IsCustom && !string.IsNullOrEmpty(fi.SettingsProperty))
                {
                    var prop = typeof(AppSettings).GetProperty(fi.SettingsProperty);
                    if (prop != null)
                        prop.SetValue(s, fi.IsEnabled);
                }
                if (fi.IsCustom)
                {
                    s.CustomAssocExtensions.Add(fi.Extension);
                }
            }
        }
    }

    #endregion
}

/// <summary>
/// 文件关联列表中每行的数据模型。
/// </summary>
internal class FormatAssocItem : INotifyPropertyChanged
{
    public string Extension { get; init; } = "";
    public string Description { get; init; } = "";
    public System.Windows.Media.ImageSource? Icon { get; set; }
    public string SettingsProperty { get; init; } = "";
    public bool IsCustom { get; init; }

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    private string _currentHandler = "";
    public string CurrentHandler
    {
        get => _currentHandler;
        set
        {
            if (_currentHandler != value)
            {
                _currentHandler = value;
                OnPropertyChanged();
            }
        }
    }

    private AssocStatus _status;
    public AssocStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDefaultStatus));
                OnPropertyChanged(nameof(IsNotDefaultStatus));
            }
        }
    }

    public bool IsDefaultStatus => _status == AssocStatus.IsDefault;
    public bool IsNotDefaultStatus => _status == AssocStatus.NotDefault;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
