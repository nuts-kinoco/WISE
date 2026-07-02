using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Application.Queries;

namespace WISE.Infrastructure.Data.Queries;

public class HistoryQueryService : IHistoryQueryService
{
    private readonly WiseDbContext _db;

    public HistoryQueryService(WiseDbContext db) => _db = db;

    public async Task<IReadOnlyList<HistoryEntryDto>> GetRecentHistoryAsync(int count, CancellationToken ct = default)
    {
        var logs = await _db.EventLogs
            .AsNoTracking()
            .Include(e => e.TargetWork)
            .OrderByDescending(e => e.OccurredAt)
            .Take(count)
            .ToListAsync(ct);

        return logs.Select(e => new HistoryEntryDto(
            e.Id,
            e.EventType,
            e.Actor,
            e.Source,
            e.Payload,
            e.TargetId,
            e.TargetWork?.PrimaryIdentifier,
            e.OccurredAt
        )).ToList();
    }
}
