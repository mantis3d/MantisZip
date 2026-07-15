using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Controls;

/// <summary>
/// A panel that shows format-specific compression options based on the SelectedFormat property.
/// Supports ZIP (encoding, compression method), 7z (method, solid, block size, dict size,
/// word size, match finder), and TAR.GZ (placeholder).
///
/// Combo box items are populated from <see cref="CompressionOptionData"/> — the single
/// source of truth shared with <see cref="ViewModels.SettingsWindowViewModel"/>.
/// </summary>
public partial class DynamicFormatOptionsPanel : UserControl
{
    // ── Styled Property ────────────────────────────────────────────────────

    public static readonly StyledProperty<string> SelectedFormatProperty =
        AvaloniaProperty.Register<DynamicFormatOptionsPanel, string>(
            nameof(SelectedFormat), "zip",
            defaultBindingMode: BindingMode.TwoWay);

    // ── Localized Binding Properties ───────────────────────────────────────

    public string ZipPanelTitle => LocalizationManager.T("FormatOptions_Zip_Title");
    public string EncodingLabel => LocalizationManager.T("FormatOptions_Zip_Encoding");
    public string ZipCompressionMethodLabel => LocalizationManager.T("FormatOptions_Zip_CompressionMethod");
    public string SevenZPanelTitle => LocalizationManager.T("FormatOptions_7z_Title");
    public string MethodLabel => LocalizationManager.T("FormatOptions_7z_Method");
    public string SolidLabel => LocalizationManager.T("FormatOptions_7z_Solid");
    public string SolidBlockSizeLabel => LocalizationManager.T("FormatOptions_7z_SolidBlockSize");
    public string DictSizeLabel => LocalizationManager.T("FormatOptions_7z_DictionarySize");
    public string WordSizeLabel => LocalizationManager.T("FormatOptions_7z_WordSize");
    public string MatchFinderLabel => LocalizationManager.T("FormatOptions_7z_MatchFinder");
    public string TarGzPlaceholder => LocalizationManager.T("FormatOptions_TarGz_Placeholder");

    // ── CLR Properties ─────────────────────────────────────────────────────

    public string SelectedFormat
    {
        get => GetValue(SelectedFormatProperty);
        set => SetValue(SelectedFormatProperty, value);
    }

    public string? FileNameEncoding
    {
        get
        {
            if (SelectedFormat != "zip") return null;
            if (EncodingCombo.SelectedItem is ComboBoxItem item)
                return item.Tag as string;
            return "utf-8";
        }
    }

    public string? ZipCompressionMethod
    {
        get
        {
            if (SelectedFormat != "zip") return null;
            if (ZipMethodCombo.SelectedItem is ComboBoxItem item)
                return item.Tag as string;
            return "deflate";
        }
    }

    public string? SevenZipCompressionMethod
    {
        get
        {
            if (SelectedFormat != "7z") return null;
            if (MethodCombo.SelectedItem is ComboBoxItem item)
                return item.Tag as string;
            return "LZMA2";
        }
    }

    public bool SevenZipSolid
    {
        get
        {
            if (SelectedFormat != "7z") return false;
            return SolidCheck.IsChecked == true;
        }
    }

    public string? SevenZipSolidBlockSize
    {
        get
        {
            if (SelectedFormat != "7z") return null;
            if (SolidBlockSizeCombo.SelectedItem is ComboBoxItem item)
                return item.Tag as string;
            return "";
        }
    }

    public int SevenZipDictionarySize
    {
        get
        {
            if (SelectedFormat != "7z") return 0;
            if (DictSizeCombo.SelectedItem is ComboBoxItem item
                && item.Tag is string tag
                && int.TryParse(tag, out var val))
                return val;
            return 0;
        }
    }

    public int SevenZipNumFastBytes
    {
        get
        {
            if (SelectedFormat != "7z") return 0;
            if (WordSizeCombo.SelectedItem is ComboBoxItem item
                && item.Tag is string tag
                && int.TryParse(tag, out var val))
                return val;
            return 0;
        }
    }

    public string? SevenZipMatchFinder
    {
        get
        {
            if (SelectedFormat != "7z") return null;
            if (MatchFinderCombo.SelectedItem is ComboBoxItem item)
                return item.Tag as string;
            return "";
        }
    }

    // ── Constructor ────────────────────────────────────────────────────────

    public DynamicFormatOptionsPanel()
    {
        InitializeComponent();
        PopulateCombos();
        UpdatePanel();
    }

    // ── Property Changed ───────────────────────────────────────────────────

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedFormatProperty)
        {
            UpdatePanel();
        }
    }

    // ── Events ─────────────────────────────────────────────────────────────

    private void SolidCheck_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (SolidBlockSizeCombo != null)
            SolidBlockSizeCombo.IsEnabled = SolidCheck.IsChecked == true;
    }

    // ── Public Methods ─────────────────────────────────────────────────────

    /// <summary>
    /// Load default values from AppSettings into all controls.
    /// </summary>
    public void LoadDefaults()
    {
        var s = AppSettings.Load();

        switch (s.DefaultFormat.ToLowerInvariant())
        {
            case "zip":
            case "7z":
            case "tar.gz":
                SelectedFormat = s.DefaultFormat;
                break;
            default:
                SelectedFormat = "zip";
                break;
        }

        SelectComboByTag(EncodingCombo, s.ZipEncoding ?? "utf-8");
        SelectComboByTag(ZipMethodCombo, s.ZipCompressionMethod ?? "deflate");
        SelectComboByTag(MethodCombo, s.SevenZipCompressionMethod ?? "LZMA2");

        SolidCheck.IsChecked = s.SevenZipSolid;
        SolidBlockSizeCombo.IsEnabled = s.SevenZipSolid;

        SelectComboByTag(SolidBlockSizeCombo, s.SevenZipSolidBlockSize ?? "");
        SelectComboByIntValue(DictSizeCombo, s.SevenZipDictionarySize);
        SelectComboByIntValue(WordSizeCombo, s.SevenZipNumFastBytes);
        SelectComboByTag(MatchFinderCombo, s.SevenZipMatchFinder ?? "");
    }

    // ── Populate combos ────────────────────────────────────────────────────

    private void PopulateCombos()
    {
        void Fill(ComboBox combo, CompressionOptionData.ComboOption[] options,
                  Func<CompressionOptionData.ComboOption, string>? displayResolver = null)
        {
            combo.Items.Clear();
            foreach (var opt in options)
            {
                var display = displayResolver?.Invoke(opt) ?? opt.Display;
                if (string.IsNullOrEmpty(display)) display = opt.Tag;
                combo.Items.Add(new ComboBoxItem { Content = display, Tag = opt.Tag });
            }
        }

        Fill(EncodingCombo, CompressionOptionData.ZipEncodings, opt =>
            opt.Tag == "default" ? LocalizationManager.T("FormatOptions_Zip_EncodingDefault") : opt.Display);

        Fill(ZipMethodCombo, CompressionOptionData.ZipCompressionMethods);
        Fill(MethodCombo, CompressionOptionData.SevenZipMethods);

        Fill(SolidBlockSizeCombo, CompressionOptionData.SevenZipSolidBlockSizes, opt =>
            opt.Tag == "" ? LocalizationManager.T("FormatOptions_7z_SolidBlockSize_Default") : opt.Display);

        Fill(DictSizeCombo, CompressionOptionData.SevenZipDictionarySizes, opt =>
            opt.Tag == "0" ? LocalizationManager.T("FormatOptions_7z_DictSize_Default") : opt.Display);

        Fill(WordSizeCombo, CompressionOptionData.SevenZipNumFastBytes, opt =>
            opt.Tag == "0" ? LocalizationManager.T("FormatOptions_7z_WordSize_Default") : opt.Display);

        Fill(MatchFinderCombo, CompressionOptionData.SevenZipMatchFinders, opt =>
            opt.Tag == "" ? LocalizationManager.T("FormatOptions_7z_MatchFinder_Default") : opt.Display);
    }

    // ── Private Helpers ────────────────────────────────────────────────────

    private void UpdatePanel()
    {
        ZipPanel.IsVisible = false;
        SevenZPanel.IsVisible = false;
        TarGzPanel.IsVisible = false;

        switch (SelectedFormat?.ToLowerInvariant())
        {
            case "zip":
                ZipPanel.IsVisible = true;
                break;
            case "7z":
                SevenZPanel.IsVisible = true;
                if (SolidBlockSizeCombo != null)
                    SolidBlockSizeCombo.IsEnabled = SolidCheck.IsChecked == true;
                break;
            case "tar.gz":
            case "tgz":
            case "tar":
                TarGzPanel.IsVisible = true;
                break;
            default:
                TarGzPanel.IsVisible = true;
                break;
        }
    }

    private static void SelectComboByTag(ComboBox combo, string targetTag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem ci && ci.Tag is string tag
                && string.Equals(tag, targetTag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = ci;
                return;
            }
        }
    }

    private static void SelectComboByIntValue(ComboBox combo, int targetValue)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem ci && ci.Tag is string tag
                && int.TryParse(tag, out var val) && val > 0 && val == targetValue)
            {
                combo.SelectedItem = ci;
                return;
            }
        }
        if (targetValue == 0 && combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }
}
