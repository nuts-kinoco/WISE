using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;

namespace WISE.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly IAppSettingsRepository _appSettings;
    private readonly IDisplayProfileRepository _profiles;

    public SettingsController(IAppSettingsRepository appSettings, IDisplayProfileRepository profiles)
    {
        _appSettings = appSettings;
        _profiles = profiles;
    }

    // デフォルト設定（DB未登録のキーに対して返す）
    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["downloadSampleImages"] = "false",
    };

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var stored = await _appSettings.GetAllAsync(ct);

        var result = Defaults
            .Select(kv => new
            {
                key   = kv.Key,
                value = stored.TryGetValue(kv.Key, out var v) ? v : kv.Value,
            })
            .ToList();

        return Ok(result);
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key, CancellationToken ct)
    {
        if (!Defaults.ContainsKey(key))
            return NotFound(new { error = $"Unknown setting key: {key}" });

        var stored = await _appSettings.GetAsync(key, ct);
        var value  = stored?.Value ?? Defaults[key];
        return Ok(new { key, value });
    }

    // ── Display Profile endpoints ──────────────────────────────────────────

    [HttpGet("display-profiles")]
    public async Task<IActionResult> GetAllDisplayProfiles(CancellationToken ct)
    {
        var all = await _profiles.GetAllAsync(ct);
        return Ok(all.Select(MapProfile));
    }

    [HttpGet("display-profiles/{mediaType}")]
    public async Task<IActionResult> GetDisplayProfile(string mediaType, CancellationToken ct)
    {
        if (!Enum.TryParse<MediaType>(mediaType, true, out var mt))
            return BadRequest(new { error = $"Unknown MediaType: {mediaType}" });

        var profile = await _profiles.GetByMediaTypeAsync(mt, ct);
        if (profile == null) return NotFound();
        return Ok(MapProfile(profile));
    }

    public record UpdateDisplayProfileDto(
        string? CoverOrientation,
        string? DefaultSort,
        List<UpdateDisplayFieldDto>? Fields);

    public record UpdateDisplayFieldDto(string FieldName, string Label, bool IsVisible, int DisplayOrder);

    [HttpPatch("display-profiles/{mediaType}")]
    public async Task<IActionResult> PatchDisplayProfile(
        string mediaType,
        [FromBody] UpdateDisplayProfileDto dto,
        CancellationToken ct)
    {
        if (!Enum.TryParse<MediaType>(mediaType, true, out var mt))
            return BadRequest(new { error = $"Unknown MediaType: {mediaType}" });

        var profile = await _profiles.GetByMediaTypeAsync(mt, ct);
        if (profile == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.CoverOrientation))
            profile.UpdateCoverOrientation(dto.CoverOrientation);

        if (dto.Fields != null)
        {
            foreach (var f in dto.Fields)
            {
                var existing = profile.Fields.FirstOrDefault(pf => pf.FieldName == f.FieldName);
                if (existing != null)
                    existing.Update(f.IsVisible, f.DisplayOrder, f.Label);
            }
            profile.MarkAsCustomized();
        }

        await _profiles.UpsertAsync(profile, ct);
        return Ok(MapProfile(profile));
    }

    [HttpDelete("display-profiles/{mediaType}")]
    public async Task<IActionResult> ResetDisplayProfile(string mediaType, CancellationToken ct)
    {
        if (!Enum.TryParse<MediaType>(mediaType, true, out var mt))
            return BadRequest(new { error = $"Unknown MediaType: {mediaType}" });

        var profile = await _profiles.GetByMediaTypeAsync(mt, ct);
        if (profile == null) return NotFound();

        profile.ResetToDefault();
        await _profiles.UpsertAsync(profile, ct);
        return Ok(MapProfile(profile));
    }

    private static object MapProfile(DisplayProfile p) => new
    {
        mediaType = p.MediaType.ToString(),
        coverOrientation = p.CoverOrientation,
        defaultSort = p.DefaultSort,
        isUserCustomized = p.IsUserCustomized,
        fields = p.Fields
            .OrderBy(f => f.DisplayOrder)
            .Select(f => new { f.FieldName, f.Label, f.IsVisible, f.DisplayOrder })
    };

    // ── App Setting endpoints ──────────────────────────────────────────────

    public record SetValueDto(string Value);

    [HttpPatch("{key}")]
    public async Task<IActionResult> Patch(string key, [FromBody] SetValueDto dto, CancellationToken ct)
    {
        if (!Defaults.ContainsKey(key))
            return NotFound(new { error = $"Unknown setting key: {key}" });

        await _appSettings.SetAsync(key, dto.Value, ct);
        return Ok(new { key, value = dto.Value });
    }
}
