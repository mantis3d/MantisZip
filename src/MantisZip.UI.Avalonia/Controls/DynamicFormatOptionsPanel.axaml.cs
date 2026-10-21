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

    /// <summary>
    /// ZIP32 单包未压缩大小上限（与 ZipEngine.MaxZip32Size 同源：uint.MaxValue - 1）。
    /// 达到该值时 ZIP 多线程压缩不可用（ZipBinaryRewriter 不支持 ZIP64）。
    /// </summary>
    private const long MaxZip32Size = uint.MaxValue - 1L;

    /// <summary>
    /// 当前待压缩总量是否达到 ZIP32 上限（由 <see cref="UpdateTotalSourceSize"/> 驱动）。
    /// </summary>
    private bool _zipMtOverSizeLimit;

    /// <summary>
    /// 进入超限状态前用户的勾选状态（退出超限时恢复，避免灰勾与提示矛盾）。
    /// </summary>
    private bool _mtCheckedBeforeLimit;

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

    public string AdaptiveCompressionLabel => LocalizationManager.T("FormatOptions_ZIP_AdaptiveCompression");
    public string SmartDetectLabel => LocalizationManager.T("FormatOptions_ZIP_SmartDetect");
    public string ZipMultiThreadLabel => LocalizationManager.T("FormatOptions_ZIP_MultiThread");
    public string ZipMtOverLimitHint => LocalizationManager.T("FormatOptions_ZIP_MultiThreadOver4Gb");
    /// <summary>多线程压缩常驻科普提示（收益与 CPU 相关，双机实测；常驻显示，不绑显隐）。</summary>
    public string BenefitHint => LocalizationManager.T("Compress_MT_BenefitHint");
    public string ZipAdaptiveSectionTitle => LocalizationManager.T("Settings_AdaptiveCompression");
    public string TarGzPlaceholder => LocalizationManager.T("FormatOptions_TarGz_Placeholder");
    public string CustomDictSizeHint => LocalizationManager.T("FormatOptions_7z_DictSize_CustomHint");
    public string DictSizeMemoryHint => LocalizationManager.T("FormatOptions_7z_DictSize_MemoryHint");
    public string CustomSolidSizeHint => LocalizationManager.T("FormatOptions_7z_SolidBlockSize_CustomHint");

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

    /// <summary>
    /// 7z 固实块大小：""=引擎默认，或 "64m" / "256m" / "1g" / "4g"（CustomParameters["s"] 直传）。
    /// 选「自定义」（tag="-1"）时读取输入框 MB 数值拼为 "Nm"；输入非法返回 ""。
    /// 固实开关未勾选时恒返回 ""（引擎据此走 s=off）。
    /// </summary>
    public string? SevenZipSolidBlockSize
    {
        get
        {
            if (SelectedFormat != "7z") return null;
            if (SolidCheck.IsChecked != true) return "";
            if (SolidBlockSizeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (tag == "-1")
                {
                    // 自定义：输入框收 MB 数字，拼 "Nm"（裸数字直传 7z 会被解释为 2^N 幂次，必须带单位）
                    if (long.TryParse(CustomSolidSizeBox?.Text?.Trim(), out var mb) && mb > 0)
                        return $"{mb}m";
                    return "";
                }
                return tag; // ""=默认 或 "16m"/"1g" 等预设
            }
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

    /// <summary>
    /// 自适应压缩。仅对 ZIP 格式有效；非 ZIP 格式返回 false。
    /// </summary>
    public bool AdaptiveCompression
    {
        get
        {
            if (SelectedFormat != "zip") return false;
            return AdaptiveCompressionCheck.IsChecked == true;
        }
    }

    /// <summary>
    /// 魔数检测（Phase 2 / Task 11：魔数检测已实现，控件恢复可见）。
    /// 仅对 ZIP 格式有效；非 ZIP 格式返回 false。
    /// </summary>
    public bool AdaptiveSmartDetect
    {
        get
        {
            if (SelectedFormat != "zip") return false;
            return SmartDetectCheck.IsChecked == true;
        }
    }

    /// <summary>
    /// ZIP 多线程压缩。仅对 ZIP 格式有效（非加密 + Deflate 方法）；非 ZIP 格式返回 false。
    /// 超过 4GB ZIP32 上限时恒返回 false（即使勾选框残留 IsChecked=true）。
    /// </summary>
    public bool MultiThreadedCompression
    {
        get
        {
            if (SelectedFormat != "zip") return false;
            if (_zipMtOverSizeLimit) return false;
            return ZipMultiThreadCheck.IsChecked == true;
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
        UpdateCustomSolidPanelVisibility();
    }

    /// <summary>
    /// 固实块大小下拉变更 — 选「自定义」（tag="-1"）时显示 MB 输入框（规则 6：隐藏而非禁用）。
    /// </summary>
    private void SolidBlockSizeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateCustomSolidPanelVisibility();
    }

    /// <summary>自定义固实块输入框可见性 = 选中「自定义」且固实开关开启（规则 6：隐藏而非禁用）。</summary>
    private void UpdateCustomSolidPanelVisibility()
    {
        if (CustomSolidSizePanel == null) return;
        bool isCustom = SolidBlockSizeCombo?.SelectedItem is ComboBoxItem item && item.Tag as string == "-1";
        CustomSolidSizePanel.IsVisible = isCustom && SolidCheck.IsChecked == true;
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
    /// 由压缩设置窗口在预览树重建后调用：传入本次待压缩的最大单包未压缩大小（字节），
    /// 达到 <see cref="MaxZip32Size"/> 时禁用 ZIP 多线程勾选框并显示说明文字，
    /// 否则恢复可用并隐藏说明（规则 6：联动面板整块显隐）。
    /// Separate 模式下每个源各自成包，引擎按单包判定，故调用方应传最大单包而非总和。
    /// </summary>
    public void UpdateTotalSourceSize(long maxArchiveBytes)
    {
        bool overLimit = maxArchiveBytes >= MaxZip32Size;
        if (overLimit == _zipMtOverSizeLimit) return;

        if (ZipMultiThreadCheck != null)
        {
            if (overLimit)
            {
                // 进入超限：记住用户勾选状态并取消勾选（避免禁用后残留灰勾与提示矛盾）
                _mtCheckedBeforeLimit = ZipMultiThreadCheck.IsChecked == true;
                ZipMultiThreadCheck.IsChecked = false;
            }
            else
            {
                // 退出超限：恢复原勾选状态
                ZipMultiThreadCheck.IsChecked = _mtCheckedBeforeLimit;
            }
            ZipMultiThreadCheck.IsEnabled = !overLimit;
        }

        _zipMtOverSizeLimit = overLimit;

        if (ZipMtOverLimitHintText != null)
            ZipMtOverLimitHintText.IsVisible = overLimit;
    }

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
        AdaptiveCompressionCheck.IsChecked = s.AdaptiveCompression;
        SmartDetectCheck.IsChecked = s.AdaptiveSmartDetect;
        ZipMultiThreadCheck.IsChecked = s.MultiThreadedCompression;

        // 固实块大小：settings 值不在预设 tag 中（如 "4096m" 自定义值）→ 回退选「自定义」并回填 MB 数字
        if (!SelectComboByTag(SolidBlockSizeCombo, s.SevenZipSolidBlockSize ?? "")
            && !string.IsNullOrEmpty(s.SevenZipSolidBlockSize))
        {
            SelectComboByTag(SolidBlockSizeCombo, "-1");
            var sv = s.SevenZipSolidBlockSize.Trim();
            if (CustomSolidSizeBox != null)
                CustomSolidSizeBox.Text = sv.EndsWith("m", StringComparison.OrdinalIgnoreCase) ? sv[..^1] : sv;
        }
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
            opt.Tag == "" ? LocalizationManager.T("FormatOptions_7z_SolidBlockSize_Default")
            : opt.Tag == "-1" ? LocalizationManager.T("FormatOptions_7z_SolidBlockSize_Custom")
            : opt.Display);

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
                UpdateCustomSolidPanelVisibility();
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
