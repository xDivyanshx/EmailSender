using EmailSender.Models;

namespace EmailSender.Services;

public sealed class BatchOperationService
{
    private readonly object _sync = new();
    private BatchOperationStatus _status = new(false, null, 0, 0, 0, 0, 0, null, false, null, null);

    public BatchOperationStatus GetStatus()
    {
        lock (_sync) return _status;
    }

    public bool TryStart(string operationType, int total)
    {
        lock (_sync)
        {
            if (_status.Running) return false;
            _status = new(true, operationType, total, 0, 0, 0, 0, null, false, DateTimeOffset.Now, null);
            return true;
        }
    }

    public void SetCurrent(string email)
    {
        lock (_sync) _status = _status with { CurrentEmail = email };
    }

    public void RecordResult(bool sent, bool failed, bool skipped)
    {
        lock (_sync) _status = _status with
        {
            Processed = _status.Processed + 1,
            Sent = _status.Sent + (sent ? 1 : 0),
            Failed = _status.Failed + (failed ? 1 : 0),
            Skipped = _status.Skipped + (skipped ? 1 : 0)
        };
    }

    public bool RequestCancellation()
    {
        lock (_sync)
        {
            if (!_status.Running) return false;
            _status = _status with { CancellationRequested = true };
            return true;
        }
    }

    public bool IsCancellationRequested()
    {
        lock (_sync) return _status.CancellationRequested;
    }

    public void Complete()
    {
        lock (_sync) _status = _status with { Running = false, CurrentEmail = null, CompletedAt = DateTimeOffset.Now };
    }
}
