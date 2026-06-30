using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Data.Repositories;

public class ReadingHistoryRepository : IReadingHistoryRepository
{
    private readonly WiseDbContext _db;

    public ReadingHistoryRepository(WiseDbContext db) => _db = db;

    public async Task<ReadingHistory?> GetAsync(Guid workId, string deviceId, CancellationToken ct = default)
        => await _db.ReadingHistories
            .FirstOrDefaultAsync(r => r.WorkId == workId && r.DeviceId == deviceId, ct);

    public async Task UpsertAsync(ReadingHistory history, CancellationToken ct = default)
    {
        var existing = await _db.ReadingHistories
            .FirstOrDefaultAsync(r => r.WorkId == history.WorkId && r.DeviceId == history.DeviceId, ct);

        if (existing == null)
            _db.ReadingHistories.Add(history);
        else
            existing.UpdateProgress(history.PageNumber, history.PositionSeconds, history.PositionPercent);

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid workId, string deviceId, CancellationToken ct = default)
    {
        var existing = await _db.ReadingHistories
            .FirstOrDefaultAsync(r => r.WorkId == workId && r.DeviceId == deviceId, ct);
        if (existing != null)
        {
            _db.ReadingHistories.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
    }
}
