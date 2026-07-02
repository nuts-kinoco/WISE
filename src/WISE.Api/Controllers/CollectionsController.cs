using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using WISE.Api.UseCases;
using WISE.Application.Queries;

namespace WISE.Api.Controllers;

[ApiController]
[Route("api/collections")]
public class CollectionsController : ControllerBase
{
    private readonly ICollectionsQueryService _query;
    private readonly CollectionUseCase _useCase;

    public CollectionsController(ICollectionsQueryService query, CollectionUseCase useCase)
    {
        _query = query;
        _useCase = useCase;
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await _query.GetAllAsync(ct);
        return Ok(result);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCollectionRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest("Name is required.");

        var collection = await _useCase.CreateAsync(req.Name, req.Description, ct);

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
        var collection = await _query.GetByIdAsync(id, ct);
        if (collection == null) return NotFound();

        return Ok(new
        {
            collection.Id,
            collection.Name,
            collection.Description,
            collection.CreatedAt,
            collection.UpdatedAt,
            items = collection.Items.Select(i => new
            {
                Id = i.ItemId,
                i.Order,
                i.AddedAt,
                work = WorkItemMapper.Map(i.Work),
            }).ToList(),
        });
    }

    // ── Rename ────────────────────────────────────────────────────────────────

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Patch(Guid id, [FromBody] PatchCollectionRequest req, CancellationToken ct)
    {
        var success = await _useCase.PatchAsync(id, req.Name, req.Description, ct);
        return success ? NoContent() : NotFound();
    }

    // ── Delete Collection ─────────────────────────────────────────────────────

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var success = await _useCase.DeleteAsync(id, ct);
        return success ? NoContent() : NotFound();
    }

    // ── Add Work ──────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/items")]
    public async Task<IActionResult> AddItem(Guid id, [FromBody] AddItemRequest req, CancellationToken ct)
    {
        var (result, item) = await _useCase.AddItemAsync(id, req.WorkId, ct);
        return result switch
        {
            CollectionUseCase.AddItemResult.CollectionNotFound => NotFound(),
            CollectionUseCase.AddItemResult.InvalidWorkId => BadRequest("Invalid WorkId."),
            CollectionUseCase.AddItemResult.AlreadyExists => Conflict("Work is already in this collection."),
            _ => Ok(new { item!.Id, item.Order, item.AddedAt }),
        };
    }

    // ── Remove Work ───────────────────────────────────────────────────────────

    [HttpDelete("{id:guid}/items/{workId}")]
    public async Task<IActionResult> RemoveItem(Guid id, string workId, CancellationToken ct)
    {
        if (!Guid.TryParse(workId, out var wid))
            return BadRequest("Invalid WorkId.");

        var success = await _useCase.RemoveItemAsync(id, wid, ct);
        return success ? NoContent() : NotFound();
    }
}

public record CreateCollectionRequest(string Name, string? Description);
public record PatchCollectionRequest(string? Name, string? Description);
public record AddItemRequest(string WorkId);
