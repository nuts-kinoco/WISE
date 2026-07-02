using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Application.Queries;
using WISE.Domain.Entities;
using WISE.Domain.Enums;

namespace WISE.Infrastructure.Data.Queries;

public class JobsQueryService : IJobsQueryService
{
    private readonly WiseDbContext _db;

    public JobsQueryService(WiseDbContext db) => _db = db;

    public async Task<IReadOnlyList<Job>> GetRecentAsync(int take, CancellationToken ct = default)
        => await _db.Jobs
            .AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ActiveJobDto>> GetActiveAsync(CancellationToken ct = default)
    {
        var activeStatuses = new[] { JobStatus.Created, JobStatus.Queued, JobStatus.Running };

        var jobs = await _db.Jobs
            .AsNoTracking()
            .Where(j => activeStatuses.Contains(j.Status))
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(ct);

        // Target = "Work_{guid}" → Work の PrimaryIdentifier を解決
        var workIds = jobs
            .Where(j => j.Target != null && j.Target.StartsWith("Work_"))
            .Select(j =>
            {
                Guid.TryParse(j.Target!["Work_".Length..], out var gid);
                return gid;
            })
            .Where(g => g != Guid.Empty)
            .Distinct()
            .ToList();

        var identifiers = await _db.Works
            .AsNoTracking()
            .Where(w => workIds.Contains(w.Id))
            .Select(w => new { w.Id, w.PrimaryIdentifier })
            .ToDictionaryAsync(w => w.Id, w => w.PrimaryIdentifier, ct);

        return jobs.Select(j =>
        {
            string? identifier = null;
            if (j.Target != null && j.Target.StartsWith("Work_") &&
                Guid.TryParse(j.Target["Work_".Length..], out var gid))
                identifiers.TryGetValue(gid, out identifier);

            return new ActiveJobDto(
                j.Id, j.JobType, j.Status.ToString(), j.Target, identifier,
                j.CreatedAt, j.StartedAt, j.ErrorMessage);
        }).ToList();
    }

    public Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
}
