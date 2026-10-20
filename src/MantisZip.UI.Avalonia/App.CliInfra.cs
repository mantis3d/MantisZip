using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using MantisZip.UI.Avalonia.Views;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace MantisZip.UI.Avalonia;

/// <summary>
/// App 的 CLI 共用基础设施（shell 命令 / 提权 / IPC，自 App.axaml.cs 拆分，纯移动）。
/// </summary>
public partial class App : Application
{
    // ════════════════════════════════════════════════════════════════
    //  Shell commands (--install-shell, --uninstall-shell, etc.)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Handle headless shell commands (--install-shell, --uninstall-shell, etc.).
    /// Uses Environment.Exit(exitCode) instead of AppShutdown.Request(desktop.Shutdown) because these headless
    /// commands run during OnFrameworkInitializationCompleted, before the Dispatcher main loop
    /// starts — a Shutdown() there would throw InvalidOperationException (see Services/AppShutdown.cs),
    /// and they need an explicit exit code without starting the Avalonia lifetime.
    /// </summary>
    private static void HandleShellCommand(string command, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.Error.WriteLine("Shell integration is only supported on Windows.");
            Environment.Exit(1);
            return;
        }

        int exitCode = 0;

        try
        {
            switch (command)
            {
                case "--install-shell":
                    DebugLog("Shell command: --install-shell (native)");
                    ShellIntegration.Install();
                    Console.WriteLine("Shell extension installed successfully.");
                    break;

                case "--uninstall-shell":
                    DebugLog("Shell command: --uninstall-shell (native)");
                    ShellIntegration.Uninstall();
                    Console.WriteLine("Shell extension uninstalled successfully.");
                    break;

                case "--install-assoc":
                    DebugLog("Shell command: --install-assoc (native)");
                    ShellIntegration.InstallAssociations();
                    Console.WriteLine("File associations installed successfully.");
                    break;

                case "--uninstall-assoc":
                    DebugLog("Shell command: --uninstall-assoc (native)");
                    ShellIntegration.UninstallAssociations();
                    Console.WriteLine("File associations uninstalled successfully.");
                    break;

                default:
                    Console.Error.WriteLine($"Unknown shell command: {command}");
                    exitCode = 1;
                    break;
            }
        }
        catch (Exception ex)
        {
            DebugLog($"HandleShellCommand: {command} failed: {ex.Message}");
            Console.Error.WriteLine($"Failed to execute {command}: {ex.Message}");
            exitCode = 1;
        }

        Environment.Exit(exitCode);
    }

    // ════════════════════════════════════════════════════════════════
    //  Elevation helpers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// 检测指定目录是否可写入。尝试创建测试文件并用 DeleteOnClose 自动清理。
    /// </summary>
    private static bool IsDirectoryWritable(string dirPath)
    {
        try
        {
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            var testFile = Path.Combine(dirPath, Path.GetRandomFileName());
            using (var fs = File.Create(testFile, 1, FileOptions.DeleteOnClose)) { }
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
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// 以管理员权限重新启动当前进程，传递原始 CLI 参数。
    /// </summary>
    private static void RestartAsAdmin(string[] originalArgs)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

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
    /// 弹出权限不足对话框处理流程：
    ///   - 已提权 → 显示 ElevationFailedDialog
    ///   - 允许提权 → 显示 ElevationDialog → 用户选择提权则 RestartAsAdmin 后 shutdown
    ///   - 不允许/取消 → 显示 ElevationInfoDialog
    /// 返回 true 表示进程已重启（调用方无需再 shutdown），false 表示用户取消或已失败。
    /// </summary>
    private static async Task<bool> HandleElevationAsync(
        IReadOnlyList<string> unwritable,
        string[] originalArgs,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var owner = desktop.MainWindow;

        if (IsElevated())
        {
            // Already elevated but still failing — show failed dialog
            var dlg = new Dialogs.ElevationFailedDialog(unwritable);
            if (owner != null)
                await dlg.ShowDialog<bool?>(owner);
            else
                dlg.Show();
            return false;
        }

        // AllowElevation 开关：关闭时直接显示 ElevationInfoDialog，不弹提权确认（对齐 WPF App.Extract.cs:445-452 / App.Compress.cs:217 / App.Open.cs:122）
        var settings = AppSettings.Load();
        if (!settings.AllowElevation)
        {
            DebugLog("AllowElevation=false, showing ElevationInfoDialog");
            var infoDlg = new Dialogs.ElevationInfoDialog(unwritable);
            if (owner != null)
                await infoDlg.ShowDialog<bool?>(owner);
            else
                infoDlg.Show();
            return false;
        }

        // Show elevation confirm dialog
        var elevationDlg = new Dialogs.ElevationDialog(unwritable);
        bool? result;
        if (owner != null)
        {
            result = await elevationDlg.ShowDialog<bool?>(owner);
        }
        else
        {
            // No window available yet (CLI mode), use a temp owner
            var tempOwner = new Window { IsVisible = false };
            desktop.MainWindow = tempOwner;
            result = await elevationDlg.ShowDialog<bool?>(tempOwner);
        }

        if (result == true)
        {
            // User chose to elevate — restart as admin and shutdown current process
            RestartAsAdmin(originalArgs);
            AppShutdown.Request(desktop.Shutdown);
            return true;
        }
        else
        {
            // User declined — show info dialog
            var infoDlg = new Dialogs.ElevationInfoDialog(unwritable);
            if (owner != null)
                await infoDlg.ShowDialog<bool?>(owner);
            else
                infoDlg.Show();
            return false;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  IPC helpers (NamedPipe multi-instance)
    // ════════════════════════════════════════════════════════════════

    private static void StartPipeServer(List<string> allPaths, CancellationToken ct, string pipeName, ManualResetEventSlim readyEvent)
    {
        Task.Run(async () =>
        {
            try
            {
                readyEvent.Set();
                while (!ct.IsCancellationRequested)
                {
                    using var pipe = new NamedPipeServerStream(
                        pipeName, PipeDirection.In, -1,
                        PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                    try
                    {
                        await pipe.WaitForConnectionAsync(ct);
                        var receivedCount = 0;
                        using var reader = new StreamReader(pipe);
                        string? line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            lock (allPaths)
                            {
                                if (!allPaths.Contains(line) && (File.Exists(line) || Directory.Exists(line)))
                                {
                                    allPaths.Add(line);
                                    receivedCount++;
                                }
                            }
                        }
                        Debug.WriteLine($"PipeServer ({pipeName}): received {receivedCount} new paths from client");
                    }
                    catch (OperationCanceledException) { throw; }
                    finally { pipe.Dispose(); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception pipeEx) { Debug.WriteLine($"PipeServer ({pipeName}): error: {pipeEx.Message}"); }
        });
    }

    private static void SendPathsThroughPipe(List<string> paths, string pipeName)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe);
            foreach (var p in paths)
                writer.WriteLine(p);
            writer.Flush();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SendPathsThroughPipe ({pipeName}) failed: {ex.Message}");
        }
    }

}
