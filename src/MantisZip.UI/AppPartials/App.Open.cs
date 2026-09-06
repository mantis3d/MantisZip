using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MantisZip.Core;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using MantisZip.UI.Localization;

namespace MantisZip.UI;

/// <summary>
/// 打开/快速压缩命令处理器 — Open 和 Quick Compress 相关方法的 partial 定义
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 处理 --open 模式：启动主窗口并加载压缩包供浏览。
    /// </summary>
    private static void HandleOpen(string? archivePath)
    {
        var mainWin = new MainWindow();

        if (!string.IsNullOrEmpty(archivePath) && File.Exists(archivePath))
        {
            mainWin.Loaded += async (_, _) =>
            {
                await Task.Delay(200);
                await mainWin.LoadArchiveAsync(archivePath);
            };
        }

        mainWin.Show();
    }
    /// <summary>
    /// 处理 --compress-quick 模式：不显示设置窗口，使用 AppSettings 默认值直接压缩。
    /// </summary>
    private static void HandleCompressQuick(string[] paths)
    {
        try
        {
            LogStartup($"HandleCompressQuick: paths=[{string.Join(";", paths)}]");
            var app = Current;
            if (app == null) return;

            // 手动控制生命周期
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var myPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (myPaths.Count == 0)
            {
                LogStartup("HandleCompressQuick: no valid paths, shutting down");
                app.Shutdown();
                return;
            }

            var settings = AppSettings.Instance;

            // 自动确定输出路径
            var first = myPaths[0];
            string? dir;
            if (File.Exists(first))
                dir = Path.GetDirectoryName(first);
            else
                dir = ArchivePath.GetDirectoryName(first);
            dir ??= Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var baseName = settings.KeepOriginalExtension
                ? ArchivePath.GetFileName(first)
                : ArchivePath.GetFileNameWithoutExtension(first);

            var ext = settings.DefaultFormat == "tar.gz" ? ".tar.gz" : "." + settings.DefaultFormat;
            var outputPath = Path.Combine(dir, baseName + ext);
            LogStartup($"HandleCompressQuick: baseName={baseName}, ext={ext}, outputPath={outputPath}");

        var progressWindow = new ProgressWindow();
        progressWindow.InitCancellation();
        progressWindow.Show();
        app.MainWindow = progressWindow;
        progressWindow.SetProgress(0, L.T(L.App_CompressPreparing));

        var request = new CompressRequest
        {
            SourcePaths = myPaths,
            Mode = CompressOutputMode.Manual,
            Format = settings.DefaultFormat,
            CompressionLevel = settings.DefaultLevel,
            KeepOriginalExtension = settings.KeepOriginalExtension,
            OutputPath = outputPath,
            PreserveDirectoryRoot = settings.PreserveDirectoryRoot,
            FileNameEncoding = settings.ZipEncoding,
            SevenZipCompressionMethod = settings.SevenZipCompressionMethod,
            SevenZipSolid = settings.SevenZipSolid,
        };
        var outputPaths = CompressService.GetOutputPaths(request);
        progressWindow.InitBatchMode(outputPaths);
        progressWindow.SetCurrentBatchItem(0);

        bool applyToAll = false;
        Core.Abstractions.CompressConflictAction? chosenAction = null;

        var rawProgress = ProgressWindow.CreateBackgroundProgress(progressWindow);
        var progress = rawProgress;
        var ct = progressWindow.CancellationToken;

        // ===== 权限检测：检查输出目录的可写性 =====
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !IsDirectoryWritable(outputDir))
        {
            if (IsElevated())
            {
                ShowElevationFailedDialog(new[] { outputDir });
                app.Shutdown();
                return;
            }

            if (!AppSettings.Instance.AllowElevation)
            {
                ShowElevationInfoDialog(new[] { outputDir });
                app.Shutdown();
                return;
            }

            var result = ShowElevationDialog(new[] { outputDir });
            if (result == true)
            {
                RelaunchAsAdmin(Environment.GetCommandLineArgs().Skip(1).ToArray());
                app.Shutdown();
                return;
            }
            else
            {
                app.Shutdown();
                return;
            }
        }
        // ===== 权限检测结束 =====

        Task.Run(async () =>
            {
                try
                {
                    LogStartup("HandleCompressQuick: starting via CompressService");

                    var result = await CompressService.CompressAsync(
                        request,
                        conflictResolver: async info =>
                        {
                            return progressWindow.Dispatcher.Invoke(() =>
                            {
                                if (applyToAll && chosenAction.HasValue)
                                    return new CompressConflictResolution(chosenAction.Value, null);

                                var dlg = new CompressConflictDialog(info.OutputPath, info.CanAdd, info.SuggestedName);
                            dlg.Owner = progressWindow;
                                var shown = dlg.ShowDialog() == true;
                                if (dlg.ApplyToAll)
                                {
                                    applyToAll = true;
                                    chosenAction = (Core.Abstractions.CompressConflictAction)dlg.ResultAction;
                                }
                                return new CompressConflictResolution(
                                    shown ? (Core.Abstractions.CompressConflictAction)dlg.ResultAction : Core.Abstractions.CompressConflictAction.Cancel,
                                    dlg.CustomName);
                            });
                        },
                        progress,
                        ct);

                    progressWindow.FinalizeBatch();

                    if (result.Failed > 0)
                    {
                        LogStartup($"HandleCompressQuick: failed");
                        await progressWindow.Dispatcher.InvokeAsync(() =>
                        {
                            AppMessageBox.Show(L.TF(L.App_CompressFailed, "See details in log"), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                            app.Shutdown();
                        });
                    }
                    else
                    {
                        LogStartup("HandleCompressQuick: completed successfully");
                        await progressWindow.Dispatcher.InvokeAsync(() =>
                            progressWindow.SetComplete(L.T(L.App_CompressComplete)));
                        await progressWindow.AutoCloseOrWaitAsync(800, () => progressWindow.Dispatcher.Invoke(() => app.Shutdown()));
                    }
                }
                catch (OperationCanceledException)
                {
                    LogStartup("HandleCompressQuick: cancelled");
                    await progressWindow.Dispatcher.InvokeAsync(() => app.Shutdown());
                }
                catch (Exception ex)
                {
                    CoreLog.Trace("HandleOpen: failed: {0}", ex.Message);
                    LogStartup($"HandleCompressQuick: failed: {ex.Message}");
                    Log("--compress-quick 失败: {0}", ex.Message);
                    await progressWindow.Dispatcher.InvokeAsync(() =>
                    {
                        AppMessageBox.Show(L.TF(L.App_CompressFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
                        app.Shutdown();
                    });
                }
            });
        }
        catch (Exception ex)
        {
            CoreLog.Trace("HandleOpen: parse failed: {0}", ex.Message);
            LogStartup($"HandleCompressQuick: unexpected error: {ex.Message}\n{ex.StackTrace}");
            AppMessageBox.Show(L.TF(L.App_QuickCompressFailed, ex.Message), L.T(L.App_ErrorTitle), MessageBoxButton.OK, MessageBoxImage.Error);
            Current?.Shutdown();
        }
    }
}
