using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Data.Repositories;

public class CoverCacheRepository : ICoverCacheRepository
{
    private readonly WiseDbContext _db;

    public CoverCacheRepository(WiseDbContext db) => _db = db;

    public async Task<CoverCache?> GetAsync(Guid workId, string? providerName = null, CancellationToken ct = default)
    {
        var query = _db.CoverCaches.Where(c => c.WorkId == workId);
        if (providerName != null)
            query = query.Where(c => c.ProviderName == providerName);
        return await query.OrderByDescending(c => c.GeneratedAt).FirstOrDefaultAsync(ct);
    }

    public async Task UpsertAsync(CoverCache cache, CancellationToken ct = default)
    {
        var existing = await _db.CoverCaches
            .FirstOrDefaultAsync(c => c.WorkId == cache.WorkId && c.ProviderName == cache.ProviderName, ct);

        if (existing == null)
            _db.CoverCaches.Add(cache);
        else
        {
            _db.CoverCaches.Remove(existing);
            _db.CoverCaches.Add(cache);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid workId, CancellationToken ct = default)
    {
        var entries = await _db.CoverCaches.Where(c => c.WorkId == workId).ToListAsync(ct);
        if (entries.Count > 0)
        {
            _db.CoverCaches.RemoveRange(entries);
            await _db.SaveChangesAsync(ct);
        }
    }
}
