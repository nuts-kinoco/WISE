using System;
using System.Collections.Concurrent;
using System.Threading;

namespace WISE.Application.Services;

public interface IJobCancellationService
{
    void RegisterJob(Guid jobId, CancellationTokenSource cts);
    void UnregisterJob(Guid jobId);
    bool CancelJob(Guid jobId);
}

public class JobCancellationService : IJobCancellationService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeJobs = new();

    public void RegisterJob(Guid jobId, CancellationTokenSource cts)
    {
        _activeJobs[jobId] = cts;
    }

    public void UnregisterJob(Guid jobId)
    {
        _activeJobs.TryRemove(jobId, out _);
    }

    public bool CancelJob(Guid jobId)
    {
        if (_activeJobs.TryGetValue(jobId, out var cts))
        {
            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
            }
            return true;
        }
        return false;
    }
}
