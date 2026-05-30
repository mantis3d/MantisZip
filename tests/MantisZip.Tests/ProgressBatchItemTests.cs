using MantisZip.Core.Models;
using Xunit;

namespace MantisZip.Tests.Models;

public class ProgressBatchItemTests
{
    [Fact]
    public void DefaultStatus_IsPending()
    {
        var item = new BatchItem();
        Assert.Equal(BatchItemStatus.Pending, item.Status);
    }

    [Fact]
    public void StatusChange_FiresPropertyChanged()
    {
        var item = new BatchItem();
        var fired = false;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BatchItem.Status))
                fired = true;
        };

        item.Status = BatchItemStatus.InProgress;

        Assert.True(fired);
    }

    [Fact]
    public void StatusChange_SameValue_DoesNotFirePropertyChanged()
    {
        var item = new BatchItem();
        var fireCount = 0;
        item.PropertyChanged += (_, _) => fireCount++;

        item.Status = BatchItemStatus.Pending; // same as default

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void ErrorMessage_Null_DoesNotThrow()
    {
        var item = new BatchItem();
        item.ErrorMessage = null;
        item.Status = BatchItemStatus.Failed;

        Assert.Null(item.ErrorMessage);
        Assert.Equal(BatchItemStatus.Failed, item.Status);
    }

    [Fact]
    public void StatusTransition_AllValues()
    {
        var item = new BatchItem();

        item.Status = BatchItemStatus.InProgress;
        Assert.Equal(BatchItemStatus.InProgress, item.Status);

        item.Status = BatchItemStatus.Completed;
        Assert.Equal(BatchItemStatus.Completed, item.Status);

        item.Status = BatchItemStatus.Failed;
        Assert.Equal(BatchItemStatus.Failed, item.Status);
    }
}
