namespace MantisZip.Core.Services;

/// <summary>
/// 一次「有没有新版本」检查的结局。
/// <para>
/// 为什么要三态：早先 <see cref="UpdateService.CheckForUpdateAsync"/> 把「已是最新」「HTTP 非 2xx」
/// 「网络异常 / 超时」「响应解析失败」全部塌缩成 <c>null</c>，导致「关于 → 更新」在断网时
/// 显示「已是最新版本」——**在最该报警的时候给了最误导的答案**。
/// 现在至少把「无更新」与「检查失败」分开，调用方才能给出正确提示。
/// </para>
/// </summary>
public enum UpdateCheckStatus
{
    /// <summary>检查成功，且当前已是最新版本（或暂无正式发布）。</summary>
    UpToDate,

    /// <summary>检查成功，且线上存在比当前更新的正式版本。</summary>
    UpdateAvailable,

    /// <summary>检查没成功：网络不可达 / 超时 / HTTP 错误 / 响应格式异常。原因见 <see cref="UpdateCheckResult.Reason"/>。</summary>
    Failed,
}

/// <summary>
/// <see cref="UpdateService.CheckForUpdateAsync"/> 的返回值：三态 + 载荷 + 失败原因。
/// <para>
/// 不变量（由静态工厂保证，构造器私有）：
/// <list type="bullet">
/// <item><see cref="UpdateCheckStatus.UpdateAvailable"/> ⇒ <see cref="Update"/> 非 <c>null</c>；</item>
/// <item><see cref="UpdateCheckStatus.Failed"/> ⇒ <see cref="Reason"/> 非空（造不出「失败没原因」）；</item>
/// <item><see cref="UpdateCheckStatus.UpToDate"/> ⇒ 两者都为空。</item>
/// </list>
/// </para>
/// </summary>
public readonly record struct UpdateCheckResult
{
    private UpdateCheckResult(UpdateCheckStatus status, UpdateInfo? update, string reason)
    {
        Status = status;
        Update = update;
        Reason = reason;
    }

    /// <summary>本次检查的结局。</summary>
    public UpdateCheckStatus Status { get; }

    /// <summary>发现的新版本信息；仅 <see cref="UpdateCheckStatus.UpdateAvailable"/> 时非 <c>null</c>。</summary>
    public UpdateInfo? Update { get; }

    /// <summary>失败原因（面向诊断日志，不是给用户看的最终文案）；非失败状态为空串。</summary>
    public string Reason { get; }

    /// <summary>已是最新版本。</summary>
    public bool IsUpToDate => Status == UpdateCheckStatus.UpToDate;

    /// <summary>发现了更新的正式版本。</summary>
    public bool IsUpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable;

    /// <summary>检查失败（网络 / 超时 / HTTP / 响应格式）。</summary>
    public bool IsFailed => Status == UpdateCheckStatus.Failed;

    /// <summary>构造「已是最新」结局。</summary>
    public static UpdateCheckResult UpToDate() => new(UpdateCheckStatus.UpToDate, null, string.Empty);

    /// <summary>构造「发现新版本」结局。</summary>
    public static UpdateCheckResult Available(UpdateInfo update)
        => new(UpdateCheckStatus.UpdateAvailable, update, string.Empty);

    /// <summary>
    /// 构造「检查失败」结局。
    /// <para>
    /// 原因必须非空：空原因会让「为什么没查到更新」重新变成不可诊断的黑箱，
    /// 因此这里直接抛 <see cref="ArgumentException"/>（与 UI 层 <c>OutcomeReasonGuard</c> 同一取舍）。
    /// </para>
    /// </summary>
    public static UpdateCheckResult Failed(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("检查失败必须给出原因", nameof(reason));

        return new UpdateCheckResult(UpdateCheckStatus.Failed, null, reason);
    }
}
