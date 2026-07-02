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

    // P3監査 B-1 是正: 従来は全Workを MetadataFields+Assets 込みでロードしてから
    // メモリ上でグルーピングしていたため、蔵書規模が大きくなるとライブラリ全件のフルロードが
    // 発生していた。品番重複はSQL側でGroupByし、タイトル重複はTitleフィールドのみの軽量射影
    // （NormalizeTitleがRegexを使うためSQL変換不能＝クライアント評価は避けられないが、対象を
    // Titleカラムのみに絞れる）で候補を先に絞り込み、実際に重複と判定されたWorkIdの集合に対して
    // のみ MetadataFields+Assets をフルロードする2段構えにした。
    public async Task<IReadOnlyList<DuplicateGroupDto>> GetDuplicateGroupsAsync(CancellationToken ct = default)
    {
        // --- Step1: 品番重複をSQL側で検出（フルロードなし） ---
        var duplicateIdentifierKeys = await _db.Works
            .AsNoTracking()
            .Where(w => w.PrimaryIdentifier != null)
            .GroupBy(w => w.PrimaryIdentifier!.ToUpper())
            .Where(g => g.Count() >= 2)
            .Select(g => g.Key)
            .ToListAsync(ct);

        var identifierDuplicateIds = duplicateIdentifierKeys.Count > 0
            ? await _db.Works.AsNoTracking()
                .Where(w => w.PrimaryIdentifier != null && duplicateIdentifierKeys.Contains(w.PrimaryIdentifier!.ToUpper()))
                .Select(w => w.Id)
                .ToListAsync(ct)
            : new List<Guid>();

        var seen = new HashSet<Guid>(identifierDuplicateIds);

        // --- Step2: タイトル重複候補をTitleのみの軽量射影で検出（品番重複は除外） ---
        var titleCandidates = await _db.Works
            .AsNoTracking()
            .Where(w => !seen.Contains(w.Id))
            .Select(w => new
            {
                w.Id,
                Title = w.MetadataFields.Where(m => m.FieldName == "Title" && m.IsPrimary).Select(m => m.Value).FirstOrDefault()
                     ?? w.MetadataFields.Where(m => m.FieldName == "Title").Select(m => m.Value).FirstOrDefault()
            })
            .ToListAsync(ct);

        var titleGroups = titleCandidates
            .Select(x => new { x.Id, NormalizedTitle = NormalizeTitle(x.Title) })
            .Where(x => !string.IsNullOrWhiteSpace(x.NormalizedTitle) && x.NormalizedTitle!.Length >= 8)
            .GroupBy(x => x.NormalizedTitle!)
            .Where(g => g.Count() >= 2)
            .OrderBy(g => g.Key)
            .ToList();

        var titleDuplicateIds = titleGroups.SelectMany(g => g.Select(x => x.Id)).ToList();

        // --- Step3: 重複と判定されたWorkIdのみフルロード（MetadataFields+Assets） ---
        var allDuplicateIds = seen.Concat(titleDuplicateIds).Distinct().ToList();
        if (allDuplicateIds.Count == 0) return Array.Empty<DuplicateGroupDto>();

        var fullWorks = await _db.Works
            .AsNoTracking()
            .Include(w => w.MetadataFields)
            .Include(w => w.Assets)
            .Where(w => allDuplicateIds.Contains(w.Id))
            .ToListAsync(ct);

        var workMap = fullWorks.ToDictionary(w => w.Id);

        var result = new List<DuplicateGroupDto>();

        foreach (var key in duplicateIdentifierKeys.OrderBy(k => k))
        {
            var groupWorks = fullWorks.Where(w =>
                w.PrimaryIdentifier != null && w.PrimaryIdentifier.ToUpperInvariant() == key);
            result.Add(BuildGroup(key, "identifier", groupWorks));
        }

        foreach (var g in titleGroups)
            result.Add(BuildGroup(g.Key, "title", g.Select(x => workMap[x.Id])));

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
