using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Application.Queries;
using WISE.Domain.Enums;

namespace WISE.Infrastructure.Data.Queries;

public class CollectionsQueryService : ICollectionsQueryService
{
    private readonly WiseDbContext _db;

    public CollectionsQueryService(WiseDbContext db) => _db = db;

    public async Task<IReadOnlyList<CollectionSummaryDto>> GetAllAsync(CancellationToken ct = default)
    {
        var collections = await _db.Collections
            .AsNoTracking()
            .OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Description,
                c.CreatedAt,
                c.UpdatedAt,
                itemCount = c.Items.Count,
                firstWorkId = c.Items.OrderBy(i => i.Order).Select(i => (Guid?)i.WorkId).FirstOrDefault()
            })
            .ToListAsync(ct);

        // 各コレクションの先頭Workのカバー画像を解決
        var firstWorkIds = collections.Where(c => c.firstWorkId.HasValue).Select(c => c.firstWorkId!.Value).Distinct().ToList();
        var covers = await _db.Assets
            .AsNoTracking()
            .Where(a => firstWorkIds.Contains(a.WorkId!.Value))
            .Where(a => a.AssetType == AssetType.PortraitCover || a.AssetType == AssetType.LandscapeCover)
            .ToListAsync(ct);

        var coverMap = covers
            .GroupBy(a => a.WorkId!.Value)
            .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.Id);

        return collections.Select(c => new CollectionSummaryDto(
            c.Id, c.Name, c.Description, c.CreatedAt, c.UpdatedAt, c.itemCount,
            c.firstWorkId.HasValue && coverMap.TryGetValue(c.firstWorkId.Value, out var assetId) && assetId.HasValue
                ? $"/api/assets/{assetId}/content"
                : null
        )).ToList();
    }

    public async Task<CollectionDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var collection = await _db.Collections
            .AsNoTracking()
            .Include(c => c.Items.OrderBy(i => i.Order))
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (collection == null) return null;

        var workIds = collection.Items.Select(i => i.WorkId).ToList();
        var works = await _db.Works
            .AsNoTracking()
            .Include(w => w.MetadataFields)
            .Include(w => w.Assets)
            .Where(w => workIds.Contains(w.Id))
            .ToListAsync(ct);

        var workMap = works.ToDictionary(w => w.Id);

        var items = collection.Items
            .Where(i => workMap.ContainsKey(i.WorkId))
            .Select(i => new CollectionDetailItemDto(i.Id, i.Order, i.AddedAt, workMap[i.WorkId]))
            .ToList();

        return new CollectionDetailDto(
            collection.Id, collection.Name, collection.Description,
            collection.CreatedAt, collection.UpdatedAt, items);
    }
}
