using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 窗口状态持久化管理器。
/// 将窗口的大小、位置、状态保存到 %LOCALAPPDATA%\MantisZip\window.json
/// （便携模式：exe 旁 Data/window.json），跨会话恢复窗口布局。
/// 
/// 用法：
///   WindowStateManager.LoadAsync(window)      // 启动时
///   WindowStateManager.SaveAsync(window)      // 关闭时
/// </summary>
internal static class WindowStateManager
{
    private static readonly string BaseDir = AppSettings.DataDir;

    private static readonly string ConfigFile =
        Path.Combine(BaseDir, "window.json");

    /// <summary>
    /// 从 JSON 恢复窗口状态。
    /// 仅在 Width/Height > 0 时恢复；忽略 Position 为默认值的情况。
    /// 返回保存的列状态（可能为 null，表示无列数据或加载失败）。
    /// sortColumnPath/sortDirection 输出持久化的列排序状态（0=无, 1=升序, 2=降序，与 WPF window.json 兼容）。
    /// </summary>
    public static List<ColumnStateDto>? Load(Window window, out string? sortColumnPath, out int sortDirection)
    {
        sortColumnPath = null;
        sortDirection = 0;

        if (!File.Exists(ConfigFile))
            return null;

        try
        {
            var json = File.ReadAllText(ConfigFile);
            var snapshot = JsonSerializer.Deserialize<WindowStateSnapshot>(json);
            if (snapshot == null)
                return null;

            sortColumnPath = snapshot.SortColumnPath;
            sortDirection = snapshot.SortDirection;

            if (snapshot.Width > 0 && snapshot.Height > 0)
            {
                window.Width = snapshot.Width;
                window.Height = snapshot.Height;
            }

            // Avalonia PixelPoint (0,0) is a valid screen position but
            // also the default — skip if it looks uninitialized.
            if (snapshot.X != 0 || snapshot.Y != 0)
            {
                window.Position = new PixelPoint(snapshot.X, snapshot.Y);
            }

            if (snapshot.State >= 0)
            {
                // Don't restore FullScreen — only Normal/Maximized/Minimized
                var ws = (WindowState)snapshot.State;
                if (ws is WindowState.Normal or WindowState.Maximized or WindowState.Minimized)
                    window.WindowState = ws;
            }

            return snapshot.ColumnStates;
        }
        catch (Exception ex)
        {
            App.DebugLog($"WindowStateManager.Load: failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 将当前窗口状态序列化到 JSON。
    /// 仅保存 Normal / Maximized 窗口的大小位置；最小化或全屏时跳过。
    /// </summary>
    /// <param name="window">目标窗口。</param>
    /// <param name="columnStates">DataGrid 列状态快照（可为 null，兼容仅窗口尺寸的旧格式）。</param>
    /// <param name="sortColumnPath">当前排序列的 SortMemberPath（null = 未排序）。</param>
    /// <param name="sortDirection">排序方向编码（0=无, 1=升序, 2=降序，与 WPF window.json 兼容）。</param>
    public static void Save(Window window, IReadOnlyList<ColumnStateDto>? columnStates = null,
        string? sortColumnPath = null, int sortDirection = 0)
    {
        try
        {
            // Don't persist while minimized — position/size may be stale
            if (window.WindowState == WindowState.Minimized)
                return;

            var snapshot = new WindowStateSnapshot
            {
                Width = window.Width,
                Height = window.Height,
                X = window.Position.X,
                Y = window.Position.Y,
                State = (int)window.WindowState,
                ColumnStates = columnStates?.ToList() ?? new List<ColumnStateDto>(),
                SortColumnPath = sortColumnPath,
                SortDirection = sortDirection,
            };

            if (!Directory.Exists(BaseDir))
                Directory.CreateDirectory(BaseDir);

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFile, json);
        }
        catch (Exception ex)
        {
            App.DebugLog($"WindowStateManager.Save: failed: {ex.Message}");
        }
    }

    /// <summary>
    /// DataGrid 列状态。字段与 WPF 版 window.json 的 ColumnStates 兼容：
    /// ColumnId = 列的 SortMemberPath（无 SortMemberPath 的列不参与持久化）。
    /// </summary>
    public sealed class ColumnStateDto
    {
        public string? ColumnId { get; set; }
        public double Width { get; set; }
        public bool Visible { get; set; }
        public int DisplayIndex { get; set; }
    }

    private class WindowStateSnapshot
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int State { get; set; } = (int)WindowState.Normal;
        public List<ColumnStateDto> ColumnStates { get; set; } = new();
        /// <summary>当前排序列的 SortMemberPath（null = 未排序）。字段与 WPF window.json 兼容。</summary>
        public string? SortColumnPath { get; set; }
        /// <summary>排序方向编码：0=无, 1=升序, 2=降序。字段与 WPF window.json 兼容。</summary>
        public int SortDirection { get; set; }
    }
}
