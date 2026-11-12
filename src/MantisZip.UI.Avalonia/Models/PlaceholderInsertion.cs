namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 占位符插入的纯函数：把 <paramref name="token"/> 插到光标处（有选区时替换选区）。
/// 抽成静态方法是为了能在无头测试里覆盖边界（空文本 / 光标越界 / 负索引 / 有选区），
/// 视图层只负责从 <c>TextBox</c> 读出三个值再写回。
/// </summary>
public static class PlaceholderInsertion
{
    /// <summary>
    /// 在 <paramref name="text"/> 的 <paramref name="selectionStart"/>..<paramref name="selectionEnd"/>
    /// 之间插入 <paramref name="token"/>，返回新文本与插入后的光标位置。
    /// </summary>
    /// <param name="text">当前文本（null 视为空串）。</param>
    /// <param name="selectionStart">选区起点（也是无选区时的插入点）。</param>
    /// <param name="selectionEnd">选区终点；小于起点时与起点交换。</param>
    /// <param name="token">要插入的占位符，如 <c>{name}</c>。</param>
    /// <param name="caretIndex">插入后光标应落的位置。</param>
    /// <returns>插入后的完整文本。</returns>
    public static string Insert(
        string? text,
        int selectionStart,
        int selectionEnd,
        string token,
        out int caretIndex)
    {
        var source = text ?? string.Empty;
        var start = Math.Clamp(selectionStart, 0, source.Length);
        var end = Math.Clamp(selectionEnd, 0, source.Length);
        if (start > end) (start, end) = (end, start);

        var inserted = token ?? string.Empty;
        var result = string.Concat(source.AsSpan(0, start), inserted, source.AsSpan(end));
        caretIndex = start + inserted.Length;
        return result;
    }

    /// <summary>
    /// 计算插入点：光标有效就用光标，否则退到文本末尾。
    /// <para>
    /// 用「从未聚焦过」的输入框点插入按钮时，<c>CaretIndex</c> 恒为 0，
    /// 直接按 0 插会插到最前面——那不是用户期望的「接着写」。调用方把上次
    /// 聚焦时的光标位置传进来（没有则传 -1），这里统一兜底。
    /// </para>
    /// </summary>
    /// <param name="text">当前文本。</param>
    /// <param name="caretIndex">光标位置；无效传负数。</param>
    /// <returns>应当使用的插入点。</returns>
    public static int ResolveInsertIndex(string? text, int caretIndex)
    {
        var length = (text ?? string.Empty).Length;
        return caretIndex < 0 || caretIndex > length ? length : caretIndex;
    }
}
