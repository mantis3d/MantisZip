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
/// MainWindowViewModel 的 压缩族部分（自 MainWindowViewModel.cs 拆分，纯移动）。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    [RelayCommand]
    private async Task NewArchive()
    {
        if (ShowCompressSettingsDialog == null || RunWithProgress == null) return;
        var vm = new CompressSettingsViewModel(Array.Empty<string>());
        var result = await ShowCompressSettingsDialog(vm);
        if (result != true || vm.SelectedPaths.Count == 0) return;

        await ExecuteCompressFromSettings(vm);
    }

    /// <summary>
    /// 拖入压缩入口：未打开压缩包时从资源管理器拖入文件/目录 → 压缩对话框预填源路径，
    /// 确认后走与「新建压缩包」完全一致的执行管道（对话框选项拷贝回传 + AdoptPlan +
    /// CompressFlow 构建请求 + 进度窗口 + 冲突/错误弹窗 + 密码库保存）。
    /// 回归背景：分支 3b 曾在 ShowDialog 后直接返回，无人执行压缩（点击「压缩」关窗后无任何反应）。
    /// </summary>
    public async Task DragInCompressAsync(IReadOnlyList<string> droppedPaths)
    {
        if (ShowCompressSettingsDialog == null || RunWithProgress == null) return;
        var vm = new CompressSettingsViewModel(droppedPaths);
        var result = await ShowCompressSettingsDialog(vm);
        if (result != true || vm.SelectedPaths.Count == 0) return;

        await ExecuteCompressFromSettings(vm);
    }

    /// <summary>
    /// 从 CompressSettingsViewModel 读取设置，构建 CompressRequest 并执行压缩。
    /// Request 构建与冲突处理复用 CompressFlow（与 CLI 右键菜单共用同一流程）。
    /// </summary>
    private async Task ExecuteCompressFromSettings(CompressSettingsViewModel vm)
    {
        var request = CompressFlow.BuildRequest(vm);
        if (request == null)
        {
            await AppMessageBox.Show(
                LocalizationManager.T("Compress_FilteredAllSkipped"),
                LocalizationManager.T("Compress_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (RunWithProgress == null) return;
        PendingCompressFlow = true; // Task 6: 压缩流程 → 进度窗口显示「压缩通道」说明行
        PendingCompressRequest = request; // 列表播种：供 RunWithProgress 枚举全量条目
        var completed = await RunWithProgress(
            LocalizationManager.T("Status_Compressing"),
            AvaloniaCompressService.GetOutputPaths(request),
            async (progress, ct) =>
            {
                var svc = new AvaloniaCompressService();
                await svc.CompressAsync(request, progress, ct,
                    conflictResolver: CompressFlow.CreateResolver(async info =>
                    {
                        if (ShowCompressConflictDialog == null)
                            return (Core.Abstractions.CompressConflictAction.Overwrite, null, false);
                        return await ShowCompressConflictDialog(info);
                    }, request.ConflictAction),
                    onItemStatus: BatchStatusReporter,
                    onItemOutputPath: BatchOutputPathReporter);
            });

        if (completed)
        {
            StatusMessage = LocalizationManager.T("Status_Compressed");

            // 压缩成功后把输出目录写入路径历史（覆盖手输地址栏直接点压缩等此前不入史的场景）
            CompressFlow.RecordOutputHistory(request);

            // Save or update password in the password library (matches WPF SavePasswordAfterCompress logic).
            // Must run after compress succeeds, before the dialog closes.
            if (vm.SaveToLibrary && vm.Encrypt)
            {
                try
                {
                    var rules = vm.RulesText
                        ?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(r => r.Trim())
                        .Where(r => !string.IsNullOrWhiteSpace(r))
                        .ToList() ?? new List<string>();

                    if (rules.Count == 0)
                    {
                        var ext = vm.DefaultFormat == "tar.gz" ? ".tar.gz" : "." + vm.DefaultFormat;
                        rules.Add($"*{ext}");
                    }

                    if (vm.IsPasswordLibraryMode && vm.SelectedPasswordEntry != null)
                    {
                        // Update matching rules: deduplicate and append
                        var entry = vm.SelectedPasswordEntry;
                        var updated = false;
                        foreach (var rule in rules)
                        {
                            if (!entry.Patterns.Contains(rule))
                            {
                                entry.Patterns.Add(rule);
                                updated = true;
                            }
                        }
                        if (updated)
                        {
                            PasswordManager.Instance.UpdatePassword(
                                entry.Id, entry.Password, entry.Description, entry.Patterns);
                            PasswordManager.Instance.MarkUsed(entry.Id);
                            App.DebugLog($"Password rules updated for entry: {entry.Description}");
                        }
                    }
                    else if (!vm.IsPasswordLibraryMode)
                    {
                        // New password entry (not library mode)
                        var password = vm.Password;
                        var desc = vm.PasswordDescription?.Trim() ?? "";
                        if (string.IsNullOrEmpty(desc))
                            desc = $"Compressed on {DateTime.Now:yyyy-MM-dd HH:mm}";

                        if (password != null)
                        {
                            PasswordManager.Instance.AddPassword(password, desc, rules);
                            App.DebugLog($"Password saved to library: {desc}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.DebugLog($"SavePasswordAfterCompress failed: {ex.Message}");
                }
            }
        }
    }

    [RelayCommand]
    private async Task AddFiles()
    {
        if (CurrentArchivePath == null || GetOpenFilePaths == null) return;

        var files = await GetOpenFilePaths();
        if (files == null || files.Count == 0) return;

        await AddFilesToArchiveAsync(files);
    }

    /// <summary>
    /// 将指定文件/文件夹添加到当前压缩包（复用解压冲突处理策略 + entryBasePath=当前浏览目录）。
    /// 供工具栏「添加文件」命令与窗口拖拽添加共用，保证行为一致。
    /// </summary>
    /// <param name="files">要添加的本地文件/文件夹路径。</param>
    /// <returns>true=添加完成（含刷新），false=无压缩包/无引擎/被取消/失败。</returns>
    public async Task<bool> AddFilesToArchiveAsync(IReadOnlyList<string> files)
    {
        if (CurrentArchivePath == null || RunWithProgress == null || files.Count == 0) return false;

        var engine = ArchiveEngineFactory.GetEngineByExtension(CurrentArchivePath);
        if (engine == null) return false;

        _sessionPasswords.TryGetValue(GetSessionPasswordKey(CurrentArchivePath, _currentFormat), out var password);

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_AddingFiles"),
            files.ToArray(),
            async (progress, ct) =>
            {
                // 复用解压冲突处理：同一 AppSettings.FileConflictAction 策略 + Ask 弹窗回调（标题区分）
                var settings = AppSettings.Load();
                var options = SelectedItemsExtractService.CreateExtractOptions(
                    settings.FileConflictAction, ShowAddFileConflictDialogAsync);
                options.Password = password;
                // 文件名编码：与压缩对话框同源，透传用户的 ZIP 文件名编码设置。
                // 此前未透传，引擎只能靠包内 bit 11 启发式猜编码，导致拖拽添加中文文件名乱码。
                options.FileNameEncoding = settings.ZipEncoding;
                // 源文件读取错误（被占用等）→ 弹 ErrorDialog（重试/跳过/中止）
                options.ErrorResolver = CompressFlow.CreateErrorResolver();
                // entryBasePath：当前浏览的压缩包内目录，null=根目录（与 WPF 版行为一致）
                await engine.AddToArchiveAsync(CurrentArchivePath, files.ToArray(), options, progress, ct,
                    entryBasePath: string.IsNullOrEmpty(CurrentFolder) ? null : CurrentFolder);
            });

        if (completed)
        {
            StatusMessage = LocalizationManager.T("Status_AddComplete");
            await RefreshArchive();
        }
        return completed;
    }

    [RelayCommand]
    private async Task DeleteFiles()
    {
        if (CurrentArchivePath == null || SelectedEntry == null || RunWithProgress == null) return;

        var entryPath = SelectedEntry.FullPath ?? SelectedEntry.Name;
        if (entryPath == null) return;
        string deleteEntryPath = entryPath; // guard narrows to non-null; explicit string type for array inference

        var engine = ArchiveEngineFactory.GetEngineByExtension(CurrentArchivePath);
        if (engine == null) return;

        // 只读格式（tar/gz…）不支持删除：入口按钮已按 CanDeleteEntries 禁用，这里再兜底一次，
        // 防止命令经快捷键 / 自动化等路径绕过按钮状态被触发。
        if (!engine.CanDelete(_currentFormat)) return;

        // 删除目录带递归语义（引擎按「条目本身 + 其下全部子条目」匹配），故确认框必须先把影响面
        // 算清楚：只写「删除此项？」会让用户以为只删掉一个空目录行。
        var (fileCount, totalSize) = GetDeleteTargetStats(SelectedEntry);
        var sizeText = FormatUtil.FormatSize(totalSize);
        var confirmMessage = SelectedEntry.IsDirectory
            ? LocalizationManager.T("Confirm_DeleteDirectoryFmt", deleteEntryPath, fileCount, sizeText)
            : LocalizationManager.T("Confirm_DeleteFileFmt", deleteEntryPath, sizeText);

        var confirm = await AppMessageBox.Show(
            confirmMessage,
            LocalizationManager.T("App_ConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        _sessionPasswords.TryGetValue(GetSessionPasswordKey(CurrentArchivePath, _currentFormat), out var password);

        // 删除是整包重写、引擎无逐条目回调：进度窗用一行说明代替 N 行假进度（见计划决策记录）。
        PendingOperationNote = LocalizationManager.T("Progress_DeleteNoteFmt", fileCount, sizeText);

        // 完成即刷新列表：RunWithProgress 并非操作一结束就返回——它还要等「完成态 → 自动关闭
        // 倒计时（默认 3 秒）→ 关窗」才返回。若等到返回后再刷新，用户在整个读秒期间看到的都是
        // 删除前的旧列表（本次修复实测反馈的「完成后一两秒才刷新」）。改在完成回调里刷新，
        // 进度窗显示「删除完成」的同时主窗口列表已在重读。
        // 注意：一旦占用 PendingCompletedCallback，进度窗的完成态就由本回调负责写入，
        // 默认那条 SetComplete(Cli_StatusDone) 不会再执行。
        PendingCompletedCallback = pw =>
        {
            StatusMessage = LocalizationManager.T("Status_DeleteComplete");
            pw.SetComplete(LocalizationManager.T("Status_DeleteComplete"));
            _ = RefreshArchiveAfterDeleteAsync();
        };

        await RunWithProgress(
            LocalizationManager.T("Status_DeletingFiles"),
            new[] { deleteEntryPath },
            async (progress, ct) =>
            {
                // 删除会整包重写，须透传用户的 ZIP 文件名编码设置，
                // 否则存活的其它条目可能被降级重写成乱码（与 AddFilesToArchiveAsync 同源）。
                await engine.DeleteEntriesAsync(CurrentArchivePath, new[] { deleteEntryPath }, password, progress, ct,
                    new ArchiveOptions { FileNameEncoding = AppSettings.Load().ZipEncoding });
            });
    }

    /// <summary>
    /// 删除完成后的列表刷新（由进度窗完成回调以 fire-and-forget 方式发起）。
    /// 异常必须在这里吞掉：没有 await 的 Task 抛异常会变成未观察异常，而此刻用户已经在看
    /// 进度窗的完成态，列表刷新失败不该再打断他；写日志留痕即可。
    /// </summary>
    private async Task RefreshArchiveAfterDeleteAsync()
    {
        try
        {
            await RefreshArchive();
        }
        catch (Exception ex)
        {
            App.DebugLog($"[DeleteFiles] 删除后刷新列表失败：{ex}");
        }
    }

    /// <summary>
    /// 计算删除目标的影响面（文件数 + 总大小），供确认框与进度窗说明行使用。
    /// 目录只统计其下文件（不含目录条目本身与空目录），口径与
    /// <see cref="ArchiveEntryLister.ComputeDirectoryStats"/> 的 Count 一致；
    /// 数据源取未过滤的原始条目列表，避免受当前搜索/过滤条件影响。
    /// </summary>
    private (int FileCount, long TotalSize) GetDeleteTargetStats(ArchiveItemModel entry)
    {
        if (!entry.IsDirectory) return (1, entry.Size);

        var path = entry.FullPath ?? entry.Name;
        if (string.IsNullOrEmpty(path)) return (0, 0L);

        var dirStats = ArchiveEntryLister.ComputeDirectoryStats(_allRawItems ?? Array.Empty<ArchiveItem>());
        return dirStats.TryGetValue(path, out var stat) ? (stat.Count, stat.Size) : (0, 0L);
    }

}
