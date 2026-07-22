using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 窗口状态持久化管理器。
/// 将窗口的大小、位置、状态保存到 %LOCALAPPDATA%\MantisZip\window.json，
/// 跨会话恢复窗口布局。
/// 
/// 用法：
///   WindowStateManager.LoadAsync(window)      // 启动时
///   WindowStateManager.SaveAsync(window)      // 关闭时
/// </summary>
internal static class WindowStateManager
{
    private static readonly string BaseDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantisZip");

    private static readonly string ConfigFile =
        Path.Combine(BaseDir, "window.json");

    /// <summary>
    /// 从 JSON 恢复窗口状态。
    /// 仅在 Width/Height > 0 时恢复；忽略 Position 为默认值的情况。
    /// </summary>
    public static void Load(Window window)
    {
        if (!File.Exists(ConfigFile))
            return;

        try
        {
            var json = File.ReadAllText(ConfigFile);
            var snapshot = JsonSerializer.Deserialize<WindowStateSnapshot>(json);
            if (snapshot == null)
                return;

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
        }
        catch (Exception ex)
        {
            App.DebugLog($"WindowStateManager.Load: failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 将当前窗口状态序列化到 JSON。
    /// 仅保存 Normal / Maximized 窗口的大小位置；最小化或全屏时跳过。
    /// </summary>
    public static void Save(Window window)
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

    private class WindowStateSnapshot
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int State { get; set; } = (int)WindowState.Normal;
    }
}
