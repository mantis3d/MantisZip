using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Diagnostics;
using System.Security.Principal;
using System.Collections.Generic;

namespace MantisZip.UI;

/// <summary>
/// 权限提升相关工具方法 — Elevation 相关方法的 partial 定义
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 检测指定目录是否可写入。包含两级探测：
    /// 1. 根目录写入测试文件（DeleteOnClose 自动清理）
    /// 2. 在根目录下创建子目录并写入测试文件（捕获子目录级别的权限问题）
    /// </summary>
    private static bool IsDirectoryWritable(string dirPath)
    {
        try
        {
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            // 第一级：根目录写入测试
            var testFile = Path.Combine(dirPath, Path.GetRandomFileName());
            using (var fs = File.Create(testFile, 1, FileOptions.DeleteOnClose)) { }

            // 第二级：子目录创建 + 写入测试
            var testDir = Path.Combine(dirPath, Path.GetRandomFileName());
            Directory.CreateDirectory(testDir);
            var testFileInSub = Path.Combine(testDir, Path.GetRandomFileName());
            using (var fs = File.Create(testFileInSub, 1, FileOptions.DeleteOnClose)) { }

            // 清理测试子目录（文件已因 DeleteOnClose 自动删除）
            try { Directory.Delete(testDir); } catch { }

            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    /// <summary>
    /// 检测当前进程是否以管理员权限运行。
    /// </summary>
    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// 以管理员权限重新启动当前进程，传递原始 CLI 参数。
    /// </summary>
    private static void RelaunchAsAdmin(string[] originalArgs)
    {
        var exePath = Process.GetCurrentProcess().MainModule!.FileName;
        var args = string.Join(" ", originalArgs.Select(a => $"\"{a}\""));
        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            Verb = "runas",
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// 弹出权限不足提示对话框（默认行为，仅 OK 按钮，无提权选项）。
    /// </summary>
    private static void ShowElevationInfoDialog(IReadOnlyList<string> unwritableDirs)
    {
        var dlg = new ElevationInfoDialog(unwritableDirs);
        dlg.Owner = Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        dlg.ShowDialog();
    }

    /// <summary>
    /// 弹出提权确认对话框（允许用户选择以管理员身份运行）。
    /// </summary>
    private static bool? ShowElevationDialog(IReadOnlyList<string> unwritableDirs)
    {
        var dlg = new ElevationDialog(unwritableDirs);
        dlg.Owner = Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        return dlg.ShowDialog();
    }

    /// <summary>
    /// 弹出提权失败对话框（提权后仍无法写入时的错误提示）。
    /// </summary>
    private static void ShowElevationFailedDialog(IReadOnlyList<string> unwritableDirs)
    {
        var dlg = new ElevationFailedDialog(unwritableDirs);
        dlg.Owner = Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        dlg.ShowDialog();
    }
}
