using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Application.Services;
using WISE.Domain.Entities;
using WISE.Domain.Enums;
using WISE.Infrastructure.Data;

namespace WISE.Api.UseCases;

/// <summary>
/// P1 リファクタリング Phase4: JobsController から WiseDbContext 直接参照を排除するための UseCase。
/// Job の作成（FetchMetadata系）・状態遷移（cancel/retry）・一括削除を担う。
/// </summary>
public class JobUseCase
{
    private readonly WiseDbContext _dbContext;
    private readonly IJobCancellationService _cancellationService;

    public JobUseCase(WiseDbContext dbContext, IJobCancellationService cancellationService)
    {
        _dbContext = dbContext;
        _cancellationService = cancellationService;
    }

    public async Task<(bool Found, Guid JobId)> EnqueueFetchMetadataAsync(Guid workId, CancellationToken ct = default)
    {
        var exists = await _dbContext.Works.AsNoTracking().AnyAsync(w => w.Id == workId, ct);
        if (!exists) return (false, Guid.Empty);

        var payload = JsonSerializer.Serialize(new { WorkId = workId });
        var job = new Job("FetchMetadata", $"Work_{workId}", payload);
        job.MarkAsQueued();
        _dbContext.Jobs.Add(job);
        await _dbContext.SaveChangesAsync(ct);

        return (true, job.Id);
    }

    public async Task<int> EnqueueFetchMetadataBatchAsync(IReadOnlyList<Guid> workIds, CancellationToken ct = default)
    {
        var existingIds = await _dbContext.Works
            .AsNoTracking()
            .Where(w => workIds.Contains(w.Id))
            .Select(w => w.Id)
            .ToListAsync(ct);

        int queued = 0;
        foreach (var workId in existingIds)
        {
            var payload = JsonSerializer.Serialize(new { WorkId = workId });
            var job = new Job("FetchMetadata", $"Work_{workId}", payload);
            job.MarkAsQueued();
            _dbContext.Jobs.Add(job);
            queued++;
        }
        await _dbContext.SaveChangesAsync(ct);
        return queued;
    }

    public enum CancelResult { NotFound, CancellationRequested, CanceledBeforeRunning, CannotCancel }

    public async Task<CancelResult> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var job = await _dbContext.Jobs.FindAsync(new object[] { id }, ct);
        if (job == null) return CancelResult.NotFound;

        if (job.Status == JobStatus.Running)
        {
            return _cancellationService.CancelJob(id) ? CancelResult.CancellationRequested : CancelResult.CannotCancel;
        }
        if (job.Status == JobStatus.Queued || job.Status == JobStatus.Created)
        {
            job.MarkAsCanceled();
            await _dbContext.SaveChangesAsync(ct);
            return CancelResult.CanceledBeforeRunning;
        }
        return CancelResult.CannotCancel;
    }

    public enum RetryResult { NotFound, InvalidState, Ok }

    public async Task<(RetryResult Result, Job? Job)> RetryAsync(Guid id, CancellationToken ct = default)
    {
        var job = await _dbContext.Jobs.FindAsync(new object[] { id }, ct);
        if (job == null) return (RetryResult.NotFound, null);
        if (job.Status != JobStatus.Failed && job.Status != JobStatus.Canceled) return (RetryResult.InvalidState, null);

        job.MarkAsQueued();
        await _dbContext.SaveChangesAsync(ct);
        return (RetryResult.Ok, job);
    }

    public async Task<int> ClearFinishedAsync(CancellationToken ct = default)
    {
        var finished = new[] { JobStatus.Completed, JobStatus.Failed, JobStatus.Canceled };
        var count = await _dbContext.Jobs.Where(j => finished.Contains(j.Status)).CountAsync(ct);
        await _dbContext.Jobs.Where(j => finished.Contains(j.Status)).ExecuteDeleteAsync(ct);
        return count;
    }
}
