using System.Windows;
using System.Windows.Controls;

namespace MantisZip.UI.Controls;

public partial class DynamicFormatOptionsPanel : UserControl
{
    // ── Dependency Properties ─────────────────────────────────────────────────

    public static readonly DependencyProperty SelectedFormatProperty =
        DependencyProperty.Register(nameof(SelectedFormat), typeof(string), typeof(DynamicFormatOptionsPanel),
            new PropertyMetadata("zip", OnSelectedFormatChanged));

    // ── CLR Properties ────────────────────────────────────────────────────────

    public string SelectedFormat
    {
        get => (string)GetValue(SelectedFormatProperty);
        set => SetValue(SelectedFormatProperty, value);
    }

    /// <summary>ZIP filename encoding (utf-8, gbk, default). Null if not ZIP mode.</summary>
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

    /// <summary>7z compression method. Null if not 7z mode.</summary>
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

    /// <summary>7z solid archive flag. False if not 7z mode.</summary>
    public bool SevenZipSolid
    {
        get
        {
            if (SelectedFormat != "7z") return false;
            return SolidCheck.IsChecked == true;
        }
    }

    /// <summary>7z solid block size. null if not 7z mode.</summary>
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

    /// <summary>7z dictionary size (bytes). null if not 7z mode.</summary>
    public int? SevenZipDictionarySize
    {
        get
        {
            if (SelectedFormat != "7z") return null;
            if (DictSizeCombo.SelectedItem is ComboBoxItem item
                && int.TryParse(item.Tag?.ToString(), out var val) && val > 0)
                return val;
            return null;
        }
    }

    /// <summary>7z num fast bytes / word size. null if not 7z mode.</summary>
    public int? SevenZipNumFastBytes
    {
        get
        {
            if (SelectedFormat != "7z") return null;
            if (WordSizeCombo.SelectedItem is ComboBoxItem item
                && int.TryParse(item.Tag?.ToString(), out var val) && val > 0)
                return val;
            return null;
        }
    }

    /// <summary>7z match finder. null if not 7z mode.</summary>
    public string? SevenZipMatchFinder
    {
        get
        {
            if (SelectedFormat != "7z") return null;
            if (MatchFinderCombo.SelectedItem is ComboBoxItem item)
                return item.Tag as string;
            return null;
        }
    }

    /// <summary>ZIP compression method. null if not zip mode.</summary>
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

    // ── Constructor ───────────────────────────────────────────────────────────

    public DynamicFormatOptionsPanel()
    {
        InitializeComponent();
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private static void OnSelectedFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DynamicFormatOptionsPanel panel)
        {
            panel.UpdatePanel();
        }
    }

    private void SolidCheck_Changed(object sender, RoutedEventArgs e)
    {
        // 固实不勾选时，固实块大小禁用
        SolidBlockSizeCombo.IsEnabled = SolidCheck.IsChecked == true;
    }

    /// <summary>
    /// 从 AppSettings 加载默认值，预填到各控件中。
    /// </summary>
    public void LoadDefaults()
    {
        var s = AppSettings.Instance;

        // ZIP 编码
        for (int i = 0; i < EncodingCombo.Items.Count; i++)
        {
            if (EncodingCombo.Items[i] is ComboBoxItem item && (string)item.Tag == s.ZipEncoding)
            { EncodingCombo.SelectedIndex = i; break; }
        }

        // ZIP 压缩方法
        for (int i = 0; i < ZipMethodCombo.Items.Count; i++)
        {
            if (ZipMethodCombo.Items[i] is ComboBoxItem item && (string)item.Tag == s.ZipCompressionMethod)
            { ZipMethodCombo.SelectedIndex = i; break; }
        }

        // 7z 压缩方法
        for (int i = 0; i < MethodCombo.Items.Count; i++)
        {
            if (MethodCombo.Items[i] is ComboBoxItem item && (string)item.Tag == s.SevenZipCompressionMethod)
            { MethodCombo.SelectedIndex = i; break; }
        }

        // 7z 固实
        SolidCheck.IsChecked = s.SevenZipSolid;
        SolidBlockSizeCombo.IsEnabled = s.SevenZipSolid;

        // 7z 固实块大小
        for (int i = 0; i < SolidBlockSizeCombo.Items.Count; i++)
        {
            if (SolidBlockSizeCombo.Items[i] is ComboBoxItem item && (string)item.Tag == s.SevenZipSolidBlockSize)
            { SolidBlockSizeCombo.SelectedIndex = i; break; }
        }

        // 7z 字典大小
        for (int i = 0; i < DictSizeCombo.Items.Count; i++)
        {
            if (DictSizeCombo.Items[i] is ComboBoxItem item
                && int.TryParse(item.Tag?.ToString(), out var val) && val > 0
                && val == s.SevenZipDictionarySize)
            { DictSizeCombo.SelectedIndex = i; break; }
        }

        // 7z Word Size
        for (int i = 0; i < WordSizeCombo.Items.Count; i++)
        {
            if (WordSizeCombo.Items[i] is ComboBoxItem item
                && int.TryParse(item.Tag?.ToString(), out var val) && val > 0
                && val == s.SevenZipNumFastBytes)
            { WordSizeCombo.SelectedIndex = i; break; }
        }

        // 7z 匹配器
        for (int i = 0; i < MatchFinderCombo.Items.Count; i++)
        {
            if (MatchFinderCombo.Items[i] is ComboBoxItem item && (string)item.Tag == s.SevenZipMatchFinder)
            { MatchFinderCombo.SelectedIndex = i; break; }
        }
    }

    private void UpdatePanel()
    {
        // Hide all panels first
        ZipPanel.Visibility = Visibility.Collapsed;
        SevenZPanel.Visibility = Visibility.Collapsed;
        TarGzPanel.Visibility = Visibility.Collapsed;

        // Show the matching panel
        switch (SelectedFormat?.ToLowerInvariant())
        {
            case "zip":
                ZipPanel.Visibility = Visibility.Visible;
                break;
            case "7z":
                SevenZPanel.Visibility = Visibility.Visible;
                SolidBlockSizeCombo.IsEnabled = SolidCheck.IsChecked == true;
                break;
            case "tar.gz":
            case "tgz":
            case "tar":
                TarGzPanel.Visibility = Visibility.Visible;
                break;
            default:
                TarGzPanel.Visibility = Visibility.Visible;
                break;
        }
    }
}