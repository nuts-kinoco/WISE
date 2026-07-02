using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Application.Queries;

namespace WISE.Infrastructure.Data.Queries;

public class SystemHistoryQueryService : ISystemHistoryQueryService
{
    private readonly WiseDbContext _db;

    public SystemHistoryQueryService(WiseDbContext db) => _db = db;

    public async Task<IReadOnlyList<SystemHistoryEntryDto>> GetHistoryAsync(int limit, CancellationToken ct = default)
        // 注: Select 射影のみで TargetWork.PrimaryIdentifier を取得できるため Include は不要
        // （P3監査 C-3: 射影と併用された Include は EF Core が無視するだけで無害だが誤解を招くため除去）
        => await _db.EventLogs
            .AsNoTracking()
            .OrderByDescending(e => e.OccurredAt)
            .Take(limit)
            .Select(e => new SystemHistoryEntryDto(
                e.Id,
                e.OccurredAt,
                e.EventType,
                e.TargetId,
                e.TargetWork != null ? e.TargetWork.PrimaryIdentifier : null,
                e.Payload))
            .ToListAsync(ct);

    public Task<int> GetHistoryCountAsync(CancellationToken ct = default)
        => _db.EventLogs.CountAsync(ct);
}
