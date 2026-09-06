using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// UI 控件测试窗口的 ViewModel，为各控件变体提供初始演示数据。
/// 开发者诊断工具（豁免本地化，见 AGENTS.md 规则 13 豁免条款）。
/// </summary>
public partial class UiTestViewModel : ObservableObject
{
    // ── 文本与按钮组 ──
    [ObservableProperty] private string _nameText = "MantisZip";
    [ObservableProperty] private string _passwordText = "";
    [ObservableProperty] private string _readOnlyText = "只读文本框：内容不可编辑，但可选中复制。";

    // ── 选择与输入组 ──
    public List<string> Formats { get; } = new() { "ZIP", "7Z", "TAR.GZ", "RAR（只读解压）" };
    public List<string> Encodings { get; } = new() { "UTF-8", "GBK", "Big5", "Shift-JIS" };
    public List<string> PathSuggestions { get; } = new()
    {
        @"D:\下载", @"D:\文档", @"D:\照片", @"C:\Users\Admin\Desktop",
        @"C:\Program Files", @"E:\备份", @"\\NAS\share\movie",
    };

    [ObservableProperty] private string _selectedFormat = "ZIP";
    [ObservableProperty] private string _selectedEncoding = "UTF-8";

    /// <summary>DynamicFormatOptionsPanel 专用格式（内部按小写 "zip"/"7z"/"tar.gz" 严格比较）。</summary>
    [ObservableProperty] private string _testFormat = "zip";
    [ObservableProperty] private string _editableComboText = "";
    [ObservableProperty] private bool _isCompressEncrypted = true;
    [ObservableProperty] private bool? _threeStateCheck = null;
    [ObservableProperty] private bool _toggleSwitchOn = true;
    [ObservableProperty] private double _sliderValue = 65;
    [ObservableProperty] private decimal _numericValue = 42;
    [ObservableProperty] private DateTimeOffset? _filterDateFrom = new(DateTime.Today.AddDays(-7));
    [ObservableProperty] private DateTimeOffset? _filterDateTo = new(DateTime.Today);
    [ObservableProperty] private DateTimeOffset? _pickedDate = new(DateTime.Today);
    [ObservableProperty] private DateTime? _calendarDate = DateTime.Today;
    [ObservableProperty] private string _autoCompletePath = @"D:\";
    [ObservableProperty] private string _quickPath = @"D:\下载";

    // ── 列表与数据组 ──
    /// <summary>文件列表（ListBox / DataGrid / ItemsControl 共用同一份数据）。</summary>
    public ObservableCollection<UiTestEntry> Entries { get; } = new();

    /// <summary>目录树根节点（TreeView 用）。</summary>
    public ObservableCollection<PreviewTreeNode> TreeRoots { get; } = new();

    /// <summary>结果预览树根节点（ResultTreeView 用）。</summary>
    public PreviewTreeNode ResultTreeRoot { get; private set; } = null!;

    /// <summary>信息面板数据（InfoPanel 用）。</summary>
    public ObservableCollection<FormatMetadataItem> MetadataItems { get; } = new();

    public UiTestViewModel()
    {
        LoadEntries();
        LoadTree();
        LoadMetadata();
    }

    private void LoadEntries()
    {
        Entries.Add(new UiTestEntry { Name = "📁 docs", Type = "文件夹", SizeDisplay = "—", CompressedDisplay = "—", Modified = "2026-07-28 10:12", Ratio = 1.00 });
        Entries.Add(new UiTestEntry { Name = "📁 images", Type = "文件夹", SizeDisplay = "—", CompressedDisplay = "—", Modified = "2026-07-28 10:15", Ratio = 0.98 });
        Entries.Add(new UiTestEntry { Name = "📁 src", Type = "文件夹", SizeDisplay = "—", CompressedDisplay = "—", Modified = "2026-07-28 10:20", Ratio = 0.96 });
        Entries.Add(new UiTestEntry { Name = "readme.md", Type = "Markdown", SizeDisplay = "12.1 KB", CompressedDisplay = "3.2 KB", Modified = "2026-07-27 22:41", Ratio = 0.38 });
        Entries.Add(new UiTestEntry { Name = "manual.pdf", Type = "PDF", SizeDisplay = "2.34 MB", CompressedDisplay = "2.21 MB", Modified = "2026-07-25 18:02", Ratio = 0.94 });
        Entries.Add(new UiTestEntry { Name = "logo.png", Type = "图片", SizeDisplay = "45.6 KB", CompressedDisplay = "44.8 KB", Modified = "2026-07-24 09:33", Ratio = 0.98 });
        Entries.Add(new UiTestEntry { Name = "screenshot.jpg", Type = "图片", SizeDisplay = "118 KB", CompressedDisplay = "117 KB", Modified = "2026-07-24 09:35", Ratio = 0.99 });
        Entries.Add(new UiTestEntry { Name = "Program.cs", Type = "C#", SizeDisplay = "2.1 KB", CompressedDisplay = "689 B", Modified = "2026-07-22 15:08", Ratio = 0.32 });
        Entries.Add(new UiTestEntry { Name = "MainWindow.axaml", Type = "XAML", SizeDisplay = "8.4 KB", CompressedDisplay = "1.9 KB", Modified = "2026-07-22 15:12", Ratio = 0.23 });
        Entries.Add(new UiTestEntry { Name = "AppSettings.cs", Type = "C#", SizeDisplay = "5.6 KB", CompressedDisplay = "1.4 KB", Modified = "2026-07-21 11:47", Ratio = 0.25 });
        Entries.Add(new UiTestEntry { Name = "install.bat", Type = "脚本", SizeDisplay = "1.0 KB", CompressedDisplay = "320 B", Modified = "2026-07-20 20:05", Ratio = 0.31 });
        Entries.Add(new UiTestEntry { Name = "notes.txt", Type = "文本", SizeDisplay = "3.0 KB", CompressedDisplay = "412 B", Modified = "2026-07-19 08:26", Ratio = 0.14 });
    }

    private void LoadTree()
    {
        var root = new PreviewTreeNode
        {
            Name = "backup.zip",
            FullPath = string.Empty,
            IsArchiveNode = true,
            IsDirectory = true,
            IsExpanded = true,
        };
        root.Children.Add(Dir("docs",
            FileNode("readme.md", 12_345),
            FileNode("manual.pdf", 2_456_789)));
        root.Children.Add(Dir("images",
            FileNode("logo.png", 46_694),
            FileNode("screenshot.jpg", 120_832)));
        root.Children.Add(Dir("src",
            FileNode("Program.cs", 2_150),
            FileNode("MainWindow.axaml", 8_601),
            Dir("ViewModels",
                FileNode("MainWindowViewModel.cs", 21_504))));
        root.Children.Add(FileNode("install.bat", 1_024));

        ResultTreeRoot = root;
        TreeRoots.Add(root);
    }

    private void LoadMetadata()
    {
        MetadataItems.Add(new FormatMetadataItem("压缩格式", "ZIP"));
        MetadataItems.Add(new FormatMetadataItem("条目总数", "12 个文件 · 4 个文件夹"));
        MetadataItems.Add(new FormatMetadataItem("原始大小", "2.56 MB"));
        MetadataItems.Add(new FormatMetadataItem("压缩后大小", "2.41 MB"));
        MetadataItems.Add(new FormatMetadataItem("压缩率", "94.1%"));
        MetadataItems.Add(new FormatMetadataItem("最后修改", "2026-07-28 10:20"));
        MetadataItems.Add(new FormatMetadataItem("加密", "AES-256（文件名已加密）"));
    }

    private static PreviewTreeNode Dir(string name, params PreviewTreeNode[] children)
    {
        var node = new PreviewTreeNode
        {
            Name = name,
            FullPath = name,
            IsDirectory = true,
            IsExpanded = true,
        };
        node.Children.AddRange(children);
        return node;
    }

    private static PreviewTreeNode FileNode(string name, long size)
    {
        return new PreviewTreeNode
        {
            Name = name,
            FullPath = name,
            IsDirectory = false,
            Size = size,
            SizeDisplay = FormatSize(size),
            IsExpanded = true,
        };
    }

    private static string FormatSize(long bytes) => bytes >= 1_048_576
        ? $"{bytes / 1048576.0:0.#} MB"
        : bytes >= 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes} B";
}

/// <summary>测试窗口文件条目（轻量演示数据，与业务模型解耦）。</summary>
public class UiTestEntry
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string SizeDisplay { get; init; } = string.Empty;
    public string CompressedDisplay { get; init; } = string.Empty;
    public string Modified { get; init; } = string.Empty;

    /// <summary>0~1 尺寸占比，用于展示比例条（DataGrid 内嵌 ProgressBar）。</summary>
    public double Ratio { get; init; }

    /// <summary>0~100 百分比，供 ProgressBar.Value 直接绑定。</summary>
    public double RatioPercent => Math.Clamp(Ratio * 100, 0, 100);
}
