using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Services;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Dialogs;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 压缩流程公共逻辑（主窗口与 CLI 右键菜单共用，消除两套实现漂移）。
///
/// 历史背景：WPF 时代主窗口（code-behind）与 CLI（App）各自实现了一套压缩流程，
/// Avalonia 移植时继承了这个分裂，导致 CLI 侧曾出现「硬编码 Manual 模式」「不处理文件冲突」
/// 等与主窗口行为不一致的 bug。本类将可共享部分抽取为单一事实来源：
/// - <see cref="BuildRequest"/>：CompressSettingsViewModel → CompressRequest（含文件过滤）
/// - <see cref="CreateResolver"/>：冲突处理 resolver（弹窗回调注入 + ApplyToAll 记忆）
/// - <see cref="ShowConflictDialogAsync"/>：CompressConflictDialog 弹窗 + 结果映射（无状态）
/// </summary>
public static class CompressFlow
{
    /// <summary>
    /// 从 CompressSettingsViewModel 构建 CompressRequest。
    /// 消费预览构建时缓存的 B 数据集（CompressPlan）：源路径保持目录粒度，过滤由每项 IncludedFiles
    /// 白名单表达，输出路径取自 B —— 执行侧不再重新计算路径、不再重新过滤，预览 = 实际。
    /// B 为空（无源/路径无效/构建未完成）或过滤激活且全部无匹配时返回 null，由调用方决定提示/退出。
    /// </summary>
    public static CompressRequest? BuildRequest(CompressSettingsViewModel vm)
    {
        var plan = vm.GetPlanForExecution();
        if (plan == null || plan.Items.Count == 0)
            return null;

        // 过滤激活时（B 中任何一项带 IncludedFiles）丢弃无匹配项的源；全部无匹配 → null
        var items = plan.Items;
        bool filterActive = items.Any(i => i.IncludedFiles != null);
        if (filterActive)
        {
            items = items.Where(i => i.IncludedFiles is { Count: > 0 }).ToList();
            if (items.Count == 0)
                return null;
        }

        var settings = AppSettings.Load();
        return new CompressRequest
        {
            // 目录粒度源（过滤语义在白名单中表达，不再扁平化为文件列表）
            SourcePaths = items.Select(i => i.SourcePath).ToList(),
            // B 数据集原样传入：CompressService 逐项消费 OutputArchivePath + IncludedFiles
            Plan = new CompressPlan(plan.Mode, plan.OutputPath, items),
            Mode = vm.OutputMode,
            Format = vm.DefaultFormat,
            CompressionLevel = vm.CompressionLevel,
            Password = vm.GetActivePassword(),
            Encrypt = vm.Encrypt,
            Comment = vm.Comment,
            CommentDistribution = vm.CommentDistribution,
            OutputPath = vm.OutputMode switch
            {
                CompressOutputMode.Manual => vm.OutputPath,
                CompressOutputMode.Separate => null,
                CompressOutputMode.Combined => vm.OutputPath,
                _ => null,
            },
            SplitSize = vm.SplitSize,
            PreserveDirectoryRoot = settings.PreserveDirectoryRoot,
            // 从对话框 ViewModel 读取（对话框可能已修改，不再经 AppSettings 中转）
            KeepOriginalExtension = vm.KeepOriginalExtension,
            // 高级格式选项从对话框 ViewModel 读取（仅本次压缩生效），不再从 AppSettings 中转
            FileNameEncoding = vm.FileNameEncoding,
            ZipCompressionMethod = vm.ZipCompressionMethod,
            ZipEncryptionMethod = vm.ZipEncryptionMethod,
            SevenZipCompressionMethod = vm.SevenZipCompressionMethod,
            SevenZipSolid = vm.SevenZipSolid,
            SevenZipSolidBlockSize = vm.SevenZipSolidBlockSize,
            SevenZipDictionarySize = vm.SevenZipDictionarySize,
            SevenZipNumFastBytes = vm.SevenZipNumFastBytes,
            SevenZipMatchFinder = vm.SevenZipMatchFinder,
            SevenZipEncryptHeaders = vm.SevenZipEncryptHeaders,
            // 源文件读取错误（被占用等）→ 弹 ErrorDialog（重试/跳过/中止），补上 Avalonia 迁移时遗漏的接线
            ErrorResolver = CreateErrorResolver(),
        };
    }

    /// <summary>
    /// 创建源文件读取错误 resolver（文件被占用等）。
    /// 引擎在后台线程调用（重试耗尽后），本方法将其封送回 UI 线程弹 <see cref="ErrorDialog"/>
    /// 让用户选择 重试/跳过/中止，并统一「ApplyToAll 记忆」逻辑（对齐 WPF App.CreateCompressOptions）。
    /// 返回 Abort 时引擎重抛异常使整个压缩失败；Skip 跳过该文件继续；Retry 重新尝试读取。
    /// </summary>
    public static Func<FileErrorInfo, FileErrorAction> CreateErrorResolver()
    {
        bool applyToAll = false;
        FileErrorAction? chosenAction = null;

        return info =>
        {
            // 已勾选"应用到全部" → 直接返回记忆的选择
            if (applyToAll && chosenAction.HasValue)
                return chosenAction.Value;

            // 封送回 UI 线程弹窗并阻塞等待用户选择（错误回调在后台线程，无 UI 死锁风险）
            var result = Dispatcher.UIThread.InvokeAsync(() =>
            {
                var dlg = new ErrorDialog(info);
                dlg.ShowDialog(ResolveOwnerWindow()).GetAwaiter().GetResult();
                return (Action: dlg.ResultAction, All: dlg.ApplyToAll);
            }).Result;

            if (result.All)
            {
                applyToAll = true;
                chosenAction = result.Action;
            }

            return result.Action;
        };
    }

    /// <summary>
    /// 解析错误/冲突对话框的 Owner 窗口：优先当前活跃窗口，否则回退到主窗口。
    /// 避免弹窗出现在主窗口之后。
    /// </summary>
    private static Window? ResolveOwnerWindow()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.Windows
                    .OfType<Window>()
                    .FirstOrDefault(w => w.IsActive)
                    ?? desktop.MainWindow;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 创建压缩冲突处理 resolver。
    /// 弹窗由 <paramref name="showDialog"/> 回调完成（主窗口走 VM 回调、CLI 直接弹窗），
    /// 本方法统一「ApplyToAll 记忆 + 结果包装」逻辑。
    /// </summary>
    /// <param name="showDialog">
    /// 弹窗回调：传入冲突信息，返回用户选择（Action、重命名名、是否应用到全部）。
    /// 返回 Cancel 且取消整个操作时抛 OperationCanceledException 以终止压缩。
    /// </param>
    public static CompressConflictResolver CreateResolver(
        Func<CompressConflictInfo, Task<(MantisZip.Core.Abstractions.CompressConflictAction Action, string? CustomName, bool ApplyToAll)>> showDialog)
    {
        bool applyToAll = false;
        MantisZip.Core.Abstractions.CompressConflictAction? chosenAction = null;

        return async info =>
        {
            // 已勾选"应用到全部" → 直接返回记忆的选择
            if (applyToAll && chosenAction.HasValue)
                return new CompressConflictResolution(chosenAction.Value, null);

            var (action, customName, applyAll) = await showDialog(info);
            if (applyAll)
            {
                applyToAll = true;
                chosenAction = action;
            }

            return action switch
            {
                MantisZip.Core.Abstractions.CompressConflictAction.Cancel
                    => new CompressConflictResolution(MantisZip.Core.Abstractions.CompressConflictAction.Cancel, null),
                _ => new CompressConflictResolution(action, customName),
            };
        };
    }

    /// <summary>
    /// 弹 CompressConflictDialog 并映射结果（供主窗口/CLI 的弹窗回调复用）。
    /// resolver 由 Core 在后台线程调用，本方法内部通过 Dispatcher 封送回 UI 线程弹窗。
    /// 循环重入：用户点击"暂停"时收起冲突对话框并进入进度窗口暂停态，
    /// 恢复后重新弹窗处理同一个冲突（对齐 WPF AppPartials/App.Compress.cs 的实现）。
    /// </summary>
    public static async Task<(MantisZip.Core.Abstractions.CompressConflictAction Action, string? CustomName, bool ApplyToAll)>
        ShowConflictDialogAsync(Window owner, CompressConflictInfo info)
    {
        // 循环重入：暂停后恢复时重新弹窗（对齐 WPF AppPartials/App.Compress.cs CompressConflictResolver）
        while (true)
        {
            var result = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlg = new CompressConflictDialog(info.OutputPath, info.SuggestedName, info.CanAdd);
                await dlg.ShowDialog(owner);

                // 暂停：收起对话框，返回暂停标志由外层处理
                if (dlg.IsPaused)
                {
                    return (Action: MantisZip.Core.Abstractions.CompressConflictAction.Cancel, CustomName: (string?)null, IsPaused: true, IsCancelled: false, ApplyAll: false);
                }

                // "取消操作"按钮：终止整个压缩（对齐解压侧 Ask 弹窗"取消整个操作"抛
                // OperationCanceledException 的语义；Core 收到后取消剩余任务）
                if (dlg.CancelOperation)
                {
                    return (Action: MantisZip.Core.Abstractions.CompressConflictAction.Cancel, CustomName: (string?)null, IsPaused: false, IsCancelled: true, ApplyAll: false);
                }

                MantisZip.Core.Abstractions.CompressConflictAction resultAction;
                string? customName = null;
                switch (dlg.ResultAction)
                {
                    case Dialogs.CompressConflictAction.Overwrite:
                        resultAction = MantisZip.Core.Abstractions.CompressConflictAction.Overwrite;
                        break;
                    case Dialogs.CompressConflictAction.Add:
                        resultAction = MantisZip.Core.Abstractions.CompressConflictAction.Add;
                        break;
                    case Dialogs.CompressConflictAction.Rename:
                        resultAction = MantisZip.Core.Abstractions.CompressConflictAction.Rename;
                        customName = dlg.CustomName;
                        break;
                    case Dialogs.CompressConflictAction.Skip:
                    case Dialogs.CompressConflictAction.Cancel:
                    default:
                        resultAction = MantisZip.Core.Abstractions.CompressConflictAction.Cancel;
                        break;
                }

                return (Action: resultAction, CustomName: customName, IsPaused: false, IsCancelled: false, ApplyAll: dlg.ApplyToAll);
            });

            if (result.IsCancelled)
                throw new OperationCanceledException("压缩被用户取消");

            if (result.IsPaused)
            {
                // 从 owner（CLI 直接传 ProgressWindow）或当前打开的进度窗口中找到目标，
                // 在 UI 线程调用 PauseFromConflict 进入暂停态，然后在后台线程等待暂停事件
                // （不阻塞 UI 线程，用户可在进度窗口点击"继续"恢复）。
                var pw = owner as ProgressWindow ?? ProgressWindow.CurrentVisible;
                if (pw != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => pw.PauseFromConflict());
                    pw.PauseEvent.Wait(pw.CancellationToken);
                }
                continue; // 恢复后重新弹窗
            }

            return (result.Action, result.CustomName, result.ApplyAll);
        }
    }

    /// <summary>
    /// 压缩成功后把输出目录写入路径历史（<see cref="PathHistoryManager"/>，
    /// QuickPathPicker 地址栏 / CustomFilePickerDialog 历史建议的数据源）。
    /// 目录取自 GetOutputPaths 各输出文件的父目录并去重（Manual/Combined = 输出文件所在目录；
    /// Separate = 各源条目所在目录）。失败仅记日志，不影响压缩结果。
    /// 由调用方在压缩确认成功后调用（主窗口 ExecuteCompressFromSettings / CLI CompressWithProgress）。
    /// </summary>
    public static void RecordOutputHistory(CompressRequest request)
    {
        try
        {
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var output in AvaloniaCompressService.GetOutputPaths(request))
            {
                var dir = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(dir))
                    dirs.Add(dir);
            }
            foreach (var dir in dirs)
                PathHistoryManager.Record(dir);
        }
        catch (Exception ex)
        {
            App.DebugLog($"[CompressFlow] RecordOutputHistory failed: {ex.Message}");
        }
    }
}
