using System.Text.Json;

namespace MantisZip.Core.Utils;

/// <summary>
/// 仓库统一的 <see cref="JsonSerializerOptions"/> 入口。
/// </summary>
/// <remarks>
/// 所有「从 JSON 文本读回对象」的调用都必须显式传入本类的 <see cref="CaseInsensitive"/>，
/// 不要用 <c>JsonSerializer.Deserialize&lt;T&gt;(json)</c> 单参数重载，也不要临场
/// <c>new JsonSerializerOptions()</c>——两者的行为都等于「区分大小写」，会静默丢字段。
/// </remarks>
public static class JsonDefaults
{
    /// <summary>
    /// 宽容键名大小写的反序列化选项：JSON 键名与目标属性名按不区分大小写匹配。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么必须显式传</b>：<see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/> 默认为
    /// <c>false</c>（区分大小写），而本仓库的 JSON 键名习惯写全小写 / camelCase
    /// （<c>name</c>、<c>translator</c>、<c>lastOpened</c> …），目标类型的属性却是 PascalCase。
    /// 默认行为下反序列化<b>不会抛异常、也不会告警</b>——它「成功」返回一个实例，只是所有对不上的
    /// 属性全都停在类型默认值（字符串是 <c>""</c>、可空类型是 <c>null</c>）。
    /// 这种静默失败曾让设置窗口语言页的「翻译贡献者」一行为空，而构建、CI、测试全绿
    /// （2026-10-28 事故：<c>LocalizationManager</c> 读 <c>languages.json</c> 时用了单参数重载）。
    /// </para>
    /// <para>
    /// <b>为什么键名大小写要保持宽容</b>：JSON 是跨版本、跨工具的外部契约——磁盘上既有历史版本用
    /// camelCase 写出的文件，也有手写配置与第三方导出。把「键名风格」当成严格契约，只会让旧数据
    /// 静默变空；宽容匹配仅影响「键名 → 属性」的对应关系，不会让拼错的键名凭空生效，
    /// 也不影响写入端（序列化仍按属性名/命名策略输出）。
    /// </para>
    /// <para>
    /// <b>边界</b>：本选项只解决大小写，不解决「键名与属性名完全不同」（那需要
    /// <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/> 或自定义命名策略）。
    /// 同一实例可安全复用：<see cref="JsonSerializerOptions"/> 只读使用是线程安全的，首次调用后
    /// 元数据会被内部缓存，复用比每次 <c>new</c> 更快。
    /// </para>
    /// </remarks>
    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
