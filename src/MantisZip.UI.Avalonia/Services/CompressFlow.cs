using Avalonia.Controls;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Services;
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
    /// 应用文件过滤（目录递归、逐文件匹配）；过滤后无文件时返回 null，由调用方决定提示/退出。
    /// </summary>
    public static CompressRequest? BuildRequest(CompressSettingsViewModel vm)
    {
        var sources = vm.FileFilter?.IsActive == true
            ? FileFilterHelper.ApplyFilter(vm.SelectedPaths.ToArray(), vm.FileFilter).ToList()
            : vm.SelectedPaths.ToList();
        if (sources.Count == 0)
            return null;

        var settings = AppSettings.Load();
        return new CompressRequest
        {
            SourcePaths = sources,
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
            KeepOriginalExtension = settings.KeepOriginalExtension,
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
        };
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
    /// 弹 CompressConflictDialog 并映射结果（无状态，供主窗口/CLI 的弹窗回调复用）。
    /// resolver 由 Core 在后台线程调用，本方法内部通过 Dispatcher 封送回 UI 线程弹窗。
    /// </summary>
    public static Task<(MantisZip.Core.Abstractions.CompressConflictAction Action, string? CustomName, bool ApplyToAll)>
        ShowConflictDialogAsync(Window owner, CompressConflictInfo info)
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dlg = new CompressConflictDialog(info.OutputPath, info.SuggestedName, info.CanAdd);
            await dlg.ShowDialog(owner);

            // "取消操作"按钮：终止整个压缩（对齐解压侧 Ask 弹窗"取消整个操作"抛
            // OperationCanceledException 的语义；Core 收到后取消剩余任务）
            if (dlg.CancelOperation)
                throw new OperationCanceledException("压缩被用户取消");

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

            return (resultAction, customName, dlg.ApplyToAll);
        });
    }
}
