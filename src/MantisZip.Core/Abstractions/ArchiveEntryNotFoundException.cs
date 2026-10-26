namespace MantisZip.Core.Abstractions;

/// <summary>
/// 删除压缩包条目时，目标路径在包内**一个条目都匹配不到**。
/// <para>
/// 派生自 <see cref="FileNotFoundException"/> 而不是另起一个 Exception 基类，有两个原因：
/// ① <see cref="ArchiveEngine.DeleteEntriesAsync"/> 早已声明 <c>FileNotFoundException</c> 契约，
/// 既有调用方只捕获基类也不会漏；
/// ② UI 的 <c>WholeArchiveErrorLocalizer</c> 用 <c>case FileNotFoundException</c> 兜住了所有
/// 「找不到文件」，把异常自带的「压缩包中不存在条目」消息整个丢弃。只有派生类型能让本地化层
/// 分辨「压缩包不存在」与「包内条目不存在」，给出各自的文案（该 case 必须排在基类 case 之前）。
/// </para>
/// <para>
/// 这个异常存在的意义就是**消灭静默成功**：此前删除目录一条都不命中时，copy-mode 快路径不校验，
/// 返回成功并上报 100%，用户看到「删除完成」而包内一字未动。
/// </para>
/// </summary>
public sealed class ArchiveEntryNotFoundException : FileNotFoundException
{
    /// <summary>初始化「条目不存在」异常。</summary>
    /// <param name="entryPath">用户请求删除、但包内不存在的条目路径（UI 直接显示给用户）</param>
    public ArchiveEntryNotFoundException(string entryPath)
        : base($"压缩包中不存在条目: {entryPath}", entryPath)
    {
        EntryPath = entryPath;
    }

    /// <summary>用户请求删除、但包内不存在的条目路径。</summary>
    public string EntryPath { get; }
}
