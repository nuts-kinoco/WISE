using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Infrastructure.Data;

namespace WISE.Api.Controllers;

[ApiController]
[Route("api/collections")]
public class CollectionsController : ControllerBase
{
    private readonly WiseDbContext _db;

    public CollectionsController(WiseDbContext db) => _db = db;

    // ── List ──────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var collections = await _db.Collections
            .AsNoTracking()
            .Include(c => c.Items)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Description,
                c.CreatedAt,
                c.UpdatedAt,
                itemCount = c.Items.Count,
            })
            .ToListAsync(ct);

        return Ok(collections);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCollectionRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest("Name is required.");

        var collection = new Collection
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            Description = req.Description?.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.Collections.Add(collection);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = collection.Id }, new
        {
            collection.Id,
            collection.Name,
            collection.Description,
            collection.CreatedAt,
            collection.UpdatedAt,
            itemCount = 0,
        });
    }

    // ── Get by ID ─────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var collection = await _db.Collections
            .AsNoTracking()
            .Include(c => c.Items.OrderBy(i => i.Order))
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (collection == null) return NotFound();

        // Load works for items
        var workIds = collection.Items.Select(i => i.WorkId).ToList();
        var works = await _db.Works
            .AsNoTracking()
            .Include(w => w.MetadataFields)
            .Include(w => w.Assets)
            .Where(w => workIds.Contains(w.Id))
            .ToListAsync(ct);

        var workMap = works.ToDictionary(w => w.Id);

        return Ok(new
        {
            collection.Id,
            collection.Name,
            collection.Description,
            collection.CreatedAt,
            collection.UpdatedAt,
            items = collection.Items
                .Where(i => workMap.ContainsKey(i.WorkId))
                .Select(i => new
                {
                    i.Id,
                    i.Order,
                    i.AddedAt,
                    work = WorkItemMapper.Map(workMap[i.WorkId]),
                })
                .ToList(),
        });
    }

    // ── Rename ────────────────────────────────────────────────────────────────

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Patch(Guid id, [FromBody] PatchCollectionRequest req, CancellationToken ct)
    {
        var collection = await _db.Collections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (collection == null) return NotFound();

        if (req.Name is not null) collection.Name = req.Name.Trim();
        if (req.Description is not null) collection.Description = req.Description.Trim();
        collection.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Delete Collection ─────────────────────────────────────────────────────

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var collection = await _db.Collections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (collection == null) return NotFound();

        _db.Collections.Remove(collection);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Add Work ──────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/items")]
    public async Task<IActionResult> AddItem(Guid id, [FromBody] AddItemRequest req, CancellationToken ct)
    {
        var collection = await _db.Collections
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (collection == null) return NotFound();

        if (!Guid.TryParse(req.WorkId, out var workId))
            return BadRequest("Invalid WorkId.");

        if (collection.Items.Any(i => i.WorkId == workId))
            return Conflict("Work is already in this collection.");

        var maxOrder = collection.Items.Count > 0 ? collection.Items.Max(i => i.Order) : -1;

        var item = new CollectionItem
        {
            Id = Guid.NewGuid(),
            CollectionId = id,
            WorkId = workId,
            Order = maxOrder + 1,
            AddedAt = DateTime.UtcNow,
        };

        _db.CollectionItems.Add(item);
        collection.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new { item.Id, item.Order, item.AddedAt });
    }

    // ── Remove Work ───────────────────────────────────────────────────────────

    [HttpDelete("{id:guid}/items/{workId}")]
    public async Task<IActionResult> RemoveItem(Guid id, string workId, CancellationToken ct)
    {
        if (!Guid.TryParse(workId, out var wid))
            return BadRequest("Invalid WorkId.");

        var item = await _db.CollectionItems
            .FirstOrDefaultAsync(i => i.CollectionId == id && i.WorkId == wid, ct);
        if (item == null) return NotFound();

        _db.CollectionItems.Remove(item);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public record CreateCollectionRequest(string Name, string? Description);
public record PatchCollectionRequest(string? Name, string? Description);
public record AddItemRequest(string WorkId);
