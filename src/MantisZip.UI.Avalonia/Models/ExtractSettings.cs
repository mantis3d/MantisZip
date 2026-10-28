using MantisZip.Core.Abstractions;
using MantisZip.Core.Services;

namespace MantisZip.UI.Avalonia.Models;

/// <summary>
/// 条目范围：确认那一刻「这次解压到底要落哪些条目」的三种互斥形态。
/// <para>
/// 为什么需要一个独立枚举而不是让几个可空字段各自为政：执行器必须能在一处判明范围。
/// 现状里「过滤命中集合」（<c>FilteredEntryKeys</c>）与「勾选条目」分属两条不同的执行链，
/// 谁优先没有任何地方定义；两者同时被填上时，结果取决于调用顺序而不是用户意图。
/// </para>
/// </summary>
public enum ExtractEntryScope
{
    /// <summary>整包解压（未启用过滤、也没有勾选条目）。</summary>
    WholeArchive,

    /// <summary>只解压过滤命中的条目（<see cref="ExtractSettings.FilteredEntryKeys"/> 非空）。</summary>
    FilteredEntries,

    /// <summary>只解压用户在预览树里勾选的条目（<see cref="ExtractSettings.SelectedEntries"/> 非空）。</summary>
    ExplicitEntries,
}

/// <summary>
/// 密码取得策略：确认那一刻已经知道多少密码，以及允许执行器自己再去找多少。
/// <para>
/// 为什么这条策略要进载荷而不是藏在执行器里：「要不要去密码库碰运气」是入口之间真实存在的
/// 行为差异 —— <c>--extract</c> 批处理会先拿密码库匹配（命中就不弹密码框），而主窗口设置
/// 对话框只复用对话框内已经校验过的密码。把它写进载荷，执行器就不必认识「我这次是从哪个入口
/// 来的」；写成执行器里的隐藏开关，则同一份设置在不同入口下会跑出不同结果。
/// </para>
/// </summary>
public enum ExtractPasswordPolicy
{
    /// <summary>只用载荷里已有的密码（主窗口设置对话框路径），不打密码库。</summary>
    UseProvidedOnly,

    /// <summary>载荷里没有可用密码时回退到密码库匹配（CLI 批处理路径）。</summary>
    FallbackToPasswordLibrary,
}

/// <summary>
/// 解压执行载荷：用户点下「开始解压」那一刻，其所决定的一切的**不可变快照**。
/// 执行器只读它，不再回读任何窗口、ViewModel 或宿主状态。
/// <para>
/// 为什么需要这个类型（意图-执行接缝的「结果即载荷」法则）：原先确认流程是「关窗 + 调用方补读」——
/// 主窗口在 <c>ShowExtractSettingsDialog</c> 关窗后逐个手抄 ViewModel 属性、再从窗口补读
/// <c>FilteredEntryKeys</c>，CLI 在 <c>Closed</c> 事件之后补读目标目录/冲突策略/密码/过滤结果。
/// 补读漏一项就是静默失效（历史事故：CLI 多包的过滤集合恒为 null，过滤条件静默不生效）。
/// 改成「确认事务一次产出快照」后，字段缺失会在产出点就暴露，而不是在执行段变成「这次解压好像
/// 没听我的话」。
/// </para>
/// <para>
/// 三条边界（改这个类型前必须先读）：
/// <list type="number">
/// <item><b>不许宿主依赖</b>：不得出现 <c>Window</c> / 对话框 / Presenter / <see cref="ExitPolicy"/> /
/// 取消令牌等宿主侧对象。宿主差异归 <see cref="ExtractRunContext"/>，执行器再把它叠上来。</item>
/// <item><b>不许可变回传通道</b>：确认之后没有任何人回写本类型。它 <c>init</c>-only 且集合
/// （<c>IReadOnlyList</c> / <c>IReadOnlyDictionary</c>）在构造时必须已是独立副本 —— 曾经的
/// <c>ExtractSettingsViewModel.FilteredEntryKeys</c> 就是个可写属性，确认后由调用方补赋值，
/// 于是「值为 null」同时表示「没启用过滤」和「忘了赋值」。</item>
/// <item><b>不许 UI 概念</b>：不出现「当前选中行」「预览树是否展开」「按钮可用性」这类瞬时界面态；
/// 用户意图以业务参数表达（覆盖策略、目标目录、条目范围），界面态留在 ViewModel。</item>
/// </list>
/// </para>
/// </summary>
public sealed record ExtractSettings
{
    /// <summary>本批要解压的压缩包绝对路径（按批处理行顺序；提取语义绑定首个压缩包）。</summary>
    public required IReadOnlyList<string> ArchivePaths { get; init; }

    /// <summary>解压目标目录（用户确认后的最终值，不再经 <c>ExtractPathResolver</c> 之外的规则改写）。</summary>
    public required string DestinationPath { get; init; }

    /// <summary>同名文件冲突策略（<c>ask</c> / <c>overwrite</c> / <c>overwrite-if-older</c> / <c>overwrite-if-smaller</c> / <c>rename</c> / <c>skip</c>）。</summary>
    public string ConflictAction { get; init; } = "ask";

    /// <summary>
    /// 过滤命中后需要实际解压的条目 key 列表（<c>null</c> = 未启用过滤，整包解压）。
    /// <para>
    /// 为什么它必须在载荷里：这是历史上最容易掉的一项 —— 它由 View 的过滤编辑器计算、
    /// 曾经由调用方在确认之后单独赋值，CLI 多包路径因此恒为 <c>null</c>、过滤静默失效。
    /// 现在它在事务内产出，值为 <c>null</c> 只表示「用户没启用过滤」。
    /// </para>
    /// </summary>
    public IReadOnlyList<string>? FilteredEntryKeys { get; init; }

    /// <summary>预览树里勾选的条目（<c>null</c> = 没有勾选，解整包或按过滤集合）。</summary>
    public IReadOnlyList<ArchiveItem>? SelectedEntries { get; init; }

    /// <summary>
    /// 路径裁剪锚点：当前浏览的压缩包内路径（<c>ExtractPathResolver</c> 契约的输入之一，
    /// 与预览树共用同一口径）。仅选中条目解压需要；整包解压可留 <c>null</c>。
    /// <para>
    /// 为什么它是执行参数而不是 UI 状态：它决定「解压出来的文件放在目标目录的哪一层」，
    /// 与预览树显示的结果必须一致；漏传会让预览与实际落盘不一致（历史踩坑：设置对话框漏传
    /// <c>currentFolder</c> 导致预览树与实际解压路径不一致）。它记录的是路径契约，不是「当前点了哪一行」。
    /// </para>
    /// </summary>
    public string? CurrentFolder { get; init; }

    /// <summary>调用方已确定的首包密码（会话缓存 / 命令行），仅对首个压缩包生效。</summary>
    public string? Password { get; init; }

    /// <summary>对话框内已校验/手动解锁的密码（压缩包路径 → 明文密码）。优先于 <see cref="Password"/>。</summary>
    public IReadOnlyDictionary<string, string>? MatchedPasswords { get; init; }

    /// <summary>密码取得策略（见 <see cref="ExtractPasswordPolicy"/>）。</summary>
    public ExtractPasswordPolicy PasswordPolicy { get; init; } = ExtractPasswordPolicy.UseProvidedOnly;

    /// <summary>完成后是否在文件管理器里打开目标目录（窗口上的开关最终值）。</summary>
    public bool OpenFolderAfterExtract { get; init; }

    /// <summary>完成后是否删除压缩包（窗口上的开关最终值，回收站语义由执行器侧决定）。</summary>
    public bool DeleteArchiveAfterExtract { get; init; }

    /// <summary>
    /// 解压时是否保留完整路径（<c>null</c> = 交由设置/引擎决定）。
    /// <para>
    /// 记录用字段：路径归属的唯一事实来源是 <c>ExtractPathResolver</c>，执行器不允许二次解释
    /// 这个值；它在此处是为了让「用户当时选了什么」在载荷里可追溯（T11 接入「按完整路径解压」
    /// 入口时由该入口置位）。
    /// </para>
    /// </summary>
    public bool? PreserveFullPath { get; init; }

    /// <summary>
    /// 本次解压的并行度快照（对话框里改的值本次生效、不回写全局设置）。
    /// <c>null</c> = 按全局设置 / 自动解析（CLI 路径）。
    /// </summary>
    public ParallelExtractSettings? ParallelOverride { get; init; }

    /// <summary>
    /// 由已有字段推导的条目范围，供执行器与调用方以同一口径判决。
    /// <para>
    /// 注意 <see cref="ExtractEntryScope.FilteredEntries"/> 的判定用「非 null」而不是「非空」：
    /// 过滤启用但零命中时载荷带回的是**空集合**，语义是「有意什么都不解压」，
    /// 绝不能因为集合为空就退回整包解压（「预览 = 实际」的边界保证）。
    /// </para>
    /// </summary>
    public ExtractEntryScope EntryScope =>
        SelectedEntries is { Count: > 0 } ? ExtractEntryScope.ExplicitEntries
        : FilteredEntryKeys is not null ? ExtractEntryScope.FilteredEntries
        : ExtractEntryScope.WholeArchive;
}
