using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 一次指针按下（行点击）之后的选区状态。
/// </summary>
/// <param name="Primary">主选项（用于预览 / 状态栏等）——本次单击命中的行；空格点击为 <see langword="null"/>。</param>
/// <param name="Selection">新的完整选区（有序，按 <see cref="ThumbnailSelectionController.HandlePointerPressed"/> 的输入顺序）。</param>
/// <param name="Anchor">新的锚点（Shift 连选的起点）。</param>
public sealed record SelectionResult(
    ArchiveItemModel? Primary,
    IReadOnlyList<ArchiveItemModel> Selection,
    ArchiveItemModel? Anchor);

/// <summary>
/// 文件列表选区控制器：把「按下了某一行 + 修饰键状态」翻译成新的选区。
///
/// <para>
/// 纯函数、无副作用、不碰 UI（单一可测的实现），瓦片视图与详细信息视图共用同一套语义，
/// 避免两个布局各写一遍导致 Ctrl / Shift 行为不一致。
/// </para>
/// <para>
/// 语义（与资源管理器对齐）：
/// ① 无修饰键 → 单选该行（即使它已在多选里，也收缩为单选，简单可预期）；
/// ② Ctrl → 切换：在选区里则移除，不在则追加；
/// ③ Shift → 从锚点到该行的连续区间（锚点缺失或不在列表中 → 退化成单选）；
/// ④ 点空白（<c>pressedItem == null</c>）→ 选区保持不变，只把 <see cref="SelectionResult.Primary"/> 清空。
/// </para>
/// <para>
/// 所有比较都是引用相等：列表模型不是值对象，同名条目可能是不同实例。
/// </para>
/// </summary>
public static class ThumbnailSelectionController
{
    /// <summary>
    /// 计算按下某一行之后的选区。
    /// </summary>
    /// <param name="pressedItem">被按下的行；点空白处为 <see langword="null"/>。</param>
    /// <param name="ctrl">是否按住 Ctrl（切换选择）。</param>
    /// <param name="shift">是否按住 Shift（区间选择，优先于 Ctrl）。</param>
    /// <param name="orderedItems">当前列表的显示顺序，用于定位锚点与区间两端。</param>
    /// <param name="anchor">当前锚点（上次单选 / Shift 之后的起点）。</param>
    /// <param name="currentSelection">按下前的选区。</param>
    /// <returns>新选区、主选项与新锚点。</returns>
    public static SelectionResult HandlePointerPressed(
        ArchiveItemModel? pressedItem,
        bool ctrl,
        bool shift,
        IReadOnlyList<ArchiveItemModel> orderedItems,
        ArchiveItemModel? anchor,
        IReadOnlyList<ArchiveItemModel> currentSelection)
    {
        ArgumentNullException.ThrowIfNull(orderedItems);
        ArgumentNullException.ThrowIfNull(currentSelection);

        // ① 点空白：保持现选区，只清主选项（调用方通常据此收起预览、不滚动到任何行）。
        if (pressedItem is null)
            return new SelectionResult(null, currentSelection, anchor);

        // ② Shift：锚点 → 该行的连续区间。Anchor 保留旧锚点，后续 Shift 还能以同一锚点扩缩。
        if (shift)
        {
            var anchorIndex = IndexOfReference(orderedItems, anchor);
            var pressedIndex = IndexOfReference(orderedItems, pressedItem);

            // 锚点缺失 / 不在当前列表（换过文件夹、锚点行被过滤掉）→ 退化为单选。
            if (anchorIndex < 0 || pressedIndex < 0)
                return SingleSelect(pressedItem);

            var start = Math.Min(anchorIndex, pressedIndex);
            var end = Math.Max(anchorIndex, pressedIndex);

            var range = new List<ArchiveItemModel>(end - start + 1);
            for (var i = start; i <= end; i++)
                range.Add(orderedItems[i]);

            return new SelectionResult(pressedItem, range, anchor);
        }

        // ③ Ctrl：切换该行的选中状态。
        if (ctrl)
        {
            var existingIndex = IndexOfReference(currentSelection, pressedItem);

            if (existingIndex >= 0)
            {
                // 已选中 → 移除；主选项落到剩下的第一项（全删光则清空）。
                var remaining = new List<ArchiveItemModel>(currentSelection.Count - 1);
                for (var i = 0; i < currentSelection.Count; i++)
                {
                    if (i == existingIndex)
                        continue;

                    remaining.Add(currentSelection[i]);
                }

                return new SelectionResult(
                    remaining.Count > 0 ? remaining[0] : null,
                    remaining,
                    anchor);
            }

            // 未选中 → 追加到末尾，主选项取新追加的这一行。
            var selection = new List<ArchiveItemModel>(currentSelection.Count + 1);
            for (var i = 0; i < currentSelection.Count; i++)
                selection.Add(currentSelection[i]);
            selection.Add(pressedItem);

            return new SelectionResult(pressedItem, selection, anchor);
        }

        // ④ 无修饰键：单选。即使该行本来就在多选里，也收缩为单选（行为可预期）。
        return SingleSelect(pressedItem);
    }

    private static SelectionResult SingleSelect(ArchiveItemModel item) =>
        new(item, new[] { item }, item);

    /// <summary>
    /// 一次「点击卡片 / 复选框」之后要做的勾选变更（纯数据，由视图写回
    /// <see cref="ArchiveItemModel.IsChecked"/>；控制器自己不改模型）。
    /// </summary>
    /// <param name="Primary">主选项（预览 / 状态栏用）；点空白处为 <see langword="null"/>。</param>
    /// <param name="ToCheck">需要置为勾选的条目（Shift 连勾时可能多项）。</param>
    /// <param name="ToUncheck">需要取消勾选的条目（单张切换时最多一个）。</param>
    /// <param name="Anchor">新的锚点（Shift 连勾的起点）。</param>
    public sealed record CheckResult(
        ArchiveItemModel? Primary,
        IReadOnlyList<ArchiveItemModel> ToCheck,
        IReadOnlyList<ArchiveItemModel> ToUncheck,
        ArchiveItemModel? Anchor);

    /// <summary>
    /// 计算一次勾选点击（复选框模式）之后要做的变更。
    ///
    /// <para>
    /// 语义：Shift = 从锚点到该行的**连续区间全部勾上**（只加不减，与资源管理器 Shift 连选一致）；
    /// 无修饰键 / Ctrl = 切换该行（已勾则取消，未勾则勾上）。
    /// </para>
    /// <para>
    /// 平铺、内容行与详细信息三种布局共用本方法，Shift 连勾在卡片视图与表格视图里行为一致。
    /// </para>
    /// </summary>
    /// <param name="pressedItem">被点中的条目；点空白处为 <see langword="null"/>。</param>
    /// <param name="shift">是否按住 Shift（区间连勾）。</param>
    /// <param name="orderedItems">当前列表的显示顺序，用于定位锚点与区间两端。</param>
    /// <param name="anchor">当前锚点（上次勾选 / Shift 之后的起点）。</param>
    /// <returns>要置勾、要取消与新的锚点。</returns>
    public static CheckResult HandleCheckPointerPressed(
        ArchiveItemModel? pressedItem,
        bool shift,
        IReadOnlyList<ArchiveItemModel> orderedItems,
        ArchiveItemModel? anchor)
    {
        ArgumentNullException.ThrowIfNull(orderedItems);

        var none = Array.Empty<ArchiveItemModel>();

        // ① 点空白：不动勾选态，只清主选项。
        if (pressedItem is null)
            return new CheckResult(null, none, none, anchor);

        // ② Shift：锚点 → 该行的连续区间，把区间里没勾的都勾上（只加不减）。
        if (shift)
        {
            var anchorIndex = IndexOfReference(orderedItems, anchor);
            var pressedIndex = IndexOfReference(orderedItems, pressedItem);

            // 锚点缺失 / 不在当前列表（换过文件夹、锚点行被过滤掉）→ 退化为「只勾这一行」。
            if (anchorIndex < 0 || pressedIndex < 0)
                return new CheckResult(pressedItem, CheckIfNeeded(pressedItem), none, pressedItem);

            var start = Math.Min(anchorIndex, pressedIndex);
            var end = Math.Max(anchorIndex, pressedIndex);

            var range = new List<ArchiveItemModel>(end - start + 1);
            for (var i = start; i <= end; i++)
            {
                if (!orderedItems[i].IsChecked)
                    range.Add(orderedItems[i]);
            }

            // 锚点保持不变：连续 Shift 点击始终以第一次点击为起点扩缩（与选区一致）。
            return new CheckResult(pressedItem, range, none, anchor);
        }

        // ③ 无修饰键 / Ctrl：切换该行。
        return pressedItem.IsChecked
            ? new CheckResult(pressedItem, none, new[] { pressedItem }, pressedItem)
            : new CheckResult(pressedItem, CheckIfNeeded(pressedItem), none, pressedItem);
    }

    private static IReadOnlyList<ArchiveItemModel> CheckIfNeeded(ArchiveItemModel item) =>
        item.IsChecked ? Array.Empty<ArchiveItemModel>() : new[] { item };

    private static int IndexOfReference(IReadOnlyList<ArchiveItemModel>? items, ArchiveItemModel? item)
    {
        if (items is null || item is null)
            return -1;

        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], item))
                return i;
        }

        return -1;
    }
}
