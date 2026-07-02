using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Application.Queries;
using WISE.Domain.Entities;
using WISE.Infrastructure.Services;

namespace WISE.Infrastructure.Data.Queries;

public class DuplicatesQueryService : IDuplicatesQueryService
{
    private readonly WiseDbContext _db;

    public DuplicatesQueryService(WiseDbContext db) => _db = db;

    public async Task<IReadOnlyList<DuplicateGroupDto>> GetDuplicateGroupsAsync(CancellationToken ct = default)
    {
        var works = await _db.Works
            .AsNoTracking()
            .Include(w => w.MetadataFields)
            .Include(w => w.Assets)
            .ToListAsync(ct);

        var seen = new HashSet<Guid>();
        var result = new List<DuplicateGroupDto>();

        // 1. PrimaryIdentifier 完全一致
        var identifierGroups = works
            .Where(w => w.PrimaryIdentifier != null)
            .GroupBy(w => w.PrimaryIdentifier!.ToUpperInvariant())
            .Where(g => g.Count() >= 2);

        foreach (var g in identifierGroups.OrderBy(g => g.Key))
        {
            foreach (var w in g) seen.Add(w.Id);
            result.Add(BuildGroup(g.Key, "identifier", g));
        }

        // 2. タイトル正規化一致（品番重複で既に検出済みを除く）
        var titleGroups = works
            .Where(w => !seen.Contains(w.Id))
            .Select(w => new
            {
                Work = w,
                NormalizedTitle = NormalizeTitle(
                    w.MetadataFields.FirstOrDefault(m => m.FieldName == "Title" && m.IsPrimary)?.Value
                    ?? w.MetadataFields.FirstOrDefault(m => m.FieldName == "Title")?.Value)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.NormalizedTitle) && x.NormalizedTitle.Length >= 8)
            .GroupBy(x => x.NormalizedTitle!)
            .Where(g => g.Count() >= 2);

        foreach (var g in titleGroups.OrderBy(g => g.Key))
            result.Add(BuildGroup(g.Key, "title", g.Select(x => x.Work)));

        return result;
    }

    private static string? NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var s = title.ToLowerInvariant();
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[\s　]+", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[【】「」『』（）()【】\[\]!！?？。、,\.…・★☆♥♡◆◇■□▲△▼▽]", "");
        return s.Trim();
    }

    private static DuplicateGroupDto BuildGroup(string key, string detectionType, IEnumerable<Work> works)
    {
        var workDtos = works.Select(w => new DuplicateWorkDto(
            w.Id,
            w.PrimaryIdentifier,
            w.Status.ToString(),
            w.MetadataFields.FirstOrDefault(m => m.FieldName == "Title" && m.IsPrimary)?.Value
                ?? w.MetadataFields.FirstOrDefault(m => m.FieldName == "Title")?.Value,
            w.MetadataFields.FirstOrDefault(m => m.FieldName == "Actress" && m.IsPrimary)?.Value
                ?? w.MetadataFields.FirstOrDefault(m => m.FieldName == "Actress")?.Value,
            w.MetadataFields.FirstOrDefault(m => m.FieldName == "Maker" && m.IsPrimary)?.Value
                ?? w.MetadataFields.FirstOrDefault(m => m.FieldName == "Maker")?.Value,
            w.Favorite,
            w.Rating,
            WorkMetadataJsonHelper.GetUserMemo(w.Assets),
            w.Assets.Select(a => new DuplicateAssetDto(
                a.Id, a.OriginalFilename, a.FileSize, a.AssetType.ToString())).ToList()
        )).ToList();

        return new DuplicateGroupDto(key, detectionType, workDtos);
    }
}
