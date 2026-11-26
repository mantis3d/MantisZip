using System;
using System.IO;
using MantisZip.Core.Abstractions;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 解压「到底写出了几个文件」的旁路统计。
/// <para>
/// 背景（issue #91）：引擎对**单个条目**失败只计入 <see cref="ExtractResult"/> / 逐条目
/// <see cref="ArchiveEntryStatus.Failed"/> 事件，**不抛异常**。调用方若只看 Task 是否抛错，
/// 就会出现「进度窗口显示解压完成、目标目录却一个文件都没有」的静默失败——
/// 最典型的是 WinZip AES 密码编码不匹配（密码错被逐条目吞掉，最终 0 文件仍报成功）。
/// </para>
/// <para>
/// 三个解压叶子（全量 <see cref="ExtractService"/>、过滤 <see cref="ExtractFlow"/>、
/// 选中条目 <see cref="SelectedItemsExtractService"/>）都要经过同一套判定，
/// 故把统计放在进度事件旁路：包裹调用方传入的 progress，同步转发、不改变上报线程语义，
/// 只顺手数一下 Completed / Overwritten / Failed / Skipped。
/// </para>
/// </summary>
internal sealed class ExtractOutcomeTracker : IProgress<ArchiveProgress>
{
    private readonly IProgress<ArchiveProgress>? _inner;

    public ExtractOutcomeTracker(IProgress<ArchiveProgress>? inner) => _inner = inner;

    /// <summary>成功写出的条目数（Completed + Overwritten）。</summary>
    public int WrittenCount { get; private set; }

    /// <summary>写入失败的条目数。</summary>
    public int FailedCount { get; private set; }

    /// <summary>因冲突策略跳过的条目数。</summary>
    public int SkippedCount { get; private set; }

    public void Report(ArchiveProgress value)
    {
        switch (value.EntryStatus)
        {
            case ArchiveEntryStatus.Completed:
            case ArchiveEntryStatus.Overwritten:
                WrittenCount++;
                break;
            case ArchiveEntryStatus.Failed:
                FailedCount++;
                break;
            case ArchiveEntryStatus.Skipped:
                SkippedCount++;
                break;
        }

        _inner?.Report(value);
    }

    /// <summary>
    /// 清零计数。密码重试循环每轮都会重新调用引擎，上一轮的结果不能带进下一轮判定：
    /// 否则「上一轮写出了几个文件、本轮一个都没写出来」会被旧计数掩盖成成功。
    /// </summary>
    public void Reset()
    {
        WrittenCount = 0;
        FailedCount = 0;
        SkippedCount = 0;
    }

    /// <summary>
    /// 判据：一条都没写出来、却有失败条目 ⇒ 本次解压实质失败，必须抛错让调用方走失败收尾，
    /// 绝不能让「0 文件」落进成功路径（否则进度窗口显示完成、CLI 还会记录路径历史甚至删源包）。
    /// <para>
    /// 注意「全部被跳过」（用户选了跳过冲突策略）不算失败：那是用户的明确选择，静默通过是正确的。
    /// </para>
    /// </summary>
    /// <param name="fallbackFailedEntries">进度事件缺失时的兜底失败数（如引擎返回的 ExtractResult.FailedEntries）。</param>
    public void ThrowIfNothingWritten(int fallbackFailedEntries = 0)
    {
        var failed = Math.Max(FailedCount, fallbackFailedEntries);
        if (WrittenCount > 0 || failed <= 0)
            return;

        throw new InvalidDataException(
            LocalizationManager.T("Error_Extract_AllEntriesFailed", failed));
    }
}
