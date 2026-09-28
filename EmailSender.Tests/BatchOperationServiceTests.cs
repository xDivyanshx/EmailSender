using EmailSender.Models;
using EmailSender.Services;

namespace EmailSender.Tests;

public sealed class BatchOperationServiceTests
{
    [Fact]
    public void TracksProgressAndCancellation()
    {
        BatchOperationService service = new();
        Assert.True(service.TryStart("original", 3));
        service.SetCurrent("jane@example.com");
        service.RecordResult(true, false, false);
        Assert.True(service.RequestCancellation());

        BatchOperationStatus status = service.GetStatus();
        Assert.True(status.Running);
        Assert.Equal(1, status.Processed);
        Assert.Equal(1, status.Sent);
        Assert.True(status.CancellationRequested);
        Assert.True(service.IsCancellationRequested());
        service.Complete();
        Assert.False(service.GetStatus().Running);
    }

    [Fact]
    public void RejectsConcurrentBatch()
    {
        BatchOperationService service = new();
        Assert.True(service.TryStart("original", 1));
        Assert.False(service.TryStart("follow-up", 1));
    }
}
