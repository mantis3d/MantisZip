using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// PE 预览的显示接线（issue #74）：标题用条目名、元数据交给标准元数据面板。
/// </summary>
public sealed class PePreviewPanelTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "MantisZipPePreviewTests", Guid.NewGuid().ToString("N"));

    public PePreviewPanelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 尽力清理
        }
    }

    [AvaloniaFact]
    public void ShowPe_UsesEntryNameAsTitle_NotTempFileName()
    {
        var path = WritePe("preview_19bc1690cf247236.exe");
        var vm = new PreviewViewModel();
        vm.ShowLoading("BCompare.exe");

        vm.ShowPe(path, "BCompare.exe");

        // 此前标题取 ProductName ?? 文件名，而文件名是预览临时文件 preview_<hash>.exe
        Assert.Equal("BCompare.exe", vm.PeTitle);
        Assert.DoesNotContain("preview_", vm.PeTitle);
        // 正文不再重复显示架构/子系统（两侧重复）：这两项只出现在右侧元数据面板
        Assert.Null(typeof(PreviewViewModel).GetProperty("PeSubtitle"));
        Assert.Contains(
            vm.FormatSections.SelectMany(section => section.Rows)
                             .Concat(vm.ContentTopItems)
                             .SelectMany(row => row.Items),
            item => item.Value?.Contains("x64", StringComparison.Ordinal) == true);
    }

    [AvaloniaFact]
    public void ShowPe_RendersFormatMetadataPanel()
    {
        var path = WritePe("preview_abc.exe");
        var vm = new PreviewViewModel();
        vm.ShowLoading("app.exe");
        vm.UpdateCommonMetadata("app.exe", "1 KB", "1 KB", "0%", "2026-01-01 00:00");
        Assert.True(vm.IsFormatPending);

        vm.ShowPe(path, "app.exe");

        // 此前 PE 是唯一绕过 RenderFormatToViewModel 的格式，IsFormatPending 永远停在 true，
        // 元数据设置里 PE 组的勾选是死配置。
        Assert.False(vm.IsFormatPending);
        Assert.True(vm.HasFormatSections || vm.FormatSections.Count > 0 || vm.ContentTopItems.Count > 0);
    }

    private string WritePe(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, BuildPe32Plus());
        return path;
    }

    /// <summary>最小 PE32+：DOS 头 + PE 头 + 可选头，无节、无资源（版本字段全空）。</summary>
    private static byte[] BuildPe32Plus()
    {
        const int peOffset = 0x80;
        const int optSize = 0xF0;
        var bytes = new byte[0x200];

        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        WriteU32(bytes, 0x3C, peOffset);
        WriteU32(bytes, peOffset, 0x00004550);          // "PE\0\0"
        WriteU16(bytes, peOffset + 4, 0x8664);          // Machine = x64
        WriteU16(bytes, peOffset + 6, 0);               // NumberOfSections
        WriteU16(bytes, peOffset + 20, optSize);        // SizeOfOptionalHeader
        WriteU16(bytes, peOffset + 22, 0x0022);         // Characteristics

        int opt = peOffset + 24;
        WriteU16(bytes, opt, 0x20B);                    // Magic = PE32+
        WriteU16(bytes, opt + 68, 2);                   // Subsystem = GUI（两种格式都在 +68）
        WriteU32(bytes, opt + 108, 16);                 // NumberOfRvaAndSizes（PE32+ 在 +108）
        return bytes;
    }

    private static void WriteU16(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteU32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
        bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
