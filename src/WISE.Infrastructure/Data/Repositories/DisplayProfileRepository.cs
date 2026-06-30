using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Data.Repositories;

public class DisplayProfileRepository : IDisplayProfileRepository
{
    private readonly WiseDbContext _db;

    public DisplayProfileRepository(WiseDbContext db) => _db = db;

    public async Task<DisplayProfile?> GetByMediaTypeAsync(MediaType mediaType, CancellationToken ct = default)
        => await _db.DisplayProfiles
            .Include(p => p.Fields)
            .FirstOrDefaultAsync(p => p.MediaType == mediaType, ct);

    public async Task<IReadOnlyList<DisplayProfile>> GetAllAsync(CancellationToken ct = default)
        => await _db.DisplayProfiles
            .Include(p => p.Fields)
            .OrderBy(p => p.MediaType)
            .ToListAsync(ct);

    public async Task UpsertAsync(DisplayProfile profile, CancellationToken ct = default)
    {
        var existing = await _db.DisplayProfiles
            .Include(p => p.Fields)
            .FirstOrDefaultAsync(p => p.MediaType == profile.MediaType, ct);

        if (existing == null)
        {
            _db.DisplayProfiles.Add(profile);
        }
        else
        {
            _db.DisplayProfileFields.RemoveRange(existing.Fields);
            _db.DisplayProfiles.Remove(existing);
            _db.DisplayProfiles.Add(profile);
        }

        await _db.SaveChangesAsync(ct);
    }
}
