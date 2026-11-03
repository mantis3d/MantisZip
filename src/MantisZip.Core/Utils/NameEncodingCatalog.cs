using System.Globalization;
using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// 归档**文件名**编码目录（Core 侧单一事实源）。
///
/// 键值约定（字符串键在 UI 设置、会话态与引擎之间传递，避免各层各自解析）：
/// <c>auto</c>（自动检测，缺省）/ <c>utf-8</c> / <c>gbk</c> / <c>gb18030</c> / <c>big5</c> /
/// <c>shift-jis</c> / <c>euc-kr</c> / <c>system</c>（当前系统 ANSI 代码页）/ <c>other:&lt;代码页&gt;</c>。
///
/// 与「文本内容编码」（<see cref="TextEncodingDetector"/> / <c>AppSettings.TextEncodingPreference</c>）无关：
/// 那一套解码的是文件内容，本目录解码的是 ZIP/TAR 条目名。
/// </summary>
public static class NameEncodingCatalog
{
    /// <summary>自动检测（引擎内部的 bit 11 → CJK 启发式 → GBK 重试流程）。</summary>
    public const string Auto = "auto";

    /// <summary>当前系统 ANSI 代码页（<c>CultureInfo.CurrentCulture.TextInfo.ANSICodePage</c>）。</summary>
    public const string System = "system";

    /// <summary>任意代码页前缀，形如 <c>other:950</c>。</summary>
    public const string OtherPrefix = "other:";

    private static readonly (string Key, int CodePage)[] KnownEncodings =
    [
        ("utf-8", 65001),
        ("gbk", 936),
        ("gb18030", 54936),
        ("big5", 950),
        ("shift-jis", 932),
        ("euc-kr", 949),
    ];

    /// <summary>固定编码键（不含 <see cref="Auto"/>、<see cref="System"/> 与 <c>other:</c>），供 UI 生成下拉项。</summary>
    public static IReadOnlyList<string> FixedKeys { get; } = KnownEncodings.Select(x => x.Key).ToArray();

    static NameEncodingCatalog()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// 是否为「自动检测」。null / 空串 / 空白 / <c>auto</c> 都算自动。
    /// </summary>
    public static bool IsAuto(string? key)
        => string.IsNullOrWhiteSpace(key)
           || key.Trim().Equals(Auto, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 解析编码键。返回 false 表示键无法识别（未知名称或非法代码页数字），调用方应退回自动检测，
    /// 不要静默使用别的编码 —— 猜错编码会写坏文件名。
    /// </summary>
    public static bool TryResolve(string? key, out Encoding? encoding)
    {
        encoding = null;
        if (IsAuto(key)) return false;

        var trimmed = key!.Trim();
        if (trimmed.Equals(System, StringComparison.OrdinalIgnoreCase))
        {
            encoding = ResolveSystemEncoding();
            return encoding is not null;
        }

        if (trimmed.StartsWith(OtherPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var digits = trimmed[OtherPrefix.Length..].Trim();
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var codePage))
                return false;
            return TryGetByCodePage(codePage, out encoding);
        }

        foreach (var (knownKey, codePage) in KnownEncodings)
        {
            if (!trimmed.Equals(knownKey, StringComparison.OrdinalIgnoreCase)) continue;
            return TryGetByCodePage(codePage, out encoding);
        }

        // 兜底：允许直接写 .NET 编码名（如 "utf-32"），方便 CLI 与诊断脚本使用
        try
        {
            encoding = Encoding.GetEncoding(trimmed);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>当前系统 ANSI 代码页编码；取不到（如 CultureInfo 异常）时回退 UTF-8。</summary>
    public static Encoding ResolveSystemEncoding()
    {
        try
        {
            var codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            if (codePage > 0) return Encoding.GetEncoding(codePage);
        }
        catch (ArgumentException)
        {
            // 系统 ANSI 代码页不可用（罕见），退回 UTF-8
        }
        return Encoding.UTF8;
    }

    /// <summary>
    /// 把实际使用的编码转回键，供「自动检测 · 检测到 X」回显。未知代码页返回 <c>other:&lt;代码页&gt;</c>。
    /// </summary>
    public static string Describe(Encoding? encoding)
    {
        if (encoding is null) return Auto;
        var codePage = encoding.CodePage;

        foreach (var (knownKey, knownCodePage) in KnownEncodings)
        {
            if (knownCodePage == codePage) return knownKey;
        }

        if (codePage == ResolveSystemEncoding().CodePage) return System;
        return $"{OtherPrefix}{codePage}";
    }

    private static bool TryGetByCodePage(int codePage, out Encoding? encoding)
    {
        encoding = null;
        if (codePage <= 0) return false;
        try
        {
            encoding = Encoding.GetEncoding(codePage);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
