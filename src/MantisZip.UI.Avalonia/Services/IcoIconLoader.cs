using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 后台把文件列表里 <c>.ico</c> 条目的**自身图标**解出来，替换掉先显示的通用图标。
///
/// <para>
/// 为什么要有这个东西：列表首帧必须立刻可见，不能为了读 ICO 去等解压。
/// 所以列表重建时先照旧用 <see cref="IconService.GetFileIcon"/> 填通用图标（瞬时），
/// 再由本类在后台线程逐个读包内字节、解码、回到 UI 线程覆盖 <see cref="ArchiveItemModel.IconSource"/>。
/// </para>
/// <para>
/// 每次列表重建都会取消上一批加载（见 <c>MainWindowViewModel.Navigation.cs</c> 的 <c>_iconLoadCts</c>），
/// 因此筛选/翻目录时不会有多批加载同时抢同一压缩包的读取句柄。
/// </para>
/// </summary>
internal static class IcoIconLoader
{
    /// <summary>单次列表重建最多回填多少个 .ico 图标（超出的条目保留通用图标）。</summary>
    public const int MaxIcons = 20;

    /// <summary>单个 .ico 允许读取的最大字节数：ICO 帧实际都很小，2 MB 足够，同时兜住异常巨大的条目。</summary>
    private const long MaxIcoBytes = 2L * 1024 * 1024;

    /// <summary>ICO 扩展名（含点，比较时不区分大小写）。</summary>
    private const string IcoExtension = ".ico";

    /// <summary>
    /// 为列表中的 <c>.ico</c> 条目异步加载其自身嵌入的图标。
    /// </summary>
    /// <param name="models">本次列表重建产出的模型快照（调用方应传 `CurrentEntries.ToList()`，避免遍历可变集合）。</param>
    /// <param name="archivePath">源压缩包路径。</param>
    /// <param name="format">压缩包格式（决定由哪个引擎读取条目）。</param>
    /// <param name="password">压缩包密码（无密码传 <see langword="null"/>）。</param>
    /// <param name="maxIcons">最多处理多少个条目（典型值 <see cref="MaxIcons"/>）。</param>
    /// <param name="ct">取消令牌：列表重建/关包时会取消，此时剩余条目直接放弃。</param>
    public static async Task LoadAsync(
        IReadOnlyList<ArchiveItemModel> models,
        string archivePath,
        ArchiveFormat format,
        string? password,
        int maxIcons,
        CancellationToken ct)
    {
        if (models.Count == 0 || maxIcons <= 0 || string.IsNullOrEmpty(archivePath))
            return;

        // 挑候选：只碰传入的快照，不遍历 UI 集合；筛选在主线程做，代价可忽略
        var candidates = new List<ArchiveItemModel>();
        foreach (var model in models)
        {
            if (candidates.Count >= maxIcons)
                break;

            if (model.IsDirectory)
                continue;
            if (!string.Equals(Path.GetExtension(model.Name), IcoExtension, StringComparison.OrdinalIgnoreCase))
                continue;
            // 超过读取上限的条目不可能是正常 ICO，先按元数据跳过，省掉一次注定被截断的读取
            if (model.Size > MaxIcoBytes)
                continue;

            candidates.Add(model);
        }

        if (candidates.Count == 0)
            return;

        try
        {
            // 整个循环扔到后台线程：读包内条目是同步重活，绝不能在 UI 线程上跑
            await Task.Run(async () =>
            {
                foreach (var model in candidates)
                {
                    if (ct.IsCancellationRequested)
                        return;

                    try
                    {
                        var raw = await ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
                            archivePath,
                            model.FullPath,
                            format,
                            MaxIcoBytes,
                            password,
                            ct).ConfigureAwait(false);

                        var icon = IcoIconHelper.ExtractIcon(raw);
                        if (icon == null)
                            continue;

                        if (ct.IsCancellationRequested)
                        {
                            // 列表已经重建/关包：这张位图没人要了，立刻释放，不留给 GC 慢慢收
                            icon.Dispose();
                            return;
                        }

                        // IconSource 的变更通知要回 UI 线程：列表模板在 UI 线程上监听 PropertyChanged
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (ct.IsCancellationRequested)
                            {
                                icon.Dispose();
                                return;
                            }

                            model.IconSource = icon;
                        }, DispatcherPriority.Background);
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常路径（重建列表 / 关包）：静默放弃剩余条目
                        return;
                    }
                    catch (Exception ex)
                    {
                        // 单条坏数据（加密、损坏、超限、引擎不支持）只是这一项没有专属图标，
                        // 不能影响列表，也不能中断后面的条目
                        Log($"IcoIconLoader: 跳过 {model.Name}（{ex.GetType().Name}: {ex.Message}）");
                    }
                }
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 同上：取消是预期行为，不记日志
        }
        catch (Exception ex)
        {
            Log($"IcoIconLoader: 加载中断（{ex.GetType().Name}: {ex.Message}）");
        }
    }

    /// <summary>
    /// 记一行诊断日志。日志自身绝不能再抛出（本类跑在后台任务里，抛出会变成静默的未观察异常）。
    /// </summary>
    private static void Log(string message)
    {
        try
        {
            App.DebugLog(message);
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine(message);
        }
    }
}
