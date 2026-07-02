using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Infrastructure.Data;

namespace WISE.Api.Controllers;

[ApiController]
[Route("api/home")]
public class HomeController : ControllerBase
{
    private readonly WiseDbContext _db;

    public HomeController(WiseDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetHome(
        [FromQuery] string? deviceId,
        CancellationToken ct)
    {
        // 注意: DbContext はスレッドセーフではないため、同一コンテキストへの
        // 並行クエリ（Task.WhenAll）は禁止。逐次 await で実行する。
        // （SQLite プロバイダは実質同期実行のため実行時間は変わらない）
        var continueWatching = await GetContinueWatchingAsync(deviceId, ct);
        var recentlyAdded    = await GetRecentlyAddedAsync(ct);
        var favorites        = await GetFavoritesAsync(ct);

        return Ok(new
        {
            continueWatching = continueWatching.Select(WorkItemMapper.Map),
            recentlyAdded    = recentlyAdded.Select(WorkItemMapper.Map),
            favorites        = favorites.Select(WorkItemMapper.Map),
        });
    }

    [HttpGet("random")]
    public async Task<IActionResult> GetRandom(CancellationToken ct)
    {
        var work = await _db.Works
            .AsNoTracking()
            .Include(w => w.MetadataFields)
            .Include(w => w.Assets)
            .OrderBy(_ => EF.Functions.Random())
            .FirstOrDefaultAsync(ct);

        if (work == null) return NotFound();
        return Ok(WorkItemMapper.Map(work));
    }

    // ── private helpers ────────────────────────────────────────────────────────

    private async Task<List<Work>> GetContinueWatchingAsync(string? deviceId, CancellationToken ct)
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

    private async Task<List<Work>> GetRecentlyAddedAsync(CancellationToken ct)
        => await _db.Works
            .AsNoTracking()
            .Include(w => w.MetadataFields)
            .Include(w => w.Assets)
            .OrderByDescending(w => w.CreatedAt)
            .Take(12)
            .ToListAsync(ct);

    private async Task<List<Work>> GetFavoritesAsync(CancellationToken ct)
        => await _db.Works
            .AsNoTracking()
            .Include(w => w.MetadataFields)
            .Include(w => w.Assets)
            .Where(w => w.Favorite)
            .OrderBy(_ => EF.Functions.Random())
            .Take(8)
            .ToListAsync(ct);
}
