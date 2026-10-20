using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.Core.Models;
using MantisZip.Core.Services;
using MantisZip.Core;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using System.Collections.ObjectModel;
using System.Threading;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// MainWindowViewModel 的 条目预览部分（自 MainWindowViewModel.cs 拆分，纯移动）。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// 双击文件：提取到临时目录并用系统默认程序打开。
    /// 与 WPF 版 DoubleClickOpenFileAsync 行为对齐。
    /// </summary>
    public async Task OpenEntryWithDefaultAppAsync(ArchiveItemModel entry)
    {
        var threshold = _appSettings.DoubleClickOpenThreshold;

        // 功能禁用（阈值为 0）
        if (threshold <= 0) return;
        if (CurrentArchivePath == null) return;

        // 检查格式是否支持单项提取（Tar/GZip/ISO 不支持）
        if (_currentFormat is ArchiveFormat.Tar or ArchiveFormat.GZip or ArchiveFormat.Iso)
        {
            await AppMessageBox.Show(
                LocalizationManager.T("Main_DoubleClickFormatNotSupported"),
                LocalizationManager.T("App_ErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 检查密码
        if (_hasEncryptedArchive && string.IsNullOrEmpty(_currentPassword))
        {
            await AppMessageBox.Show(
                LocalizationManager.T("Main_DoubleClickPasswordNeeded"),
                LocalizationManager.T("App_ErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 超过阈值时弹出确认框
        if (entry.Size > threshold)
        {
            var sizeStr = FormatUtil.FormatSize(entry.Size);
            var result = await AppMessageBox.Show(
                LocalizationManager.T("Main_DoubleClickOpenConfirm", sizeStr),
                LocalizationManager.T("App_ConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }

        // 临时目录（独立于预览临时目录，避免被 ClearPreviewTemp 清理）
        var tempDir = Path.Combine(AppSettings.GetTempDir(), "OpenWith", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, entry.Name);

        try
        {
            App.DebugLog($"OpenEntryWithDefaultAppAsync: extracting '{entry.FullPath}' ({FormatUtil.FormatSize(entry.Size)}) from '{CurrentArchivePath}'");

            var password = _currentPassword ?? GetSessionPassword(CurrentArchivePath);

            // 大文件（>= 1MB）显示进度窗口，否则直接提取
            if (entry.Size >= 1024 * 1024 && RunWithProgress != null)
            {
                var completed = await RunWithProgress(
                    LocalizationManager.T("Status_Extracting"),
                    new[] { tempFile },
                    async (progress, ct) =>
                    {
                        await ArchiveEntryExtractor.ExtractEntryAsync(
                            CurrentArchivePath, entry.FullPath, tempFile, _currentFormat, password, ct);
                    });

                if (!completed)
                {
                    // 取消或失败：清理临时目录
                    TryDeleteTempDir(tempDir);
                    return;
                }
            }
            else
            {
                await ArchiveEntryExtractor.ExtractEntryAsync(
                    CurrentArchivePath, entry.FullPath, tempFile, _currentFormat, password);
            }

            App.DebugLog($"OpenEntryWithDefaultAppAsync: extracted to '{tempFile}', opening with default app");

            // 用系统默认程序打开
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempFile,
                UseShellExecute = true
            });

            StatusMessage = LocalizationManager.T("Main_Status_DoubleClickOpened", entry.Name);
        }
        catch (Exception ex)
        {
            App.DebugLog($"OpenEntryWithDefaultAppAsync: failed: {ex.Message}");
            await AppMessageBox.Show(
                LocalizationManager.T("Main_Status_ExtractFailed", ex.Message),
                LocalizationManager.T("App_ErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            TryDeleteTempDir(tempDir);
        }
    }

    private static void TryDeleteTempDir(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private async Task ShowPreviewAsync(ArchiveItemModel entry)
    {
        App.DebugLog($"[PRV] ShowPreviewAsync start: {entry.Name}, fmt={_currentFormat}");

        // Phase 1: Immediate — show loading state + populate info panel from in-memory data.
        // This runs synchronously before any async extraction, so user never sees stale content.
        var version = Interlocked.Increment(ref _previewLoadVersion);
        PreviewPanelNeeded?.Invoke(); // 兜底：面板未建好时同步创建（幂等，View 侧 EnsurePreviewPanel）
        Preview.StopGifTimer();
        Preview.ShowLoading(entry.NameDisplay ?? entry.Name);
        Preview.UpdateCommonMetadata(
            entry.NameDisplay ?? entry.Name,
            entry.SizeDisplay,
            entry.CompressedSizeDisplay,
            entry.Size > 0 ? $"{entry.CompressionRatio:F1}%" : "N/A",
            entry.LastModifiedDisplay);
        StatusMessage = LocalizationManager.T("Status_Extracting");

        try
        {
            var ext = Path.GetExtension(entry.Name);

            // 目录条目：没有可预览内容，直接显示"不支持预览"。
            // 必须短路，否则魔数检测会对 0 字节目录条目发起无意义的 ExtractHeadAsync 提取。
            // 目录的名称/大小/日期/压缩率已由 Phase 1 的 UpdateCommonMetadata 用目录聚合值填充，
            // 因此这里与旧路径（跑完魔数检测再落到 ShowUnsupported）的最终显示效果一致。
            if (entry.IsDirectory)
            {
                App.DebugLog("[PRV] Directory entry, skipping preview pipeline");
                Preview.ShowUnsupported();
                StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                return;
            }

            // 加密条目且当前无匹配密码：不发起提取，直接提示需要密码。
            // 按条目判断——混合压缩包中未加密的文件仍可正常预览。
            if (entry.IsEncrypted && string.IsNullOrEmpty(_currentPassword))
            {
                Preview.ShowUnsupported(LocalizationManager.T("Preview_EntryNeedsPassword"));
                StatusMessage = LocalizationManager.T("Status_Encrypted");
                return;
            }

            // ── Magic detection ──
            var previewType = PreviewType.Unsupported;
            FileFormat magicFormat = FileFormat.Unknown;
            string? detectedFormatName = null;
            if (PreviewService.EnableFormatDetection && CurrentArchivePath != null)
            {
                try
                {
                    _sessionPasswords.TryGetValue(GetSessionPasswordKey(CurrentArchivePath, _currentFormat), out var pwd);
                    var (magicType, format, displayName) = await PreviewService.ClassifyPreviewByMagicAsync(
                        CurrentArchivePath, entry, _currentFormat,
                        PreviewService.PreviewHeadSize, pwd);
                    if (magicType != PreviewType.Unsupported && format != FileFormat.Unknown)
                    {
                        previewType = magicType;
                        magicFormat = format;
                        detectedFormatName = displayName;
                        App.DebugLog($"[PRV] Magic detected: {format} ({displayName}) -> {previewType}");
                    }
                }
                catch (Exception ex)
                {
                    App.DebugLog($"[PRV] Magic detection failed: {ex.Message}");
                }
            }

            if (previewType == PreviewType.Unsupported)
            {
                previewType = PreviewService.ClassifyPreview(ext);
                App.DebugLog($"[PRV] Fallback to extension classification: {previewType}");
            }

            if (detectedFormatName == null && PreviewService.EnableFormatDetection)
            {
                var extFormat = FileFormatDetector.DetectByExtension(ext);
                if (extFormat != FileFormat.Unknown)
                    detectedFormatName = FileFormatHelper.GetDisplayName(extFormat);
            }

            if (previewType == PreviewType.Unsupported)
            {
                Preview.ShowUnsupported();
                StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                return;
            }

            if (CurrentArchivePath == null)
            {
                App.DebugLog("[PRV] CurrentArchivePath is null, aborting");
                return;
            }

            // ── 预览大小上限检查（仅对需要加载完整内容的格式生效，只读头的格式不受限制）──
            // 与 WPF 版 MainWindow.Preview.cs 语义一致；文本类格式另有 MaxTextPreviewBytes 上限
            if (!PreviewService.IsMetadataOnlyExtension(ext) && entry.Size > PreviewService.MaxPreviewFileSize)
            {
                var limitMb = PreviewService.MaxPreviewFileSize / (1024.0 * 1024.0);
                Preview.ShowUnsupported(LocalizationManager.T(
                    "Preview_TooLarge", (double)entry.Size / 1024 / 1024, limitMb));
                StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                return;
            }

            // ── Extract to temp (async, slow) ──
            var tempFile = await PreviewService.ExtractToTempAsync(
                CurrentArchivePath, entry, _currentFormat, _currentPassword);
            App.DebugLog($"[PRV] Extracted to: {tempFile}");

            if (tempFile == null)
            {
                Preview.ShowUnsupported(LocalizationManager.T("Status_ExtractFailed"));
                return;
            }

            // Version guard: if user selected another file while extracting, discard
            if (version != _previewLoadVersion)
            {
                App.DebugLog($"[PRV] Stale preview result discarded (version {version} != {_previewLoadVersion})");
                try { File.Delete(tempFile); } catch { /* best effort */ }
                return;
            }

            // Phase 2: Content loaded — show the actual preview
            switch (previewType)
            {
                case PreviewType.Text:
                    if (!PreviewService.EnableTextPreview)
                    {
                        Preview.ShowUnsupported(LocalizationManager.T("Preview_TextDisabled"));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    if (entry.Size > PreviewService.MaxTextPreviewBytes)
                    {
                        var txtLimitMb = PreviewService.MaxTextPreviewBytes / (1024.0 * 1024.0);
                        Preview.ShowUnsupported(LocalizationManager.T(
                            "Preview_TooLargeText", (double)entry.Size / 1024 / 1024, txtLimitMb));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    Preview.ShowText(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Text", entry.DisplayName);
                    break;
                case PreviewType.Csv:
                    if (!PreviewService.EnableTextPreview)
                    {
                        Preview.ShowUnsupported(LocalizationManager.T("Preview_TextDisabled"));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    if (entry.Size > PreviewService.MaxTextPreviewBytes)
                    {
                        var txtLimitMb = PreviewService.MaxTextPreviewBytes / (1024.0 * 1024.0);
                        Preview.ShowUnsupported(LocalizationManager.T(
                            "Preview_TooLargeText", (double)entry.Size / 1024 / 1024, txtLimitMb));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    Preview.ShowCsv(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Csv", entry.DisplayName);
                    break;
                case PreviewType.Pe:
                    Preview.ShowPe(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Pe", entry.DisplayName);
                    break;
                case PreviewType.Image:
                    if (!PreviewService.EnableImagePreview)
                    {
                        Preview.ShowUnsupported(LocalizationManager.T("Preview_ImageDisabled"));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    var icoExt = Path.GetExtension(tempFile).ToLowerInvariant();
                    if (icoExt == ".ico")
                    {
                        Preview.ShowIcoGallery(tempFile);
                        StatusMessage = LocalizationManager.T("Preview_Ico", entry.DisplayName);
                    }
                    else
                    {
                        Preview.ShowImage(tempFile);
                        StatusMessage = LocalizationManager.T("Preview_Image", entry.DisplayName);
                    }
                    break;
                case PreviewType.AnimatedImage:
                    if (!PreviewService.EnableImagePreview)
                    {
                        Preview.ShowUnsupported(LocalizationManager.T("Preview_ImageDisabled"));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    Preview.ShowGif(tempFile);
                    var isGifFile = ext.Equals(".gif", StringComparison.OrdinalIgnoreCase);
                    StatusMessage = LocalizationManager.T(
                        isGifFile ? "Preview_Gif" : "Preview_AnimatedImage", entry.DisplayName);
                    break;
                case PreviewType.Svg:
                    if (!PreviewService.EnableImagePreview)
                    {
                        Preview.ShowUnsupported(LocalizationManager.T("Preview_ImageDisabled"));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    Preview.ShowSvg(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Svg", entry.DisplayName);
                    break;
                case PreviewType.Font:
                    Preview.ShowFont(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Font", entry.DisplayName);
                    break;
                case PreviewType.Audio:
                    Preview.ShowAudio(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Audio", entry.DisplayName);
                    break;
                case PreviewType.Sqlite:
                    Preview.ShowSqlitePreview(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Sqlite", entry.DisplayName);
                    break;
                case PreviewType.Iso:
                    Preview.ShowIso(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Iso", entry.DisplayName);
                    break;
                case PreviewType.Torrent:
                    Preview.ShowTorrent(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Torrent", entry.DisplayName);
                    break;
                case PreviewType.Office:
                    // Legacy Office type: try magic-detected types
                    Preview.ShowOffice(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Office", entry.DisplayName);
                    break;
                case PreviewType.Docx:
                    Preview.ShowDocx(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Docx", entry.DisplayName);
                    break;
                case PreviewType.Xlsx:
                    Preview.ShowXlsx(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Xlsx", entry.DisplayName);
                    break;
                case PreviewType.Pptx:
                    Preview.ShowPptx(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Pptx", entry.DisplayName);
                    break;
                case PreviewType.Video:
                    Preview.ShowVideo(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Video", entry.DisplayName);
                    break;
                case PreviewType.Html:
                    if (!PreviewService.EnableTextPreview)
                    {
                        Preview.ShowUnsupported(LocalizationManager.T("Preview_TextDisabled"));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    if (entry.Size > PreviewService.MaxTextPreviewBytes)
                    {
                        var txtLimitMb = PreviewService.MaxTextPreviewBytes / (1024.0 * 1024.0);
                        Preview.ShowUnsupported(LocalizationManager.T(
                            "Preview_TooLargeText", (double)entry.Size / 1024 / 1024, txtLimitMb));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    await Preview.ShowHtmlPreview(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Html", entry.DisplayName);
                    break;
                case PreviewType.Pdf:
                    await Preview.ShowPdfAsync(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Pdf", entry.DisplayName);
                    break;
                case PreviewType.Markdown:
                    if (!PreviewService.EnableTextPreview)
                    {
                        Preview.ShowUnsupported(LocalizationManager.T("Preview_TextDisabled"));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    if (entry.Size > PreviewService.MaxTextPreviewBytes)
                    {
                        var txtLimitMb = PreviewService.MaxTextPreviewBytes / (1024.0 * 1024.0);
                        Preview.ShowUnsupported(LocalizationManager.T(
                            "Preview_TooLargeText", (double)entry.Size / 1024 / 1024, txtLimitMb));
                        StatusMessage = LocalizationManager.T("Status_Unsupported", ext);
                        break;
                    }
                    Preview.ShowMarkdownPreview(tempFile);
                    StatusMessage = LocalizationManager.T("Preview_Markdown", entry.DisplayName);
                    break;
                case PreviewType.NeedsPassword:
                    // 加密文件名压缩包：提示需输入密码
                    Preview.ShowNeedsPassword(entry.NameDisplay ?? entry.Name);
                    StatusMessage = LocalizationManager.T("Preview_EntryNeedsPassword");
                    break;
            }

            // Populate format metadata from magic detection (if any)
            if (detectedFormatName != null)
            {
                var extFormat = FileFormatDetector.DetectByExtension(ext);
                bool hasConflict = extFormat != FileFormat.Unknown
                    && magicFormat != FileFormat.Unknown
                    && extFormat != magicFormat;
                string formatValue = hasConflict
                    ? LocalizationManager.T("Preview_FormatConflictWarn", detectedFormatName, ext)
                    : detectedFormatName;
                for (int i = Preview.FormatMetadata.Count - 1; i >= 0; i--)
                {
                    if (Preview.FormatMetadata[i].Key == LocalizationManager.T("Preview_FormatLabel"))
                        Preview.FormatMetadata.RemoveAt(i);
                }
                Preview.FormatMetadata.Insert(0, new FormatMetadataItem(LocalizationManager.T("Preview_FormatLabel"), formatValue));
            }
        }
        catch (Exception ex)
        {
            App.DebugLog($"[PRV] ShowPreviewAsync EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Preview.ShowUnsupported(LocalizationManager.T("Status_PreviewFailed", ex.Message));
            StatusMessage = LocalizationManager.T("Status_PreviewFailed", ex.Message);
        }
        App.DebugLog("[PRV] ShowPreviewAsync end");
    }

}
