using System.IO;
using System.Text.Json;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 内容区布局快照持久化管理器。
/// 将目录树/文件列表列宽与预览面板各位置尺寸保存到 %LOCALAPPDATA%\MantisZip\layout.json
/// （便携模式：exe 旁 Data/layout.json）。
///
/// 与 WindowStateManager（关闭时自动保存）不同：本管理器【只在用户手动点击「保存布局」时写盘】，
/// 绝不随窗口关闭自动写，保证「调坏了 → 重新打开软件即可恢复上次保存的布局」的手动快照语义。
/// </summary>
internal static class LayoutStateManager
{
    private static readonly string BaseDir = AppSettings.DataDir;

    private static readonly string ConfigFile =
        Path.Combine(BaseDir, "layout.json");

    /// <summary>
    /// 从 JSON 加载上次保存的布局快照；无文件或损坏时返回 null。
    /// </summary>
    public static LayoutSnapshot? Load()
    {
        if (!File.Exists(ConfigFile))
            return null;

        try
        {
            var json = File.ReadAllText(ConfigFile);
            return JsonSerializer.Deserialize<LayoutSnapshot>(json);
        }
        catch (Exception ex)
        {
            App.DebugLog($"LayoutStateManager.Load: failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 将布局快照序列化到 JSON（仅手动调用，不在窗口关闭时自动写）。
    /// </summary>
    public static void Save(LayoutSnapshot snapshot)
    {
        try
        {
            if (!Directory.Exists(BaseDir))
                Directory.CreateDirectory(BaseDir);

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFile, json);
        }
        catch (Exception ex)
        {
            App.DebugLog($"LayoutStateManager.Save: failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 布局快照。列宽为像素值（null 表示从未拖过、维持默认星号比例）；
    /// PreviewSizeByPosition 为预览面板各位置（1=底部, 2=目录树下方, 3=文件列表下方, 4=右侧）的记忆尺寸。
    /// </summary>
    public sealed class LayoutSnapshot
    {
        public double? TreeColumnWidth { get; set; }
        public double? FileListColumnWidth { get; set; }
        public Dictionary<int, double> PreviewSizeByPosition { get; set; } = new();
    }
}