using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;
using WISE.Domain.Interfaces;

namespace WISE.Api.Controllers;

[ApiController]
[Route("api/works/{workId}/reading-history")]
public class ReadingHistoryController : ControllerBase
{
    private readonly IReadingHistoryRepository _repo;

    public ReadingHistoryController(IReadingHistoryRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<IActionResult> Get(
        string workId,
        [FromQuery] string deviceId,
        CancellationToken ct)
    {
        if (!Guid.TryParse(workId, out var wid)) return BadRequest("Invalid Work ID.");
        if (string.IsNullOrWhiteSpace(deviceId)) return BadRequest("deviceId is required.");

        var history = await _repo.GetAsync(wid, deviceId, ct);
        if (history == null) return NotFound();

        return Ok(Map(history));
    }

    public record UpsertDto(
        string DeviceId,
        int? PageNumber,
        float? PositionSeconds,
        float? PositionPercent);

    [HttpPut]
    public async Task<IActionResult> Upsert(
        string workId,
        [FromBody] UpsertDto dto,
        CancellationToken ct)
    {
        if (!Guid.TryParse(workId, out var wid)) return BadRequest("Invalid Work ID.");
        if (string.IsNullOrWhiteSpace(dto.DeviceId)) return BadRequest("deviceId is required.");

        var existing = await _repo.GetAsync(wid, dto.DeviceId, ct);
        if (existing == null)
        {
            existing = new ReadingHistory(wid, dto.DeviceId, dto.PageNumber, dto.PositionSeconds, dto.PositionPercent);
        }
        else
        {
            existing.UpdateProgress(dto.PageNumber, dto.PositionSeconds, dto.PositionPercent);
        }

        await _repo.UpsertAsync(existing, ct);
        return Ok(Map(existing));
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(
        string workId,
        [FromQuery] string deviceId,
        CancellationToken ct)
    {
        if (!Guid.TryParse(workId, out var wid)) return BadRequest("Invalid Work ID.");
        if (string.IsNullOrWhiteSpace(deviceId)) return BadRequest("deviceId is required.");

        await _repo.DeleteAsync(wid, deviceId, ct);
        return NoContent();
    }

    private static object Map(ReadingHistory h) => new
    {
        workId           = h.WorkId,
        deviceId         = h.DeviceId,
        pageNumber       = h.PageNumber,
        positionSeconds  = h.PositionSeconds,
        positionPercent  = h.PositionPercent,
        lastReadAt       = h.LastReadAt,
        updatedAt        = h.UpdatedAt,
    };
}
