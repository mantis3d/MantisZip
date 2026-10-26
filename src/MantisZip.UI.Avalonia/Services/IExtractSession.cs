using System.Diagnostics;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 解压执行段（唯一执行器）：把一份确认好的 <see cref="ExtractSettings"/> 真正跑完。
/// <para>
/// 为什么需要接口而不是直接调静态方法：入口（主窗口五处 + CLI）一律以构造依赖的形式持有它，
/// 测试则以 fake 实现断言「每个入口恰好调用一次 <see cref="RunAsync"/>」—— 这是入口矩阵
/// （漏接续）唯一可靠的守卫。会话本身不持有 <c>MainWindowViewModel</c>，避免又变成回读通道。
/// </para>
/// <para>
/// 与调用方的职责边界：执行器只负责「做事」（解析条目范围、密码、冲突决策、解压、进度、
/// 完成动作、历史记录），调用方只负责「显示结果」（状态栏文案、列表刷新、窗口生命周期）。
/// 执行器内部自带 try/catch，任何失败都以 <see cref="ExtractOutcome"/> 返回，不向调用方抛异常。
/// </para>
/// </summary>
public interface IExtractSession
{
    /// <summary>
    /// 执行一次解压。
    /// </summary>
    /// <param name="settings">确认时刻的不可变载荷（目标目录、冲突策略、条目范围、密码、完成动作等）。</param>
    /// <param name="context">宿主差异上下文（窗口所有者、进度呈现器、退出策略、显示用路径）。</param>
    /// <param name="cancellationToken">外部取消（宿主正在关闭时使用；进度窗口自身的取消令牌由呈现器负责）。</param>
    /// <returns>
    /// <see cref="ExtractOutcome.Completed"/>（正常跑完，个别包失败也算完成，行内已有失败标记）/
    /// <see cref="ExtractOutcome.Cancelled"/>（用户取消或窗口被关，调用方**不得**再弹失败提示）/
    /// <see cref="ExtractOutcome.Failed"/>（执行异常，异常已由呈现器展示，调用方不得重复弹窗）/
    /// <see cref="ExtractOutcome.NotStarted"/>（前置不成立，未进入执行段）。
    /// </returns>
    Task<ExtractOutcome> RunAsync(
        ExtractSettings settings,
        ExtractRunContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 解压完成后的宿主动作（打开目录 / 删包）。
/// <para>
/// 为什么单独抽出这个端口，而不是塞进 <see cref="IProgressPresenter"/>：呈现器契约管的是
/// 「窗口生命周期」（谁来建窗、谁来关窗、谁来展示错误），而「解压完了要不要叫 Shell 打开目录」
/// 是宿主能力，两者变更频率完全无关。也正因为是宿主能力，CLI 那种还没接线打开目录的宿主可以
/// 用 <see cref="SupportsOpenFolder"/> 直接表达「我这里没有这个开关」，而不必让执行器去查
/// 「我是不是 CLI」。
/// </para>
/// </summary>
public interface IExtractPostActions
{
    /// <summary>本宿主是否接入「完成后打开目录」（CLI 尚未接线，故默认实现返回 <c>true</c> 只适用于桌面宿主）。</summary>
    bool SupportsOpenFolder { get; }

    /// <summary>按开关状态删除（投放回收站）一个已解压成功的压缩包。</summary>
    void DeleteArchive(string archivePath, bool enabled);

    /// <summary>打开解压结果所在目录（智能定位由 <see cref="SmartOpenPathResolver"/> 决定）。</summary>
    Task OpenFolderAsync(string destinationPath, string archivePath, string? password);
}

/// <summary>
/// 桌面宿主的默认完成动作：删包走 <c>App.TryDeleteArchiveAfterExtract</c>（读设置 / 回收站 /
/// 三次重试），打开目录镜像原 <c>MainWindowViewModel.OpenExtractedFolderAsync</c> 的行为。
/// </summary>
public sealed class DefaultExtractPostActions : IExtractPostActions
{
    /// <inheritdoc />
    public bool SupportsOpenFolder => true;

    /// <inheritdoc />
    public void DeleteArchive(string archivePath, bool enabled)
        => App.TryDeleteArchiveAfterExtract(archivePath, enabled);

    /// <inheritdoc />
    public async Task OpenFolderAsync(string destinationPath, string archivePath, string? password)
    {
        try
        {
            // 智能定位：单根目录的包直接进那层目录，避免用户再多点一次
            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            var openPath = engine == null
                ? destinationPath
                : await SmartOpenPathResolver.ResolveSmartOpenPathAsync(archivePath, destinationPath, engine, password);

            if (!OperatingSystem.IsWindows()) return;

            var psi = new ProcessStartInfo { FileName = openPath, UseShellExecute = true };
            if (OperatingSystem.IsWindows())
                psi.Verb = "open";

            using var process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            // 打开目录失败不影响解压结果，只记诊断日志
            App.DebugLog($"[ExtractSession] open folder failed: {ex.Message}");
        }
    }
}
