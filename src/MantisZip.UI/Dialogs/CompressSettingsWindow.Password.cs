using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// Password management partial for CompressSettingsWindow
/// </summary>
public partial class CompressSettingsWindow : Window
{
    private readonly List<Core.PasswordEntry> _allPasswordEntries = new();

    private Core.PasswordEntry? _selectedLibraryEntry;

    private bool _isUsingLibrary = true; // true=瀵嗙爜搴? false=鏂板瘑鐮?
    private bool _isPwdRevealed;

    private void EncryptCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var enabled = EncryptCheckBox.IsChecked == true;
        PasswordContentGrid.IsEnabled = enabled;
        if (!enabled)
        {
            _selectedLibraryEntry = null;
            PwdSelectedStatus.Text = L.T(L.Compress_Pwd_NoEntry);
        }
    }

    private void PasswordSource_Changed(object sender, RoutedEventArgs e)
    {
        _isUsingLibrary = LibraryRadio.IsChecked == true;
        UpdatePasswordSourceUI();
    }

    /// <summary>
    /// 鍚屾瀵嗙爜鏉ユ簮 RadioButton 鐨勫惎鐢?绂佺敤鍜岄€忔槑搴︾姸鎬?
    /// </summary>
    private void UpdatePasswordSourceUI()
    {
        // Null guards: controls in Password tab may not be created yet during InitializeComponent
        // 鍙帶鍒跺唴瀹归潰鏉跨殑鍚敤/閫忔槑搴︼紝RadioButton 鏈韩濮嬬粓鍙敤
        if (PwdLibraryContent != null)
        {
            PwdLibraryContent.IsEnabled = _isUsingLibrary;
            PwdLibraryContent.Opacity = _isUsingLibrary ? 1.0 : 0.3;
        }
        if (NewPwdContent != null)
        {
            NewPwdContent.IsEnabled = !_isUsingLibrary;
            NewPwdContent.Opacity = _isUsingLibrary ? 0.3 : 1.0;
        }

        if (_isUsingLibrary)
        {
            if (PwdSaveCheck != null)
                PwdSaveCheck.Content = L.T(L.Compress_Pwd_UpdateRules);
            if (PwdDescBox != null)
            {
                PwdDescBox.IsEnabled = false;
                PwdDescBox.IsReadOnly = true;
                if (_selectedLibraryEntry != null)
                    PwdDescBox.Text = _selectedLibraryEntry.Description;
            }
            // 閫夋嫨瀵嗙爜搴撴潯鐩椂涓嶈鐩栬鍒欐鍐呭锛岃鍒欏缁堢敱鑷姩瑙勫垯鎴栫敤鎴锋墜鍔ㄧ淮鎶?
            if (PwdAutoRules != null && PwdAutoRules.IsChecked == true && PwdRulesBox != null)
                RefreshAutoRules();
        }
        else
        {
            if (PwdSaveCheck != null)
                PwdSaveCheck.Content = L.T(L.Compress_Pwd_SaveToLibrary);
            if (PwdDescBox != null)
            {
                PwdDescBox.IsEnabled = true;
                PwdDescBox.IsReadOnly = false;
                PwdDescBox.Text = "";
            }
            if (PwdAutoRules != null && PwdAutoRules.IsChecked == true)
                RefreshAutoRules();
        }
    }

    /// <summary>
    /// 鍔犺浇瀵嗙爜鍒楄〃鍒?ListBox锛堟寜 LastUsed 闄嶅簭锛?
    /// </summary>
    private void LoadPasswordLibrary()
    {
        _allPasswordEntries.Clear();
        _allPasswordEntries.AddRange(PasswordManager.Instance.GetAllPasswords()
            .OrderByDescending(e => e.LastUsed ?? DateTime.MinValue));
        App.TraceLog("LoadPasswordLibrary: loaded {0} entries", _allPasswordEntries.Count);
        ApplyPasswordFilter();
    }

    /// <summary>
    /// 鏍规嵁鎼滅储璇嶈繃婊ゅ瘑鐮佸垪琛?
    /// </summary>
    private void ApplyPasswordFilter()
    {
        var query = PwdSearchBox.Text?.Trim() ?? "";
        var placeholder = L.T(L.Compress_Pwd_Search);

        // 鎼滅储妗嗙敤 {l:L} 鍗犱綅鏂囧瓧浣滀负 Text锛岃繖浼氬湪杩囨护鏃惰杩囨护鎺夋墍鏈夋潯鐩€?
        // 鎶婂崰浣嶆枃瀛楃瓑鍚屼负绌烘煡璇紝鐩村埌鐢ㄦ埛瀹為檯杈撳叆鎼滅储璇嶃€?
        if (string.Equals(query, placeholder, StringComparison.OrdinalIgnoreCase))
            query = "";

        var filtered = _allPasswordEntries
            .Where(e => string.IsNullOrEmpty(query)
                || e.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.Patterns.Any(p => p.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Null guard: PwdLibraryList may not be created yet during InitializeComponent
        // (PwdSearchBox.TextChanged fires before PwdLibraryList is created in XAML order)
        if (PwdLibraryList == null) return;

        App.TraceLog("ApplyPasswordFilter: query='{0}', total={1}, filtered={2}", query, _allPasswordEntries.Count, filtered.Count);

        if (filtered.Count == 0 && !string.IsNullOrEmpty(query))
        {
            PwdLibraryList.ItemsSource = null;
            PwdLibraryList.Items.Add(L.T(L.Compress_Pwd_EmptySearch));
        }
        else
        {
            PwdLibraryList.ItemsSource = filtered;
        }
    }

    /// <summary>
    /// 鎼滅储妗嗚幏寰楃劍鐐规椂娓呴櫎鍗犱綅鏂囧瓧
    /// </summary>
    private void PwdSearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (PwdSearchBox.Text == L.T(L.Compress_Pwd_Search))
            PwdSearchBox.Text = "";
    }

    private void PwdSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyPasswordFilter();
    }

    private void PwdLibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PwdLibraryList.SelectedItem is Core.PasswordEntry entry)
        {
            _selectedLibraryEntry = entry;
            PwdSelectedStatus.Text = L.TF(L.Compress_Pwd_Selected, entry.Description);

            // 鍚屾鍒板叡浜尯
            PwdDescBox.Text = entry.Description;
            PwdDescBox.IsReadOnly = _isUsingLibrary;
            PwdDescBox.IsEnabled = !_isUsingLibrary;

            // 閫変腑瀵嗙爜搴撴潯鐩椂娓呯┖鏂板瘑鐮佽緭鍏ユ锛堣璁℃枃妗?5锛?
            PasswordBox.Password = "";
            if (PwdTextBox != null) PwdTextBox.Text = "";
            ConfirmPasswordBox.Password = "";
            if (ConfirmPwdTextBox != null) ConfirmPwdTextBox.Text = "";
        }
        else
        {
            PwdSelectedStatus.Text = L.T(L.Compress_Pwd_NoEntry);
        }
    }

    /// <summary>
    /// 鍒囨崲瀵嗙爜鏄庢枃/鎺╃爜 鈥?鍦?PasswordBox 鍜?TextBox 涔嬮棿鍒囨崲
    /// </summary>
    private void PwdRevealBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPwdRevealed = !_isPwdRevealed;

        if (_isPwdRevealed)
        {
            // 鍒囨崲鍒版槑鏂?TextBox锛堜富瀵嗙爜 + 纭瀵嗙爜锛?
            PwdTextBox.Text = PasswordBox.Password;
            if (ConfirmPwdTextBox != null) ConfirmPwdTextBox.Text = ConfirmPasswordBox.Password;

            PasswordBox.Visibility = Visibility.Collapsed;
            PwdTextBox.Visibility = Visibility.Visible;
            ConfirmPasswordBox.Visibility = Visibility.Collapsed;
            if (ConfirmPwdTextBox != null) ConfirmPwdTextBox.Visibility = Visibility.Visible;

            PwdTextBox.Focus();
            PwdTextBox.SelectionStart = PwdTextBox.Text.Length;
        }
        else
        {
            // 鍒囨崲鍥炴帺鐮?PasswordBox锛堜富瀵嗙爜 + 纭瀵嗙爜锛?
            PasswordBox.Password = PwdTextBox.Text;
            ConfirmPasswordBox.Password = ConfirmPwdTextBox?.Text ?? "";

            PwdTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            if (ConfirmPwdTextBox != null) ConfirmPwdTextBox.Visibility = Visibility.Collapsed;
            ConfirmPasswordBox.Visibility = Visibility.Visible;

            PasswordBox.Focus();
        }
    }

    /// <summary>
    /// 瀵嗙爜妗嗗唴瀹瑰彉鍖栨椂锛氭洿鏂板己搴?+ 娓呴櫎瀵嗙爜搴撻€変腑
    /// </summary>
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        OnPasswordContentChanged(PasswordBox.Password);
    }

    /// <summary>
    /// 鏄庢枃瀵嗙爜妗嗗唴瀹瑰彉鍖栨椂锛氬悓姝ュ鐞?
    /// </summary>
    private void PwdTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        OnPasswordContentChanged(PwdTextBox.Text);
    }

    private void OnPasswordContentChanged(string? content)
    {
        UpdatePasswordStrength();

        // 鐢ㄦ埛鎵嬪姩杈撳叆瀵嗙爜鏃讹紝鍙栨秷瀵嗙爜搴撶殑閫変腑
        if (!string.IsNullOrEmpty(content))
        {
            _selectedLibraryEntry = null;
            PwdLibraryList.SelectedItem = null;
            PwdSelectedStatus.Text = L.T(L.Compress_Pwd_NoEntry);
        }

        // 濡傛灉瀵嗙爜搴撴湁閫変腑浣嗙敤鎴锋敼浜嗗瘑鐮佹锛岃嚜鍔ㄥ垏鎹㈠埌鏂板瘑鐮佹ā寮?
        if (_isUsingLibrary && _selectedLibraryEntry == null)
        {
            NewPwdRadio.IsChecked = true;
        }
    }

    /// <summary>
    /// 鏇存柊瀵嗙爜寮哄害鎸囩ず
    /// </summary>
    private void UpdatePasswordStrength()
    {
        var pwd = PasswordBox.Password;
        if (string.IsNullOrEmpty(pwd))
        {
            PwdStrengthText.Text = "";
            PwdStrengthText.ClearValue(TextBlock.ForegroundProperty);
            return;
        }

        var hasUpper = pwd.Any(char.IsUpper);
        var hasLower = pwd.Any(char.IsLower);
        var hasDigit = pwd.Any(char.IsDigit);
        var hasSpecial = pwd.Any(c => !char.IsLetterOrDigit(c));
        var variety = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);

        if (pwd.Length < 6 || (pwd.Length < 10 && variety <= 2))
        {
            PwdStrengthText.Text = "鈼?" + L.T(L.Compress_Pwd_Strength_Weak);
            PwdStrengthText.Foreground = new SolidColorBrush(Colors.Red);
        }
        else if (pwd.Length >= 10 && variety >= 3)
        {
            PwdStrengthText.Text = "鈼?" + L.T(L.Compress_Pwd_Strength_Strong);
            PwdStrengthText.Foreground = new SolidColorBrush(Colors.Green);
        }
        else
        {
            PwdStrengthText.Text = "鈼?" + L.T(L.Compress_Pwd_Strength_Medium);
            PwdStrengthText.Foreground = new SolidColorBrush(Colors.Orange);
        }
    }

    /// <summary>
    /// 鑷姩瑙勫垯 CheckBox 鍒囨崲
    /// </summary>
    private void PwdAutoRules_Changed(object sender, RoutedEventArgs e)
    {
        // Null guard: PwdRulesBox may not be created yet during InitializeComponent
        if (PwdRulesBox != null)
        {
            var auto = PwdAutoRules.IsChecked == true;
            PwdRulesBox.IsReadOnly = auto;
            PwdRulesBox.IsEnabled = !auto;
        }
        if (PwdAutoRules.IsChecked == true)
        {
            // 鏃犺搴撴ā寮忚繕鏄柊瀵嗙爜妯″紡锛岃嚜鍔ㄨ鍒欓兘鍩轰簬杈撳嚭妯″紡鐢熸垚锛屼笉瑕嗙洊涓洪€変腑鏉＄洰鐨勮鍒?
            RefreshAutoRules();
        }
    }

    /// <summary>
    /// 鑾峰彇褰撳墠婵€娲荤殑瀵嗙爜
    /// </summary>
    private string? GetActivePassword()
    {
        if (EncryptCheckBox.IsChecked != true)
            return null;

        if (_isUsingLibrary)
            return _selectedLibraryEntry?.Password;

        // 鏄庢枃妯″紡涓嬬‘淇?PasswordBox 涓?TextBox 鍚屾锛堢敤鎴峰彲鑳藉湪 TextBox 涓緭鍏ワ級
        if (_isPwdRevealed)
        {
            if (PwdTextBox != null) PasswordBox.Password = PwdTextBox.Text;
            if (ConfirmPwdTextBox != null) ConfirmPasswordBox.Password = ConfirmPwdTextBox.Text;
        }

        return PasswordBox.Password;
    }

    /// <summary>
    /// 鍒锋柊鑷姩瑙勫垯锛堟牴鎹緭鍑烘ā寮忕敓鎴愬帇缂╁寘鍚?glob锛?
    /// </summary>
    private void RefreshAutoRules()
    {
        if (!PwdAutoRules.IsChecked == true) return;

        var format = (FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "zip";
        var ext = format == "tar.gz" ? ".tar.gz" : "." + format;

        switch (_outputMode)
        {
            case CompressOutputMode.Manual:
                var manualPath = OutputPathTextBox.Text?.Trim();
                if (!string.IsNullOrEmpty(manualPath))
                {
                    var name = Path.GetFileNameWithoutExtension(manualPath);
                    PwdRulesBox.Text = $"{name}*{ext}";
                }
                break;

            case CompressOutputMode.Separate:
                var rules = new List<string>();
                foreach (var src in _sourcePaths)
                {
                    string baseName;
                    if (File.Exists(src))
                        baseName = Path.GetFileNameWithoutExtension(src);
                    else if (Directory.Exists(src))
                        baseName = Path.GetFileName(src.TrimEnd('\\', '/'));
                    else
                        continue;
                    rules.Add($"{baseName}*{ext}");
                }
                PwdRulesBox.Text = string.Join("\r\n", rules);
                break;

            case CompressOutputMode.Combined:
                var commonParent = App.FindCommonParent(_sourcePaths.ToList());
                if (commonParent != null && !App.IsDriveRoot(commonParent))
                {
                    var archiveName = Path.GetFileName(commonParent.TrimEnd('\\', '/'));
                    PwdRulesBox.Text = $"{archiveName}*{ext}";
                }
                break;
        }
    }

    /// <summary>
    /// 淇濆瓨瀵嗙爜鍒板瘑鐮佸簱锛堝帇缂╂垚鍔熷悗璋冪敤锛?
    /// </summary>
    private void SavePasswordAfterCompress()
    {
        if (PwdSaveCheck.IsChecked != true) return;
        if (EncryptCheckBox.IsChecked != true) return;

        try
        {
            var rules = PwdRulesBox.Text
                ?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .ToList() ?? new List<string>();

            if (rules.Count == 0)
            {
                // 鑷姩鐢熸垚涓€鏉¤鍒?
                var format = (FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "zip";
                var ext = format == "tar.gz" ? ".tar.gz" : "." + format;
                rules.Add($"*{ext}");
            }

            if (_isUsingLibrary && _selectedLibraryEntry != null)
            {
                // 鏇存柊鍖归厤瑙勫垯锛氬幓閲嶈拷鍔?
                var updated = false;
                foreach (var rule in rules)
                {
                    if (!_selectedLibraryEntry.Patterns.Contains(rule))
                    {
                        _selectedLibraryEntry.Patterns.Add(rule);
                        updated = true;
                    }
                }
                if (updated)
                {
                    PasswordManager.Instance.UpdatePassword(
                        _selectedLibraryEntry.Id,
                        _selectedLibraryEntry.Password,
                        _selectedLibraryEntry.Description,
                        _selectedLibraryEntry.Patterns);
                    PasswordManager.Instance.MarkUsed(_selectedLibraryEntry.Id);
                    App.LogDebug("Password rules updated for entry: {0}", _selectedLibraryEntry.Description);
                }
            }
            else if (!_isUsingLibrary)
            {
                // 鏂板瀵嗙爜鏉＄洰
                var password = PasswordBox.Password;
                var desc = PwdDescBox.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(desc))
                    desc = $"Compressed on {DateTime.Now:yyyy-MM-dd HH:mm}";

                PasswordManager.Instance.AddPassword(password, desc, rules);
                App.LogDebug("Password saved to library: {0}", desc);
            }
        }
        catch (Exception ex)
        {
            App.LogDebug("SavePasswordAfterCompress failed: {0}", ex.Message);
        }
    }

    /// <summary>
    /// 鍒囨崲鍒板姞瀵嗛€夐」鍗℃椂缁熶竴鍒锋柊鎵€鏈?UI 鐘舵€?
    /// </summary>
    private void RefreshPasswordTabUI()
    {
        UpdatePasswordFormatState();
        UpdatePasswordSourceUI();

        // 閲嶆柊搴旂敤鑷姩瑙勫垯鐘舵€侊紝纭繚 PwdRulesBox 绂佺敤鎬佹纭樉绀?
        if (PwdRulesBox != null)
        {
            var auto = PwdAutoRules.IsChecked == true;
            PwdRulesBox.IsReadOnly = auto;
            PwdRulesBox.IsEnabled = !auto;
        }

        LoadPasswordLibrary();
    }

    private void UpdatePasswordFormatState()
    {
        // Null guard: PasswordTab TabItem may not be created yet during InitializeComponent
        // (FormatComboBox_SelectionChanged fires before TabItem (Password) is created)
        if (PasswordTab == null) return;

        var tag = (FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString();
        var canEncrypt = tag == "zip" || tag == "7z";
        PasswordTab.IsEnabled = canEncrypt;
        if (!canEncrypt && EncryptCheckBox.IsChecked == true)
        {
            EncryptCheckBox.IsChecked = false;
        }
    }
}
