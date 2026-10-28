using MantisZip.Core.Services;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// <see cref="AppSettings.UpdateCheckFrequency"/> 里持久化的字符串与
/// <see cref="UpdateCheckFrequency"/> 枚举之间的唯一映射点。
/// <para>
/// 为什么存字符串而不是枚举：设置文件是给人看的、也可能被手工编辑，
/// 存 <c>"Daily"</c> 比存 <c>2</c> 可读；同时避免将来枚举顺序调整把老设置读歪。
/// </para>
/// </summary>
public static class UpdateCheckFrequencyNames
{
    /// <summary>设置文件里允许的四个取值（顺序即设置界面下拉框的顺序）。</summary>
    public static readonly IReadOnlyList<UpdateCheckFrequency> Ordered = new[]
    {
        UpdateCheckFrequency.Never,
        UpdateCheckFrequency.EveryStartup,
        UpdateCheckFrequency.Daily,
        UpdateCheckFrequency.Weekly,
    };

    /// <summary>
    /// 把设置里的字符串解析成枚举；空值/无法识别时回落「每天」
    /// （宁可多查一次，也不要因为一个手改坏的值让功能静默消失）。
    /// </summary>
    public static UpdateCheckFrequency Parse(string? value) => value switch
    {
        nameof(UpdateCheckFrequency.Never) => UpdateCheckFrequency.Never,
        nameof(UpdateCheckFrequency.EveryStartup) => UpdateCheckFrequency.EveryStartup,
        nameof(UpdateCheckFrequency.Weekly) => UpdateCheckFrequency.Weekly,
        _ => UpdateCheckFrequency.Daily,
    };

    /// <summary>把枚举写回设置字符串（枚举名即存储值）。</summary>
    public static string ToSetting(UpdateCheckFrequency frequency) => frequency switch
    {
        UpdateCheckFrequency.Never => nameof(UpdateCheckFrequency.Never),
        UpdateCheckFrequency.EveryStartup => nameof(UpdateCheckFrequency.EveryStartup),
        UpdateCheckFrequency.Weekly => nameof(UpdateCheckFrequency.Weekly),
        _ => nameof(UpdateCheckFrequency.Daily),
    };

    /// <summary>下拉框选中项（0..3）对应的枚举值。</summary>
    public static UpdateCheckFrequency FromIndex(int index)
        => index >= 0 && index < Ordered.Count ? Ordered[index] : UpdateCheckFrequency.Daily;

    /// <summary>枚举值对应的下拉框选中项（找不到时回落「每天」的下标）。</summary>
    public static int ToIndex(UpdateCheckFrequency frequency)
    {
        var index = Ordered.ToList().IndexOf(frequency);
        return index >= 0 ? index : Ordered.ToList().IndexOf(UpdateCheckFrequency.Daily);
    }
}
