using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Infrastructure.Data;

namespace WISE.Api.UseCases;

/// <summary>
/// P1 リファクタリング Phase3: CollectionsController から WiseDbContext 直接参照を排除するための UseCase。
/// Collection の作成・更新・削除・アイテム追加/削除（書込系）を担う。
/// </summary>
public class CollectionUseCase
{
    private readonly WiseDbContext _db;

    public CollectionUseCase(WiseDbContext db) => _db = db;

    public async Task<Collection> CreateAsync(string name, string? description, CancellationToken ct = default)
    {
        var collection = new Collection
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description?.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.Collections.Add(collection);
        await _db.SaveChangesAsync(ct);
        return collection;
    }

    public async Task<bool> PatchAsync(Guid id, string? name, string? description, CancellationToken ct = default)
    {
        var collection = await _db.Collections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (collection == null) return false;

        if (name is not null) collection.Name = name.Trim();
        if (description is not null) collection.Description = description.Trim();
        collection.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var collection = await _db.Collections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (collection == null) return false;

        _db.Collections.Remove(collection);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public enum AddItemResult { Ok, CollectionNotFound, InvalidWorkId, AlreadyExists }

    public async Task<(AddItemResult Result, CollectionItem? Item)> AddItemAsync(
        Guid collectionId, string workIdRaw, CancellationToken ct = default)
    {
        var collection = await _db.Collections
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == collectionId, ct);
        if (collection == null) return (AddItemResult.CollectionNotFound, null);

        if (!Guid.TryParse(workIdRaw, out var workId))
            return (AddItemResult.InvalidWorkId, null);

        if (collection.Items.Any(i => i.WorkId == workId))
            return (AddItemResult.AlreadyExists, null);

        var maxOrder = collection.Items.Count > 0 ? collection.Items.Max(i => i.Order) : -1;

        var item = new CollectionItem
        {
            Id = Guid.NewGuid(),
            CollectionId = collectionId,
            WorkId = workId,
            Order = maxOrder + 1,
            AddedAt = DateTime.UtcNow,
        };

        _db.CollectionItems.Add(item);
        collection.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return (AddItemResult.Ok, item);
    }

    public async Task<bool> RemoveItemAsync(Guid collectionId, Guid workId, CancellationToken ct = default)
    {
        var item = await _db.CollectionItems
            .FirstOrDefaultAsync(i => i.CollectionId == collectionId && i.WorkId == workId, ct);
        if (item == null) return false;

        _db.CollectionItems.Remove(item);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
