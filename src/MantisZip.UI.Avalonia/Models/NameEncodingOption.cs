using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 主工具栏「文件名代码页」下拉项。
/// <para>
/// <see cref="Key"/> 是编码键（取值见 <see cref="NameEncodingCatalog"/>，如 <c>auto</c> / <c>system</c> /
/// <c>gbk</c> / <c>other:950</c>）；<see cref="Display"/> 是显示文案 —— 编码本名（UTF-8 / GBK / Big5 …）
/// 属语言本族原生名，不翻译；「自动检测」「系统默认」「其他…」走本地化。
/// </para>
/// </summary>
public sealed class NameEncodingOption
{
    /// <summary>编码键（交给 <see cref="NameEncodingCatalog.TryResolve"/> 解析）。</summary>
    public required string Key { get; init; }

    /// <summary>下拉里显示的文案。</summary>
    public required string Display { get; init; }

    /// <summary>
    /// 构造默认选项列表：自动检测 / 系统默认 / 六个常用编码 / 其他…
    /// 每次语言切换后都要重建（见 <c>MainWindowViewModel.CultureChangedForNameEncoding</c>）。
    /// </summary>
    public static IReadOnlyList<NameEncodingOption> BuildDefault()
    {
        var options = new List<NameEncodingOption>
        {
            new() { Key = NameEncodingCatalog.Auto, Display = LocalizationManager.T("Toolbar_NameEncoding_Auto") },
            new() { Key = NameEncodingCatalog.System, Display = LocalizationManager.T("Toolbar_NameEncoding_System") },
        };

        // 常用编码：按「中文圈最常见 → 其他东亚」排序，与 Bandizip 的默认清单一致
        foreach (var (key, label) in CommonEncodings)
            options.Add(new NameEncodingOption { Key = key, Display = label });

        options.Add(new() { Key = OtherPlaceholder, Display = LocalizationManager.T("Toolbar_NameEncoding_Other") });
        return options;
    }

    /// <summary>
    /// 「其他…」的占位键：选中后要先弹框问代码页数字，再由 VM 换成真正的 <c>other:&lt;n&gt;</c>。
    /// 它不是一个可解析的编码键，所以放在这里单独命名。
    /// </summary>
    public const string OtherPlaceholder = "other";

    /// <summary>
    /// 工具栏图标徽章用的「短标记」：徽章只有 22×22 图标那么大，写全名会溢出，
    /// 所以只放 2–4 个字符（GBK / BIG5 / JIS / UTF8 / U16 …），完整名称交给 Tooltip。
    /// <para>
    /// 返回空串表示「不显示徽章」：未打开压缩包、或自动检测还没有结果（<c>null</c> / <c>auto</c>）。
    /// </para>
    /// </summary>
    /// <param name="key">编码键（<see cref="NameEncodingCatalog"/> 认可的取值）。</param>
    public static string ShortLabel(string? key)
    {
        if (string.IsNullOrEmpty(key) || NameEncodingCatalog.IsAuto(key))
            return string.Empty;

        if (string.Equals(key, NameEncodingCatalog.System, StringComparison.OrdinalIgnoreCase))
            return "ANSI";

        // 「其他代码页…」直接显示码页数字（950 / 932 …），比显示 "other:950" 直观
        if (key.StartsWith(NameEncodingCatalog.OtherPrefix, StringComparison.OrdinalIgnoreCase))
            return key[NameEncodingCatalog.OtherPrefix.Length..];

        return key.ToLowerInvariant() switch
        {
            "utf-8" => "UTF8",
            "utf-16" => "U16",
            "gbk" => "GBK",
            "gb18030" => "GB18",
            "big5" => "BIG5",
            "shift-jis" => "JIS",
            "euc-kr" => "KR",
            _ => key.ToUpperInvariant(),
        };
    }

    /// <summary>常用编码清单（键, 原生名）。</summary>
    private static readonly (string Key, string Label)[] CommonEncodings =
    [
        ("utf-8", "UTF-8"),
        ("gbk", "GBK"),
        ("gb18030", "GB18030"),
        ("big5", "Big5"),
        ("shift-jis", "Shift-JIS"),
        ("euc-kr", "EUC-KR"),
    ];
}
