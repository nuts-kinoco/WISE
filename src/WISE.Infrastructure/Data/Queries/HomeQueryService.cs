using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Application.Queries;
using WISE.Domain.Entities;

namespace WISE.Infrastructure.Data.Queries;

public class HomeQueryService : IHomeQueryService
{
    private readonly WiseDbContext _db;

    public HomeQueryService(WiseDbContext db) => _db = db;

    public async Task<List<Work>> GetContinueWatchingAsync(string? deviceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return new List<Work>();

        var histories = await _db.ReadingHistories
            .AsNoTracking()
            .Where(rh => rh.DeviceId == deviceId
                      && (rh.PositionPercent == null || rh.PositionPercent < 0.95f))
            .OrderByDescending(rh => rh.LastReadAt)
            .Take(8)
            .ToListAsync(ct);

        if (histories.Count == 0) return new List<Work>();

        var workIds = histories.Select(rh => rh.WorkId).ToList();
        var works = await _db.Works
            .AsNoTracking()
            .Include(w => w.MetadataFields)
            .Include(w => w.Assets)
            .Where(w => workIds.Contains(w.Id))
            .ToListAsync(ct);

        return workIds
            .Select(id => works.FirstOrDefault(w => w.Id == id))
            .Where(w => w != null)
            .Select(w => w!)
            .ToList();
    }

    public Task<List<Work>> GetRecentlyAddedAsync(CancellationToken ct = default)
        => _db.Works
            .AsNoTracking()
            .Include(w => w.MetadataFields)
            .Include(w => w.Assets)
            .OrderByDescending(w => w.CreatedAt)
            .Take(12)
            .ToListAsync(ct);

    public Task<List<Work>> GetFavoritesAsync(CancellationToken ct = default)
        => _db.Works
            .AsNoTracking()
            .Include(w => w.MetadataFields)
            .Include(w => w.Assets)
            .Where(w => w.Favorite)
            .OrderBy(_ => EF.Functions.Random())
            .Take(8)
            .ToListAsync(ct);

    public Task<Work?> GetRandomAsync(CancellationToken ct = default)
        => _db.Works
            .AsNoTracking()
            .Include(w => w.MetadataFields)
            .Include(w => w.Assets)
            .OrderBy(_ => EF.Functions.Random())
            .FirstOrDefaultAsync(ct);
}
