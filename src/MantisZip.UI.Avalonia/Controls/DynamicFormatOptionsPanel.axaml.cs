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
    public string MultiThreadLabel => LocalizationManager.T("FormatOptions_7z_MultiThread");
    public string TarGzPlaceholder => LocalizationManager.T("FormatOptions_TarGz_Placeholder");
    public string CustomDictSizeHint => LocalizationManager.T("FormatOptions_7z_DictSize_CustomHint");
    public string DictSizeMemoryHint => LocalizationManager.T("FormatOptions_7z_DictSize_MemoryHint");

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

    /// <summary>
    /// 7z 字典大小：""=引擎默认，或 "64m" / "256m" / "1g" / "4g"（CustomParameters["d"] 直传）。
    /// 选「自定义」（tag="-1"）时读取输入框 MB 数值拼为 "Nm"；输入非法返回 ""。
    /// </summary>
    public string SevenZipDictionarySize
    {
        get
        {
            if (SelectedFormat != "7z") return "";
            if (DictSizeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (tag == "-1")
                {
                    // 自定义：输入框收 MB 数字，拼 "Nm"（裸数字直传 7z 会被解释为 2^N 幂次，必须带单位）
                    if (long.TryParse(CustomDictSizeBox?.Text?.Trim(), out var mb) && mb > 0)
                        return $"{mb}m";
                    return "";
                }
                return tag; // ""=默认 或 "16m"/"1g" 等预设
            }
            return "";
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

    /// <summary>
    /// 7z 多线程压缩（mt=on）。仅对 7z 格式有效；非 7z 格式返回 true（引擎侧忽略）。
    /// </summary>
    public bool SevenZipMultithreaded
    {
        get
        {
            if (SelectedFormat != "7z") return true;
            return MultiThreadCheck.IsChecked == true;
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

    /// <summary>
    /// 多线程压缩复选框变更 — 当前无联动控件（仅影响引擎侧 mt=on 参数），
    /// 保留事件以便后续扩展（如线程数选择）。
    /// </summary>
    private void MultiThreadCheck_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        // 无需联动控件；占位以便未来添加线程数控件时启用/禁用
    }

    /// <summary>
    /// 字典大小下拉变更 — 选「自定义」（tag="-1"）时显示 MB 输入框（规则 6：隐藏而非禁用），
    /// 并按输入值联动内存提示（>1GB 显示「解压需同等可用内存」灰字）。
    /// </summary>
    private void DictSizeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CustomDictSizePanel == null) return;

        bool isCustom = DictSizeCombo.SelectedItem is ComboBoxItem item && item.Tag as string == "-1";
        CustomDictSizePanel.IsVisible = isCustom;
        UpdateDictSizeMemoryWarning(isCustom);
    }

    /// <summary>
    /// 自定义字典输入变化时刷新内存提示可见性（仅在自定义模式下且输入 ≥1024 MB 时显示）。
    /// </summary>
    private void CustomDictSizeBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        bool isCustom = DictSizeCombo?.SelectedItem is ComboBoxItem item && item.Tag as string == "-1";
        UpdateDictSizeMemoryWarning(isCustom);
    }

    private void UpdateDictSizeMemoryWarning(bool isCustom)
    {
        if (DictSizeMemoryWarning == null) return;
        bool show = isCustom
            && long.TryParse(CustomDictSizeBox?.Text?.Trim(), out var mb)
            && mb >= 1024;
        DictSizeMemoryWarning.IsVisible = show;
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

        MultiThreadCheck.IsChecked = s.SevenZipMultithreaded;

        SelectComboByTag(SolidBlockSizeCombo, s.SevenZipSolidBlockSize ?? "");
        // 字典大小：settings 值不在预设 tag 中（如 "4096m" 自定义值）→ 回退选「自定义」并回填 MB 数字
        if (!SelectComboByTag(DictSizeCombo, s.SevenZipDictionarySize ?? "")
            && !string.IsNullOrEmpty(s.SevenZipDictionarySize))
        {
            SelectComboByTag(DictSizeCombo, "-1");
            var v = s.SevenZipDictionarySize.Trim();
            if (CustomDictSizeBox != null)
                CustomDictSizeBox.Text = v.EndsWith("m", StringComparison.OrdinalIgnoreCase) ? v[..^1] : v;
        }
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
            opt.Tag == "" ? LocalizationManager.T("FormatOptions_7z_DictSize_Default")
            : opt.Tag == "-1" ? LocalizationManager.T("FormatOptions_7z_DictSize_Custom")
            : opt.Display);

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

    /// <summary>
    /// 按 tag 精确匹配选中下拉项；返回是否命中（供自定义值回退分支判断）。
    /// </summary>
    private static bool SelectComboByTag(ComboBox combo, string targetTag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem ci && ci.Tag is string tag
                && string.Equals(tag, targetTag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = ci;
                return true;
            }
        }
        return false;
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
