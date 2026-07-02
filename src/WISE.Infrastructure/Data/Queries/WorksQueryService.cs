using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Application.Queries;
using WISE.Domain.Entities;
using WISE.Domain.Enums;

namespace WISE.Infrastructure.Data.Queries;

public class WorksQueryService : IWorksQueryService
{
    private readonly WiseDbContext _db;

    public WorksQueryService(WiseDbContext db) => _db = db;

    public async Task<(IReadOnlyList<Work> Works, int TotalCount)> GetListAsync(
        int page, int pageSize, string? q, string? status, string? mediaType, string? sort,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Works
            .AsNoTracking()
            .Include(w => w.MetadataFields)
            .Include(w => w.Assets)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var statuses = status.Split(',')
                .Select(s => Enum.TryParse<ProcessingStatus>(s.Trim(), true, out var ps) ? ps : (ProcessingStatus?)null)
                .Where(ps => ps.HasValue)
                .Select(ps => ps!.Value)
                .ToList();
            if (statuses.Count > 0)
                query = query.Where(w => statuses.Contains(w.Status));
        }

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var types = mediaType.Split(',')
                .Select(t => Enum.TryParse<MediaType>(t.Trim(), true, out var mt) ? mt : (MediaType?)null)
                .Where(mt => mt.HasValue)
                .Select(mt => mt!.Value)
                .ToList();
            if (types.Count > 0)
                query = query.Where(w => types.Contains(w.MediaType));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var lowerQ = q.ToLower();
            query = query.Where(w =>
                (w.PrimaryIdentifier != null && w.PrimaryIdentifier.ToLower().Contains(lowerQ)) ||
                w.MetadataFields.Any(m => m.FieldName == "Title" && m.Value != null && m.Value.ToLower().Contains(lowerQ)) ||
                w.MetadataFields.Any(m => m.FieldName == "Maker" && m.Value != null && m.Value.ToLower().Contains(lowerQ)) ||
                w.MetadataFields.Any(m => m.FieldName == "Actress" && m.Value != null && m.Value.ToLower().Contains(lowerQ)) ||
                w.MetadataFields.Any(m => m.FieldName == "ActressTag" && m.Value != null && m.Value.ToLower().Contains(lowerQ)) ||
                w.MetadataFields.Any(m => m.FieldName == "Label" && m.Value != null && m.Value.ToLower().Contains(lowerQ)) ||
                w.MetadataFields.Any(m => m.FieldName == "Genre" && m.Value != null && m.Value.ToLower().Contains(lowerQ)) ||
                w.MetadataFields.Any(m => m.FieldName == "Tag" && m.Value != null && m.Value.ToLower().Contains(lowerQ))
            );
        }

        var totalCount = await query.CountAsync(ct);

        IQueryable<Work> sorted = (sort ?? "added") switch
        {
            "rating" => query.OrderByDescending(w => w.Rating == null ? -1 : (double)w.Rating)
                              .ThenByDescending(w => w.CreatedAt),
            "title" => query.OrderBy(w =>
                              w.MetadataFields.Where(m => m.FieldName == "Title" && m.IsPrimary).Select(m => m.Value).FirstOrDefault()
                              ?? w.MetadataFields.Where(m => m.FieldName == "Title").Select(m => m.Value).FirstOrDefault()),
            "identifier" => query.OrderBy(w => w.PrimaryIdentifier),
            "release" => query.OrderByDescending(w =>
                              w.MetadataFields.Where(m => m.FieldName == "ReleaseDate" && m.IsPrimary).Select(m => m.Value).FirstOrDefault()
                              ?? w.MetadataFields.Where(m => m.FieldName == "ReleaseDate" || m.FieldName == "release_date").Select(m => m.Value).FirstOrDefault())
                              .ThenByDescending(w => w.CreatedAt),
            "random" => query.OrderBy(_ => EF.Functions.Random()),
            _ => query.OrderByDescending(w => w.CreatedAt), // "added" default
        };

        var works = await sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (works, totalCount);
    }

    public async Task<WorkDetailQueryDto?> GetDetailAsync(Guid workId, CancellationToken ct = default)
    {
        var work = await _db.Works
            .AsNoTracking()
            .Include(w => w.MetadataFields)
            .Include(w => w.Assets)
            .FirstOrDefaultAsync(w => w.Id == workId, ct);

        if (work == null) return null;

        var history = await _db.EventLogs
            .AsNoTracking()
            .Where(e => e.TargetId == workId)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new WorkHistoryEventDto(e.EventType, e.OccurredAt, e.Actor, e.Payload))
            .ToListAsync(ct);

        var createEvent = history.FirstOrDefault(e => e.EventType == "Work Created");
        object? diagnostic = null;
        if (createEvent != null && !string.IsNullOrEmpty(createEvent.Payload))
        {
            try { diagnostic = JsonSerializer.Deserialize<object>(createEvent.Payload); } catch { }
        }

        string? userMemo = null;
        var sampleImages = new List<string>();
        var videoAsset = work.Assets.FirstOrDefault(a =>
            a.FilePath != null && (a.FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                || a.FilePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)));
        if (videoAsset?.FilePath != null)
        {
            var metaJsonPath = Path.Combine(Path.GetDirectoryName(videoAsset.FilePath)!, "metadata.json");
            if (File.Exists(metaJsonPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(metaJsonPath, ct));
                    if (doc.RootElement.TryGetProperty("userMemo", out var memoEl))
                        userMemo = memoEl.GetString();
                    if (doc.RootElement.TryGetProperty("sampleImages", out var samplesEl)
                        && samplesEl.ValueKind == JsonValueKind.Array)
                    {
                        sampleImages = samplesEl.EnumerateArray()
                            .Select(e => e.GetString())
                            .Where(s => s != null)
                            .Select(s => s!)
                            .ToList();
                    }
                }
                catch { }
            }
        }

        return new WorkDetailQueryDto(work, userMemo, sampleImages, history, diagnostic);
    }

    public async Task<IReadOnlyList<Work>> GetRelatedAsync(Guid workId, string? field, int limit, CancellationToken ct = default)
    {
        var targetFields = string.IsNullOrWhiteSpace(field)
            ? new[] { "Actress", "ActressTag", "Series", "Circle", "Author", "Maker" }
            : new[] { field };

        var sourceValues = await _db.MetadataFields
            .AsNoTracking()
            .Where(m => m.WorkId == workId && targetFields.Contains(m.FieldName) && m.Value != null && m.Value != "")
            .Select(m => new { m.FieldName, m.Value })
            .ToListAsync(ct);

        if (sourceValues.Count == 0) return Array.Empty<Work>();

        var fieldNames = sourceValues.Select(v => v.FieldName).Distinct().ToList();
        var values = sourceValues.Select(v => v.Value).Distinct().ToList();

        return await _db.Works
            .AsNoTracking()
            .Include(w => w.MetadataFields)
            .Include(w => w.Assets)
            .Where(w => w.Id != workId
                && w.MetadataFields.Any(m => fieldNames.Contains(m.FieldName) && values.Contains(m.Value)))
            .OrderByDescending(w => w.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task<Work?> GetForViewerInfoAsync(Guid workId, CancellationToken ct = default)
        => _db.Works.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workId, ct);

    public async Task<IReadOnlyList<ThumbnailAssetDto>?> GetThumbnailAssetsAsync(Guid workId, CancellationToken ct = default)
    {
        var work = await _db.Works.AsNoTracking()
            .Include(w => w.Assets)
            .Include(w => w.MetadataFields)
            .FirstOrDefaultAsync(w => w.Id == workId, ct);
        if (work == null) return null;

        var allowed = new[]
        {
            AssetType.PortraitCover, AssetType.LandscapeCover, AssetType.Thumbnail, AssetType.SampleImage,
        };

        var currentCoverUrl = work.MetadataFields
            .Where(m => m.FieldName == "PortraitCover" && m.IsPrimary && m.ProviderId == "Manual")
            .Select(m => m.Value)
            .FirstOrDefault();

        return work.Assets
            .Where(a => allowed.Contains(a.AssetType) && a.FilePath != null && File.Exists(a.FilePath))
            .OrderBy(a => a.AssetType) // PortraitCover → LandscapeCover → Thumbnail → SampleImage
            .Select(a => new ThumbnailAssetDto(
                a.Id,
                a.OriginalFilename,
                a.AssetType.ToString(),
                $"/api/assets/{a.Id}/content",
                currentCoverUrl != null && currentCoverUrl.Contains(a.Id.ToString())
            ))
            .ToList();
    }

    public Task<Work?> GetWorkWithAssetsAsync(Guid workId, CancellationToken ct = default)
        => _db.Works.AsNoTracking().Include(w => w.Assets).FirstOrDefaultAsync(w => w.Id == workId, ct);
}
