using System;
using System.Threading;
using System.Threading.Tasks;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// 元数据卡（卡片布局里「选中项显示格式元数据文字」）的取数编排。
///
/// <para>
/// <b>为什么单独一个 partial 文件</b>：这段逻辑只有一个触发点（选中项变化），但包含
/// 「取消上一轮 → 清旧卡文字 → 后台取数 → 回写模型」四步时序，塞进
/// <c>OnSelectedEntryChanged</c> 会让那个方法同时负责预览和卡片两件事。
/// </para>
/// <para>
/// <b>只在卡片布局有意义</b>：详情布局用 DataGrid，没有卡片；但这里不判断布局，
/// 因为 <see cref="ArchiveItemModel.MetadataCardLines"/> 只在卡片模板里被绑定，
/// 详情布局下取一次数只是浪费一次 I/O —— 由 <c>IsDetailsLayout</c> 短路更划算，见下。
/// </para>
/// </summary>
public partial class MainWindowViewModel
{
    private CancellationTokenSource? _metadataCardCts;

    /// <summary>当前元数据卡文字归属的条目。用它判断「异步返回时用户是不是已经改选」。</summary>
    private ArchiveItemModel? _metadataCardOwner;

    /// <summary>
    /// 选中项变化时刷新元数据卡：取消上一轮、清掉旧卡文字、按需发起新一轮取数。
    /// <para>由 <see cref="OnSelectedEntryChanged"/> 调用；容器改选/关包时 <paramref name="entry"/> 为 null。</para>
    /// </summary>
    private void RefreshMetadataCard(ArchiveItemModel? entry)
    {
        _metadataCardCts?.Cancel();
        _metadataCardCts?.Dispose();
        _metadataCardCts = null;

        // 改选/清空选中时，旧卡的文字必须立刻撤掉——否则用户会看到「上一个文件的元数据」
        // 挂在当前选中的卡片上（异步取数还没回来时尤其明显）。
        if (_metadataCardOwner != null && !ReferenceEquals(_metadataCardOwner, entry))
        {
            _metadataCardOwner.MetadataCardLines = null;
            _metadataCardOwner = null;
        }

        if (entry == null || entry.IsDirectory || CurrentArchivePath == null)
        {
            _metadataCardOwner = null;
            return;
        }

        // 详情布局没有卡片可绑，取数纯属浪费（点条目还会连带读一次条目）
        if (IsDetailsLayout)
            return;

        var cts = new CancellationTokenSource();
        _metadataCardCts = cts;
        _metadataCardOwner = entry;
        _ = LoadMetadataCardAsync(entry, cts.Token);
    }

    private async Task LoadMetadataCardAsync(ArchiveItemModel entry, CancellationToken ct)
    {
        try
        {
            var lines = await MetadataCardService.LoadLinesAsync(
                CurrentArchivePath!,
                entry,
                _currentFormat,
                _currentPassword,
                ct).ConfigureAwait(true);

            // 双重确认：解锁前先看取消标记，再看归属条目是否还是它（用户可能已经改选）
            if (ct.IsCancellationRequested || !ReferenceEquals(_metadataCardOwner, entry))
                return;

            entry.MetadataCardLines = lines.Count == 0 ? null : lines;
        }
        catch (OperationCanceledException)
        {
            // 改选导致的取消：正常流程，不记日志
        }
        catch (Exception ex)
        {
            App.DebugLog($"[META] LoadMetadataCardAsync failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
