using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Controls;

/// <summary>
/// A panel that shows format-specific compression options based on the SelectedFormat property.
/// Supports ZIP (filename encoding), 7z (compression method + solid flag), and TAR.GZ (placeholder).
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
    public string EncodingDefaultLabel => LocalizationManager.T("FormatOptions_Zip_EncodingDefault");
    public string SevenZPanelTitle => LocalizationManager.T("FormatOptions_7z_Title");
    public string MethodLabel => LocalizationManager.T("FormatOptions_7z_Method");
    public string SolidLabel => LocalizationManager.T("FormatOptions_7z_Solid");
    public string TarGzPlaceholder => LocalizationManager.T("FormatOptions_TarGz_Placeholder");

    // ── CLR Properties ─────────────────────────────────────────────────────

    /// <summary>
    /// Selected archive format: "zip", "7z", or "tar.gz" (or "tgz" / "tar").
    /// Determines which sub-panel is visible.
    /// </summary>
    public string SelectedFormat
    {
        get => GetValue(SelectedFormatProperty);
        set => SetValue(SelectedFormatProperty, value);
    }

    /// <summary>
    /// ZIP filename encoding. Null if not ZIP mode.
    /// Returns "utf-8", "gbk", or "default".
    /// </summary>
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

    /// <summary>
    /// 7z compression method tag. Null if not 7z mode.
    /// Returns "LZMA", "LZMA2", "PPMd", "BZip2", or "Deflate".
    /// </summary>
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

    /// <summary>
    /// 7z solid archive flag. False if not 7z mode.
    /// </summary>
    public bool SevenZipSolid
    {
        get
        {
            if (SelectedFormat != "7z") return false;
            return SolidCheck.IsChecked == true;
        }
    }

    // ── Constructor ────────────────────────────────────────────────────────

    public DynamicFormatOptionsPanel()
    {
        InitializeComponent();
        DataContext = this;
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

    // ── Public Methods ─────────────────────────────────────────────────────

    /// <summary>
    /// Load default values from AppSettings into the controls.
    /// Reads ZipEncoding, SevenZipCompressionMethod, and SevenZipSolid from AppSettings.
    /// </summary>
    public void LoadDefaults()
    {
        var settings = AppSettings.Load();

        // Set format from settings if available
        switch (settings.DefaultFormat.ToLowerInvariant())
        {
            case "zip":
            case "7z":
            case "tar.gz":
                SelectedFormat = settings.DefaultFormat;
                break;
            default:
                SelectedFormat = "zip";
                break;
        }

        // ZIP encoding — select from settings
        var encodingTag = (settings.ZipEncoding ?? "utf-8").ToLowerInvariant();
        foreach (var item in EncodingCombo.Items)
        {
            if (item is ComboBoxItem comboItem && comboItem.Tag is string tag && tag.ToLowerInvariant() == encodingTag)
            {
                EncodingCombo.SelectedItem = comboItem;
                break;
            }
        }

        // 7z compression method — select from settings
        var methodTag = settings.SevenZipCompressionMethod ?? "LZMA2";
        foreach (var item in MethodCombo.Items)
        {
            if (item is ComboBoxItem comboItem && comboItem.Tag is string tag && string.Equals(tag, methodTag, StringComparison.OrdinalIgnoreCase))
            {
                MethodCombo.SelectedItem = comboItem;
                break;
            }
        }

        // 7z solid — from settings
        SolidCheck.IsChecked = settings.SevenZipSolid;
    }

    // ── Private Methods ────────────────────────────────────────────────────

    private void UpdatePanel()
    {
        // Hide all panels first
        ZipPanel.IsVisible = false;
        SevenZPanel.IsVisible = false;
        TarGzPanel.IsVisible = false;

        // Show the matching panel
        switch (SelectedFormat?.ToLowerInvariant())
        {
            case "zip":
                ZipPanel.IsVisible = true;
                break;
            case "7z":
                SevenZPanel.IsVisible = true;
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
}
