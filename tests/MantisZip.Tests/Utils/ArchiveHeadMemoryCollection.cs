using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// 禁用并行的测试集合：<see cref="ArchiveEntryExtractorTests"/> 的内存断言读取的是
/// **进程级** <c>GC.GetTotalAllocatedBytes(precise: true)</c>，若与其它集合并行，
/// 别的测试在统计窗口里的分配会把结果冲大（假失败）。
/// </summary>
[CollectionDefinition("ArchiveHeadMemory", DisableParallelization = true)]
public sealed class ArchiveHeadMemoryCollection;
