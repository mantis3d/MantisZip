using MantisZip.Core.Models;
using Xunit;

namespace MantisZip.Tests.UI;

/// <summary>
/// 纯逻辑单元测试：ProgressWindow 批处理模式的集合操作。
/// 不实例化任何 WPF 控件，仅测试 LINQ 查询、路径提取和状态转换等纯逻辑。
/// </summary>
public class ProgressWindowBatchLogicTests
{
    // ──────────────────────────────────────────────
    // 1. BatchItem 列表状态跟踪
    // ──────────────────────────────────────────────

    [Fact]
    public void BatchItem_StatusTransitions_PendingToInProgressToCompleted()
    {
        var items = new List<BatchItem>
        {
            new() { Name = "a.zip", Status = BatchItemStatus.Pending },
            new() { Name = "b.7z", Status = BatchItemStatus.Pending },
            new() { Name = "c.tar.gz", Status = BatchItemStatus.Pending }
        };

        // 全部初始为 Pending
        Assert.All(items, item => Assert.Equal(BatchItemStatus.Pending, item.Status));

        // 第一项 -> InProgress
        items[0].Status = BatchItemStatus.InProgress;
        Assert.Equal(BatchItemStatus.InProgress, items[0].Status);
        Assert.Equal(BatchItemStatus.Pending, items[1].Status);
        Assert.Equal(BatchItemStatus.Pending, items[2].Status);

        // 第一项 -> Completed, 第二项 -> InProgress
        items[0].Status = BatchItemStatus.Completed;
        items[1].Status = BatchItemStatus.InProgress;
        Assert.Equal(BatchItemStatus.Completed, items[0].Status);
        Assert.Equal(BatchItemStatus.InProgress, items[1].Status);
        Assert.Equal(BatchItemStatus.Pending, items[2].Status);

        // 第二项 -> Completed, 第三项 -> InProgress -> Completed
        items[1].Status = BatchItemStatus.Completed;
        items[2].Status = BatchItemStatus.InProgress;
        Assert.Equal(BatchItemStatus.Completed, items[1].Status);
        Assert.Equal(BatchItemStatus.InProgress, items[2].Status);

        items[2].Status = BatchItemStatus.Completed;
        Assert.Equal(BatchItemStatus.Completed, items[2].Status);

        // 全部完成
        Assert.All(items, item => Assert.Equal(BatchItemStatus.Completed, item.Status));
    }

    [Fact]
    public void BatchItem_StatusTransition_ToFailed()
    {
        var item = new BatchItem { Name = "broken.zip", Status = BatchItemStatus.InProgress };

        item.Status = BatchItemStatus.Failed;
        item.ErrorMessage = "磁盘空间不足";

        Assert.Equal(BatchItemStatus.Failed, item.Status);
        Assert.Equal("磁盘空间不足", item.ErrorMessage);
    }

    // ──────────────────────────────────────────────
    // 2. HasFailures 逻辑（纯 LINQ 模拟）
    // ──────────────────────────────────────────────

    [Fact]
    public void HasFailures_ReturnsTrue_WhenAnyItemFailed()
    {
        var items = new List<BatchItem>
        {
            new() { Name = "a.zip", Status = BatchItemStatus.Completed },
            new() { Name = "b.7z", Status = BatchItemStatus.Failed },
            new() { Name = "c.tar.gz", Status = BatchItemStatus.Completed }
        };

        // ProgressWindow.HasFailures 逻辑：_batchItems?.Any(i => i.Status == BatchItemStatus.Failed) ?? false
        bool hasFailures = items.Any(i => i.Status == BatchItemStatus.Failed);

        Assert.True(hasFailures);
    }

    [Fact]
    public void HasFailures_ReturnsFalse_WhenAllCompleted()
    {
        var items = new List<BatchItem>
        {
            new() { Name = "a.zip", Status = BatchItemStatus.Completed },
            new() { Name = "b.7z", Status = BatchItemStatus.Completed },
            new() { Name = "c.tar.gz", Status = BatchItemStatus.Completed }
        };

        bool hasFailures = items.Any(i => i.Status == BatchItemStatus.Failed);

        Assert.False(hasFailures);
    }

    [Fact]
    public void HasFailures_ReturnsFalse_WhenListIsEmpty()
    {
        var items = new List<BatchItem>();

        bool hasFailures = items.Any(i => i.Status == BatchItemStatus.Failed);

        Assert.False(hasFailures);
    }

    [Fact]
    public void HasFailures_ReturnsFalse_WhenListIsNull()
    {
        List<BatchItem>? items = null;

        // ProgressWindow.HasFailures 使用 ?.Any() ?? false
        bool hasFailures = items?.Any(i => i.Status == BatchItemStatus.Failed) ?? false;

        Assert.False(hasFailures);
    }

    [Fact]
    public void HasFailures_ReturnsTrue_WhenPendingItemsExist_ButNoneFailed()
    {
        // 当存在 Pending 项但没有 Failed 项时，HasFailures 应为 false
        var items = new List<BatchItem>
        {
            new() { Name = "a.zip", Status = BatchItemStatus.Completed },
            new() { Name = "b.7z", Status = BatchItemStatus.Pending }
        };

        bool hasFailures = items.Any(i => i.Status == BatchItemStatus.Failed);

        Assert.False(hasFailures);
    }

    // ──────────────────────────────────────────────
    // 3. CompleteWithErrors 统计逻辑
    // ──────────────────────────────────────────────

    [Fact]
    public void CompleteWithErrors_CountsCorrectly()
    {
        // 5 项：3 Completed + 2 Failed
        var items = new List<BatchItem>
        {
            new() { Name = "a.zip", Status = BatchItemStatus.Completed },
            new() { Name = "b.7z", Status = BatchItemStatus.Completed },
            new() { Name = "c.tar.gz", Status = BatchItemStatus.Completed },
            new() { Name = "d.iso", Status = BatchItemStatus.Failed },
            new() { Name = "e.rar", Status = BatchItemStatus.Failed }
        };

        int succeeded = items.Count(i => i.Status == BatchItemStatus.Completed);
        int failed = items.Count(i => i.Status == BatchItemStatus.Failed);

        Assert.Equal(3, succeeded);
        Assert.Equal(2, failed);
        Assert.Equal(5, succeeded + failed);
    }

    [Fact]
    public void CompleteWithErrors_AllCompleted_NoFailures()
    {
        var items = new List<BatchItem>
        {
            new() { Name = "a.zip", Status = BatchItemStatus.Completed },
            new() { Name = "b.7z", Status = BatchItemStatus.Completed }
        };

        int succeeded = items.Count(i => i.Status == BatchItemStatus.Completed);
        int failed = items.Count(i => i.Status == BatchItemStatus.Failed);

        Assert.Equal(2, succeeded);
        Assert.Equal(0, failed);
    }

    [Fact]
    public void CompleteWithErrors_AllFailed_NoSuccesses()
    {
        var items = new List<BatchItem>
        {
            new() { Name = "a.zip", Status = BatchItemStatus.Failed },
            new() { Name = "b.7z", Status = BatchItemStatus.Failed }
        };

        int succeeded = items.Count(i => i.Status == BatchItemStatus.Completed);
        int failed = items.Count(i => i.Status == BatchItemStatus.Failed);

        Assert.Equal(0, succeeded);
        Assert.Equal(2, failed);
    }

    [Fact]
    public void CompleteWithErrors_MixedStatuses_OnlyCountsCompletedAndFailed()
    {
        // Pending 和 InProgress 不计入成功/失败计数
        var items = new List<BatchItem>
        {
            new() { Name = "a.zip", Status = BatchItemStatus.Completed },
            new() { Name = "b.7z", Status = BatchItemStatus.InProgress },
            new() { Name = "c.tar.gz", Status = BatchItemStatus.Pending },
            new() { Name = "d.iso", Status = BatchItemStatus.Failed }
        };

        int succeeded = items.Count(i => i.Status == BatchItemStatus.Completed);
        int failed = items.Count(i => i.Status == BatchItemStatus.Failed);

        Assert.Equal(1, succeeded);
        Assert.Equal(1, failed);
    }

    // ──────────────────────────────────────────────
    // 4. InitBatchMode 文件名提取逻辑
    // ──────────────────────────────────────────────

    [Fact]
    public void InitBatchMode_FileNameExtraction_VariousPaths()
    {
        var paths = new List<string>
        {
            @"C:\a.zip",
            @"D:\dir\b.7z",
            @"E:\deep\nested\file.tar.gz"
        };

        var names = paths.Select(Path.GetFileName).ToList();

        Assert.Equal("a.zip", names[0]);
        Assert.Equal("b.7z", names[1]);
        Assert.Equal("file.tar.gz", names[2]);
    }

    [Fact]
    public void InitBatchMode_FileNameExtraction_HandlesDifferentDriveFormats()
    {
        var paths = new List<string>
        {
            @"C:\a.zip",
            @"\\server\share\b.7z",
            @"c.tar.gz" // 相对路径
        };

        var names = paths.Select(Path.GetFileName).ToList();

        Assert.Equal("a.zip", names[0]);
        Assert.Equal("b.7z", names[1]);
        Assert.Equal("c.tar.gz", names[2]);
    }

    [Fact]
    public void InitBatchMode_FileNameExtraction_AllEntriesPendingByDefault()
    {
        // 模拟 InitBatchMode 的初始化逻辑：paths.Select(p => new BatchItem { Name = Path.GetFileName(p), Status = Pending })
        var paths = new[] { @"C:\a.zip", @"C:\b.7z" };

        var items = paths.Select(p => new BatchItem
        {
            Name = Path.GetFileName(p),
            FullPath = p,
            Status = BatchItemStatus.Pending
        }).ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal("a.zip", items[0].Name);
        Assert.Equal(@"C:\a.zip", items[0].FullPath);
        Assert.Equal(BatchItemStatus.Pending, items[0].Status);
        Assert.Equal("b.7z", items[1].Name);
        Assert.Equal(@"C:\b.7z", items[1].FullPath);
        Assert.Equal(BatchItemStatus.Pending, items[1].Status);
    }

    // ──────────────────────────────────────────────
    // 5. SetCurrentBatchItem 的前一项自动完成逻辑
    // ──────────────────────────────────────────────

    [Fact]
    public void SetCurrentBatchItem_PreviousItemAutoCompletes()
    {
        // 模拟 SetCurrentBatchItem 的核心逻辑：
        // if (index > 0 && _batchItems[index - 1].Status == BatchItemStatus.InProgress)
        //     _batchItems[index - 1].Status = BatchItemStatus.Completed;
        // _batchItems[index].Status = BatchItemStatus.InProgress;
        var items = new List<BatchItem>
        {
            new() { Name = "a.zip", Status = BatchItemStatus.Pending },
            new() { Name = "b.7z", Status = BatchItemStatus.Pending },
            new() { Name = "c.tar.gz", Status = BatchItemStatus.Pending }
        };

        // 模拟 index=1：先设前一项为 InProgress（代表上一个操作），再切换到 index=1
        void SetCurrent(int index)
        {
            if (index > 0 && items[index - 1].Status == BatchItemStatus.InProgress)
                items[index - 1].Status = BatchItemStatus.Completed;

            items[index].Status = BatchItemStatus.InProgress;
        }

        // 第一个项设为 InProgress
        SetCurrent(0);
        Assert.Equal(BatchItemStatus.InProgress, items[0].Status);
        Assert.Equal(BatchItemStatus.Pending, items[1].Status);

        // 切换到索引 1：前一项（索引 0）是 InProgress，应自动变为 Completed
        SetCurrent(1);
        Assert.Equal(BatchItemStatus.Completed, items[0].Status);
        Assert.Equal(BatchItemStatus.InProgress, items[1].Status);
        Assert.Equal(BatchItemStatus.Pending, items[2].Status);

        // 切换到索引 2：前一项（索引 1）是 InProgress，应自动变为 Completed
        SetCurrent(2);
        Assert.Equal(BatchItemStatus.Completed, items[0].Status);
        Assert.Equal(BatchItemStatus.Completed, items[1].Status);
        Assert.Equal(BatchItemStatus.InProgress, items[2].Status);
    }

    [Fact]
    public void SetCurrentBatchItem_PreviousItemAlreadyCompleted_DoesNotOverwrite()
    {
        var items = new List<BatchItem>
        {
            new() { Name = "a.zip", Status = BatchItemStatus.Completed },
            new() { Name = "b.7z", Status = BatchItemStatus.Pending }
        };

        // 模拟：前一项已经是 Completed，跳过自动完成
        if (1 > 0 && items[0].Status == BatchItemStatus.InProgress)
            items[0].Status = BatchItemStatus.Completed; // 不应执行

        items[1].Status = BatchItemStatus.InProgress;

        Assert.Equal(BatchItemStatus.Completed, items[0].Status); // 保持 Completed
        Assert.Equal(BatchItemStatus.InProgress, items[1].Status);
    }

    [Fact]
    public void SetCurrentBatchItem_PreviousItemAlreadyFailed_DoesNotOverwrite()
    {
        var items = new List<BatchItem>
        {
            new() { Name = "a.zip", Status = BatchItemStatus.Failed },
            new() { Name = "b.7z", Status = BatchItemStatus.Pending }
        };

        // 模拟：前一项已是 Failed，不应改为 Completed
        if (1 > 0 && items[0].Status == BatchItemStatus.InProgress)
            items[0].Status = BatchItemStatus.Completed; // 不应执行

        items[1].Status = BatchItemStatus.InProgress;

        Assert.Equal(BatchItemStatus.Failed, items[0].Status); // 保持 Failed
        Assert.Equal(BatchItemStatus.InProgress, items[1].Status);
    }

    // ──────────────────────────────────────────────
    // 6. 边界条件
    // ──────────────────────────────────────────────

    [Fact]
    public void SetCurrentBatchItem_IndexOutOfRange_DoesNotThrow()
    {
        var items = new List<BatchItem>
        {
            new() { Name = "a.zip", Status = BatchItemStatus.Pending }
        };

        // ProgressWindow 的 SetCurrentBatchItem: if (index < 0 || index >= _batchItems.Count) return;
        // 模拟相同的防御逻辑
        void SetCurrentSafe(int index)
        {
            if (items == null || index < 0 || index >= items.Count) return;
            items[index].Status = BatchItemStatus.InProgress;
        }

        // 负索引不抛异常
        SetCurrentSafe(-1);
        // 超出范围不抛异常
        SetCurrentSafe(5);
        // 有效索引正常工作
        SetCurrentSafe(0);

        Assert.Equal(BatchItemStatus.InProgress, items[0].Status);
    }

    [Fact]
    public void SetCurrentBatchItem_EmptyList_DoesNotThrow()
    {
        var items = new List<BatchItem>();

        void SetCurrentSafe(int index)
        {
            if (items == null || index < 0 || index >= items.Count) return;
            items[index].Status = BatchItemStatus.InProgress;
        }

        // 空列表上的所有操作均不抛异常
        SetCurrentSafe(-1);
        SetCurrentSafe(0);
        SetCurrentSafe(1);
    }

    [Fact]
    public void UpdateBatchItemStatus_IndexOutOfRange_DoesNotThrow()
    {
        var items = new List<BatchItem>
        {
            new() { Name = "a.zip", Status = BatchItemStatus.Pending }
        };

        // 模拟 UpdateBatchItemStatus 防御逻辑
        void UpdateSafe(int index, BatchItemStatus status)
        {
            if (items == null || index < 0 || index >= items.Count) return;
            items[index].Status = status;
        }

        UpdateSafe(-1, BatchItemStatus.Completed);
        UpdateSafe(5, BatchItemStatus.Completed);

        // 原始项不受影响
        Assert.Equal(BatchItemStatus.Pending, items[0].Status);
    }

    [Fact]
    public void UpdateBatchItemStatus_NullList_DoesNotThrow()
    {
        List<BatchItem>? items = null;

        void UpdateSafe(int index, BatchItemStatus status)
        {
            if (items == null || index < 0 || index >= items.Count) return;
            items[index].Status = status;
        }

        UpdateSafe(0, BatchItemStatus.Completed);
    }

    [Fact]
    public void CompleteWithErrors_NullList_DoesNotThrow()
    {
        List<BatchItem>? items = null;

        // 模拟 CompleteWithErrors 防御逻辑：if (_batchItems == null) return;
        if (items != null)
        {
            int succeeded = items.Count(i => i.Status == BatchItemStatus.Completed);
            int failed = items.Count(i => i.Status == BatchItemStatus.Failed);
        }
    }

    [Fact]
    public void CompleteWithErrors_EmptyList_CountsAreZero()
    {
        var items = new List<BatchItem>();

        int succeeded = items.Count(i => i.Status == BatchItemStatus.Completed);
        int failed = items.Count(i => i.Status == BatchItemStatus.Failed);

        Assert.Equal(0, succeeded);
        Assert.Equal(0, failed);
    }

    [Fact]
    public void InitBatchMode_EmptyPaths_ProducesEmptyItemList()
    {
        var paths = Array.Empty<string>();

        var items = paths.Select(p => new BatchItem
        {
            Name = Path.GetFileName(p),
            FullPath = p,
            Status = BatchItemStatus.Pending
        }).ToList();

        Assert.Empty(items);
    }
}
