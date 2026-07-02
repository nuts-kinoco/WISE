using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using WISE.Application.Queries;

namespace WISE.Api.Controllers;

[ApiController]
[Route("api/home")]
public class HomeController : ControllerBase
{
    private readonly IHomeQueryService _homeQuery;

    public HomeController(IHomeQueryService homeQuery) => _homeQuery = homeQuery;

    [HttpGet]
    public async Task<IActionResult> GetHome(
        [FromQuery] string? deviceId,
        CancellationToken ct)
    {
        // 注意: DbContext はスレッドセーフではないため、逐次 await で実行する
        // （P3監査 A-1: 同一DbContextへの並行クエリ禁止）
        var continueWatching = await _homeQuery.GetContinueWatchingAsync(deviceId, ct);
        var recentlyAdded    = await _homeQuery.GetRecentlyAddedAsync(ct);
        var favorites        = await _homeQuery.GetFavoritesAsync(ct);

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
        var work = await _homeQuery.GetRandomAsync(ct);
        if (work == null) return NotFound();
        return Ok(WorkItemMapper.Map(work));
    }
}
