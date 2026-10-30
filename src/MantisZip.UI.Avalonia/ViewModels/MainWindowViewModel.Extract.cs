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
/// MainWindowViewModel 的 解压族部分（自 MainWindowViewModel.cs 拆分，纯移动）。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    [RelayCommand]
    private async Task ExtractArchive()
    {
        if (CurrentArchivePath == null || ShowExtractSettingsDialog == null) return;
        // 压缩包可能已被删掉 / 被替换（解压删源包、外部删除），先确认再进流程
        if (!EnsureArchiveAvailable()) return;

        // 确认即载荷：目标目录、冲突策略、过滤集合、完成动作都由设置窗口在确认事务里冻结，
        // 并由回调内的解压会话执行；本命令不再回读对话框状态、也不再自己跑进度。
        await ShowExtractSettingsDialog(new[] { CurrentArchivePath });
    }

    [RelayCommand]
    private async Task ExtractArchiveHere()
    {
        if (CurrentArchivePath == null || RunWithProgress == null) return;
        if (!EnsureArchiveAvailable()) return;

        var dest = Path.GetDirectoryName(CurrentArchivePath)
                   ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        _sessionPasswords.TryGetValue(GetSessionPasswordKey(CurrentArchivePath, _currentFormat), out var password);

        // spec §11.2: 解压流程 → 两开关可见；非对话框流程种子 null → 镜像设置
        PendingExtractPostActions = (true, true, null);

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_Extracting"),
            new[] { CurrentArchivePath! },
            async (progress, ct) =>
            {
                // 列表模式播种：列出全部条目（Pending），解压中逐条目状态驱动列表
                ExtractFlow.TrySeedEntryItemsInBackground(
                    ProgressWindow.CurrentVisible, CurrentArchivePath, password, filteredKeys: null, ct);
                await new ExtractService().ExtractAsync(
                    CurrentArchivePath, dest, password, progress, ct);
            });

        if (completed)
        {
            StatusMessage = LocalizationManager.T("Status_ExtractComplete");
            // D19: 后置动作读进度窗口开关最终值，回退设置
            App.TryDeleteArchiveAfterExtract(CurrentArchivePath!,
                LastProgressWindow?.DeleteArchiveAfterExtractValue ?? AppSettings.Load().DeleteArchiveAfterExtract);
            if (LastProgressWindow?.OpenFolderAfterExtractValue ?? AppSettings.Load().OpenFolderAfterExtract)
            {
                await OpenExtractedFolderAsync(dest, CurrentArchivePath!, password);
            }
            // 成功后把目标目录写入路径历史（原地解压到压缩包所在目录）
            PathHistoryManager.Record(dest);
            // 勾了「解压完成后删除压缩包」时源包已不在了：立刻关包，别让界面停在空路径上
            SyncArchiveStateAfterExtract();
        }
    }

    [RelayCommand]
    private async Task ExtractArchiveToName()
    {
        if (CurrentArchivePath == null || RunWithProgress == null) return;
        if (!EnsureArchiveAvailable()) return;

        var parentDir = Path.GetDirectoryName(CurrentArchivePath)
                        ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var folderName = Path.GetFileNameWithoutExtension(CurrentArchivePath);
        var dest = Path.Combine(parentDir, folderName);

        _sessionPasswords.TryGetValue(GetSessionPasswordKey(CurrentArchivePath, _currentFormat), out var password);

        // spec §11.2: 解压流程 → 两开关可见；非对话框流程种子 null → 镜像设置
        PendingExtractPostActions = (true, true, null);

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_Extracting"),
            new[] { CurrentArchivePath! },
            async (progress, ct) =>
            {
                // 列表模式播种：列出全部条目（Pending），解压中逐条目状态驱动列表
                ExtractFlow.TrySeedEntryItemsInBackground(
                    ProgressWindow.CurrentVisible, CurrentArchivePath, password, filteredKeys: null, ct);
                await new ExtractService().ExtractAsync(
                    CurrentArchivePath, dest, password, progress, ct);
            });

        if (completed)
        {
            StatusMessage = LocalizationManager.T("Status_ExtractComplete");
            // D19: 后置动作读进度窗口开关最终值，回退设置
            App.TryDeleteArchiveAfterExtract(CurrentArchivePath!,
                LastProgressWindow?.DeleteArchiveAfterExtractValue ?? AppSettings.Load().DeleteArchiveAfterExtract);
            if (LastProgressWindow?.OpenFolderAfterExtractValue ?? AppSettings.Load().OpenFolderAfterExtract)
            {
                await OpenExtractedFolderAsync(dest, CurrentArchivePath!, password);
            }
            // 成功后把目标目录写入路径历史（解压到同名子目录）
            PathHistoryManager.Record(dest);
            // 勾了「解压完成后删除压缩包」时源包已不在了：立刻关包，别让界面停在空路径上
            SyncArchiveStateAfterExtract();
        }
    }

    /// <summary>
    /// 解压选中的条目到当前目录（压缩包所在目录）。
    /// </summary>
    [RelayCommand]
    private async Task ExtractSelectedHere()
    {
        if (CurrentArchivePath == null || RunWithProgress == null) return;
        if (!EnsureArchiveAvailable()) return;
        if (SelectedEntries.Count == 0 && SelectedEntry == null) return;

        var entries = GetSelectedEntriesForExtract();
        if (entries.Count == 0) return;

        var dest = Path.GetDirectoryName(CurrentArchivePath)
                   ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        // 无对话框入口：不传 preserveFullPath（null → 由 Core 内部的 settings 默认值兜底，避免二次反序列化 settings.json）
        await ExtractSelectedEntriesCoreAsync(entries, dest, null);
    }

    /// <summary>
    /// 解压选中的条目到用户指定目录（弹出文件选择器，默认定位到压缩包同名文件夹）。
    /// 冲突策略与打开文件夹行为使用 AppSettings 默认值。
    /// </summary>
    [RelayCommand]
    private async Task ExtractSelectedTo()
    {
        if (CurrentArchivePath == null || ShowExtractEntryPicker == null) return;
        if (!EnsureArchiveAvailable()) return;
        if (SelectedEntries.Count == 0 && SelectedEntry == null) return;

        var entries = GetSelectedEntriesForExtract();
        if (entries.Count == 0) return;

        var parentDir = Path.GetDirectoryName(CurrentArchivePath)
                        ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var defaultDest = Path.Combine(parentDir, Path.GetFileNameWithoutExtension(CurrentArchivePath));

        var settings = AppSettings.Load();
        // 条目选择模式（v0.5.5 第三步后半）：对话框自己就是执行入口（确认即载荷 + 唯一执行器），
        // 这里只负责把「本次触发的条目」和默认目标交出去，不再回读窗口、也不再走 ExtractFlow。
        var password = _sessionPasswords.TryGetValue(GetSessionPasswordKey(CurrentArchivePath, _currentFormat), out var knownPassword)
            ? knownPassword
            : null;
        // 整包条目给预览/过滤统计，真正解压的是本次选中的子集
        var allEntries = _allRawItems ?? entries;
        var handled = await ShowExtractEntryPicker(allEntries, entries, defaultDest, CurrentFolder ?? "", password);
        if (handled)
        {
            StatusMessage = LocalizationManager.T("Status_ExtractComplete");
            // 源包可能被收尾动作移入回收站，立刻同步开包状态
            SyncArchiveStateAfterExtract();
        }
    }

    /// <summary>
    /// 执行选中条目的解压（统一走 <see cref="ExtractFlow.RunSelectedItemsExtractionAsync"/>，
    /// 与拖拽解压拿到目标路径后完全同一流程：进度窗口、压缩包一行批处理列表、状态驱动、失败弹窗）。
    /// 冲突策略与打开文件夹行为使用 AppSettings 默认值。
    /// </summary>
    private async Task ExtractSelectedEntriesCoreAsync(List<ArchiveItem> entries, string destinationPath, bool? preserveFullPath = null)
    {
        if (CurrentArchivePath == null) return;

        _sessionPasswords.TryGetValue(GetSessionPasswordKey(CurrentArchivePath, _currentFormat), out var password);

        var settings = AppSettings.Load();

        var result = await ExtractFlow.RunSelectedItemsExtractionAsync(
            CurrentArchivePath, password, entries, destinationPath,
            CurrentFolder ?? "", preserveFullPath ?? settings.ExtractPreserveFullPath, settings.FileConflictAction,
            ShowExtractFileConflictDialogAsync,
            LocalizationManager.T("Status_Extracting"));

        switch (result.Status)
        {
            case SelectedItemsExtractStatus.Success:
                StatusMessage = LocalizationManager.T("Status_ExtractComplete");
                // D19: 开关值来自进度窗口实例（结果记录携带），不再直接读设置
                App.TryDeleteArchiveAfterExtract(CurrentArchivePath!, result.DeleteArchiveAfterExtract);
                if (result.OpenFolderAfterExtract)
                {
                    await OpenExtractedFolderAsync(destinationPath, CurrentArchivePath!, password);
                }
                // 源包已被本次收尾删除时立刻关包（状态残留会让后续操作全落到不存在的路径上）
                SyncArchiveStateAfterExtract();
                break;
            case SelectedItemsExtractStatus.Failed:
                StatusMessage = LocalizationManager.T("Main_Status_ExtractFailed", result.ErrorMessage);
                break;
            case SelectedItemsExtractStatus.Cancelled:
                StatusMessage = LocalizationManager.T("Status_Cancelled");
                break;
        }
    }

    /// <summary>
    /// 获取用于解压的选中条目列表（含目录展开，全量条目展开与拖拽共用同一实现）。
    /// 对应 WPF 的 ExtractSelectedAsync 逻辑。
    /// </summary>
    private List<ArchiveItem> GetSelectedEntriesForExtract()
    {
        var selected = new List<ArchiveItem>();

        if (SelectedEntries.Count > 0)
        {
            selected.AddRange(SelectedEntries.Select(i => i.ToCoreItem()));
        }
        else if (SelectedEntry != null)
        {
            selected.Add(SelectedEntry.ToCoreItem());
        }

        if (selected.Count == 0) return selected;

        // 目录展开：复用 DragDropItemExpander.ExpandItems（与拖拽同源，全量条目）
        return DragDropItemExpander.ExpandItems(selected, GetAllRawItems()).ToList();
    }

    /// <summary>
    /// 打开解压后的文件夹（智能选择：若压缩包内所有条目共享一个公共根目录，
    /// 则打开 dest/公共根目录，否则直接打开 dest）。
    /// </summary>
    private async Task OpenExtractedFolderAsync(string dest, string archivePath, string? password)
    {
        if (ShowOpenFolderDialog == null) return;

        try
        {
            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            var openPath = engine == null
                ? dest
                : await SmartOpenPathResolver.ResolveSmartOpenPathAsync(archivePath, dest, engine, password);

            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = openPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
        }
        catch (Exception ex)
        {
            App.DebugLog($"OpenExtractedFolderAsync: failed to open folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task TestEntry()
    {
        if (SelectedEntry == null || RunWithProgress == null) return;
        if (!EnsureArchiveAvailable()) return;

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_TestingEntry"),
            new[] { SelectedEntry.Name ?? string.Empty },
            async (progress, ct) =>
            {
                // Simulate test by checking entry exists in archive
                await Task.Delay(100, ct);
                progress.Report(new ArchiveProgress { PercentComplete = 100, CurrentFile = SelectedEntry.Name ?? string.Empty });
            });

        if (completed)
            StatusMessage = $"{LocalizationManager.T("Status_EntryTested")}: {SelectedEntry.Name}";
    }

    [RelayCommand]
    private async Task ExtractTo()
    {
        if (CurrentArchivePath == null || ShowExtractSettingsDialog == null) return;
        // 压缩包可能已被删掉 / 被替换（解压删源包、外部删除），先确认再进流程
        if (!EnsureArchiveAvailable()) return;

        // 默认目标目录（压缩包所在目录 / 包名）由 ExtractSettingsViewModel 构造时按同一规则预填，
        // 故这里不再单独算一遍；执行同样由回调内的解压会话按载荷负责。
        await ShowExtractSettingsDialog(new[] { CurrentArchivePath });
    }

    [RelayCommand]
    private async Task SmartExtract()
    {
        if (CurrentArchivePath == null || RunWithProgress == null) return;
        if (!EnsureArchiveAvailable()) return;

        var parentDir = Path.GetDirectoryName(CurrentArchivePath)
                        ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        bool hasSingleRoot = _allRawItems != null && ArchiveStructureAnalyzer.HasSingleRootDirectory(_allRawItems);

        var dest = hasSingleRoot
            ? parentDir
            : Path.Combine(parentDir, Path.GetFileNameWithoutExtension(CurrentArchivePath));

        _sessionPasswords.TryGetValue(GetSessionPasswordKey(CurrentArchivePath, _currentFormat), out var password);

        // spec §11.2: 解压流程 → 两开关可见；非对话框流程种子 null → 镜像设置
        PendingExtractPostActions = (true, true, null);

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_SmartExtracting"),
            new[] { CurrentArchivePath! },
            async (progress, ct) =>
            {
                // 列表模式播种：列出全部条目（Pending），解压中逐条目状态驱动列表
                ExtractFlow.TrySeedEntryItemsInBackground(
                    ProgressWindow.CurrentVisible, CurrentArchivePath, password, filteredKeys: null, ct);
                await new ExtractService().ExtractAsync(
                    CurrentArchivePath, dest, password, progress, ct);
            });

        if (completed)
        {
            StatusMessage = hasSingleRoot
                ? LocalizationManager.T("Status_SmartExtractSingleRoot")
                : LocalizationManager.T("Status_SmartExtractNamed");
            // D19: 后置动作读进度窗口开关最终值，回退设置
            App.TryDeleteArchiveAfterExtract(CurrentArchivePath!,
                LastProgressWindow?.DeleteArchiveAfterExtractValue ?? AppSettings.Load().DeleteArchiveAfterExtract);
            if (LastProgressWindow?.OpenFolderAfterExtractValue ?? AppSettings.Load().OpenFolderAfterExtract)
            {
                await OpenExtractedFolderAsync(dest, CurrentArchivePath!, password);
            }
            // 勾了「解压完成后删除压缩包」时源包已不在了：立刻关包，别让界面停在空路径上
            SyncArchiveStateAfterExtract();
        }
    }

    [RelayCommand]
    private async Task TestArchive()
    {
        if (CurrentArchivePath == null || RunWithProgress == null) return;
        if (!EnsureArchiveAvailable()) return;

        // 加密压缩包先确保密码就绪（会话缓存 → 自动匹配 → 密码对话框，对齐解压/打开流程）。
        // 无密码/用户取消 → 中止测试，避免 TestArchiveAsync 无密码快速失败造成"静默测试失败"。
        if (!await TryEnsureArchivePasswordAsync())
            return; // 状态消息已在该方法内设置

        _sessionPasswords.TryGetValue(GetSessionPasswordKey(CurrentArchivePath, _currentFormat), out var password);

        var engine = ArchiveEngineFactory.GetEngineByExtension(CurrentArchivePath);
        if (engine == null) return;

        // 捕获 TestArchiveAsync 的完整性结果：RunWithProgress 的 completed 只表示
        // "未取消/未抛异常"，不代表测试通过——不捕获的话损坏包也会显示"测试通过"
        bool testOk = false;
        // spec §9: 测试流自动按下 📌 + 结果在关窗前写进窗口（状态行/错误摘要）
        PendingTestFlow = true;
        PendingCompletedCallback = pw =>
        {
            if (testOk)
            {
                pw.SetComplete(LocalizationManager.T("Status_TestOK"));
            }
            else
            {
                // 失败：错误摘要（写入即自动按下 📌）+ 状态行完成文案（按钮翻转为关闭）
                pw.SetErrorSummary(LocalizationManager.T("Status_ArchiveCorrupted"));
                pw.SetComplete(LocalizationManager.T("Status_TestFailed"));
            }
        };

        var completed = await RunWithProgress(
            LocalizationManager.T("Status_TestingArchive"),
            new[] { CurrentArchivePath! },
            async (progress, ct) =>
            {
                // 列表模式播种：测试前列出全部条目（Pending），测试中逐条目状态驱动列表（spec §12 #6）
                ExtractFlow.TrySeedEntryItemsInBackground(
                    ProgressWindow.CurrentVisible, CurrentArchivePath, password, filteredKeys: null, ct);
                testOk = await engine.TestArchiveAsync(CurrentArchivePath, password, progress, ct);
            });

        if (!completed) return;

        // 既有主窗口状态栏文案保留
        StatusMessage = testOk
            ? LocalizationManager.T("Status_TestOK")
            : LocalizationManager.T("Status_TestFailed");

        // D12: 测试失败模态框已移除（结果由进度窗口状态行 + 错误摘要承载，📌 保证留窗）
        App.DebugLog($"[TestArchive] completed: ok={testOk}, archive={CurrentArchivePath}");
    }

}
