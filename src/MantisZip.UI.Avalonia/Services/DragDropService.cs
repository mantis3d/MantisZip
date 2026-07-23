using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// Orchestrates the post-drop extraction workflow:
/// detect target directory, expand items, extract with progress, handle conflicts.
/// </summary>
internal class DragDropService
{
    private readonly string _archivePath;
    private readonly ArchiveFormat _format;
    private readonly string? _password;
    private readonly Window _ownerWindow;
    private readonly AppSettings _settings;

    public DragDropService(string archivePath, ArchiveFormat format, string? password, Window ownerWindow)
    {
        _archivePath = archivePath;
        _format = format;
        _password = password;
        _ownerWindow = ownerWindow;
        _settings = AppSettings.Load();
    }

    /// <summary>
    /// Execute the full post-drop workflow:
    /// 1. Detect target directory (fallback to folder picker)
    /// 2. Expand selected items to flat file list
    /// 3. Show ProgressWindow and extract files with progress
    /// 4. Handle conflicts, cancellation, errors
    /// 5. Optionally open target folder after extraction
    /// </summary>
    public async Task ExecuteAfterDropAsync(
        IReadOnlyList<ArchiveItem> selectedItems,
        IReadOnlyList<ArchiveItem> allItems,
        MainWindowViewModel? vm)
    {
        // 1. Detect target directory
        var (targetDir, _) = DropTargetDetector.DetectTargetDirectory();

        // 2. Fallback folder picker if detection failed
        if (string.IsNullOrEmpty(targetDir))
        {
            targetDir = await PickFolderAsync();
            if (targetDir == null)
                return;
        }

        // 3. Expand selected items to flat file list
        var itemsToExtract = DragDropItemExpander.ExpandItems(selectedItems, allItems);
        if (itemsToExtract.Count == 0)
            return;

        // 4. Get conflict action from settings ("ask"/"overwrite"/"rename"/"skip")
        var conflictAction = _settings.FileConflictAction;

        // 5. Get selected directories for path trimming
        var selectedDirs = selectedItems.Where(i => i.IsDirectory).ToList();

        // 6. Show ProgressWindow (non-modal)
        var folderName = Path.GetFileName(targetDir);
        var pw = new ProgressWindow($"正在解压到 {folderName}...");
        pw.InitCancellation();
        pw.Show();

        int totalFiles = itemsToExtract.Count;
        int processedFiles = 0;

        try
        {
            // Create progress dispatcher that marshals to UI thread at Background priority
            var progress = ProgressViewModel.CreateBackgroundProgress(pw, p => pw.SetProgress(p));

            // 7. Extract in background thread
            await Task.Run(async () =>
            {
                foreach (var item in itemsToExtract)
                {
                    pw.CancellationToken.ThrowIfCancellationRequested();

                    var outputPath = DragDropItemExpander.GetExtractPath(item, selectedDirs, targetDir);

                    // Create output directory if needed
                    var outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                        Directory.CreateDirectory(outputDir);

                    // Handle file conflicts
                    if (File.Exists(outputPath))
                    {
                        switch (conflictAction)
                        {
                            case "skip":
                                Interlocked.Increment(ref processedFiles);
                                continue;
                            case "rename":
                                outputPath = GetUniquePath(outputPath);
                                break;
                            case "overwrite":
                            default:
                                File.Delete(outputPath);
                                break;
                        }
                    }

                    // Extract the entry
                    await ArchiveEntryExtractor.ExtractEntryAsync(
                        _archivePath, item.FullPath, outputPath,
                        _format, _password, pw.CancellationToken);

                    int current = Interlocked.Increment(ref processedFiles);
                    double pct = totalFiles > 0 ? (double)current / totalFiles * 100 : 100;
                    progress.Report(new ArchiveProgress
                    {
                        PercentComplete = pct,
                        CurrentFile = item.Name
                    });
                }
            }, pw.CancellationToken);

            // 8. Post-extraction: status message and optional folder open
            if (vm != null)
                vm.StatusMessage = $"解压完成: {processedFiles}/{totalFiles} 个文件到 {folderName}";

            if (_settings.OpenFolderAfterExtract)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = targetDir,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DragDropService] Failed to open folder: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 9. Handle cancellation
            if (vm != null)
                vm.StatusMessage = "拖拽解压已取消";
        }
        catch (Exception ex)
        {
            // 10. Handle errors
            if (vm != null)
                vm.StatusMessage = $"解压失败: {ex.Message}";
        }
        finally
        {
            // 11. Always close the progress window
            pw.Close();
        }
    }

    /// <summary>
    /// Show a folder picker dialog as fallback when DropTargetDetector fails.
    /// Uses Avalonia's StorageProvider API.
    /// </summary>
    private async Task<string?> PickFolderAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(_ownerWindow);
            if (topLevel == null)
                return null;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择解压目标文件夹",
                AllowMultiple = false
            });

            return folders.Count >= 1 ? folders[0].Path.LocalPath : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Generate a unique file path by appending " (1)", " (2)" etc. before the extension.
    /// Returns the original path if no unique variant is found (up to 99 attempts).
    /// </summary>
    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path) ?? ".";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (int i = 1; i <= 99; i++)
        {
            var candidate = Path.Combine(dir, $"{nameWithoutExt} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return path; // fallback: all 99 variants exist
    }
}
