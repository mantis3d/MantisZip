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

        // 7z 压缩方法
        for (int i = 0; i < MethodCombo.Items.Count; i++)
        {
            if (MethodCombo.Items[i] is ComboBoxItem item && (string)item.Tag == s.SevenZipCompressionMethod)
            { MethodCombo.SelectedIndex = i; break; }
        }

        // 7z 固实
        SolidCheck.IsChecked = s.SevenZipSolid;
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