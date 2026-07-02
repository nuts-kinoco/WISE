using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Domain.Enums;
using WISE.Infrastructure.Data;

namespace WISE.Api.UseCases;

/// <summary>
/// P1 リファクタリング Phase5: WorksController から WiseDbContext 直接参照を排除するための UseCase。
/// トリアージでの手動メタデータ上書き・ユーザータグ・ジャンルタグ削除を担う。
/// </summary>
public class WorkMetadataUseCase
{
    private readonly WiseDbContext _dbContext;

    public WorkMetadataUseCase(WiseDbContext dbContext) => _dbContext = dbContext;

    public record ManualFields(
        string? Title, string? Actress, string? Maker, string? Label,
        string? Series, string? ReleaseDate, string? Genre, string? Runtime);

    public async Task<bool> PatchManualMetadataAsync(Guid workId, ManualFields dto, CancellationToken ct = default)
    {
        var work = await _dbContext.Works
            .Include(w => w.MetadataFields)
            .FirstOrDefaultAsync(w => w.Id == workId, ct);
        if (work == null) return false;

        var fields = new Dictionary<string, string?>
        {
            ["Title"] = dto.Title,
            ["Actress"] = dto.Actress,
            ["Maker"] = dto.Maker,
            ["Label"] = dto.Label,
            ["Series"] = dto.Series,
            ["ReleaseDate"] = dto.ReleaseDate,
            ["Genre"] = dto.Genre,
            ["Runtime"] = dto.Runtime,
        };

        foreach (var (fieldName, value) in fields)
        {
            if (value is null) continue;

            // 既存 primary を非 primary に降格
            foreach (var existing in work.MetadataFields.Where(m => m.FieldName == fieldName && m.IsPrimary))
                existing.SetPrimary(false);

            // Manual エントリを追加（最高優先度 999）
            var newField = new MetadataField(fieldName, value, "Manual", true, 999);
            newField.SetWorkId(work.Id);
            _dbContext.MetadataFields.Add(newField);
        }

        work.UpdateStatus(ProcessingStatus.Organized);
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public enum AddUserTagResult { NotFound, AlreadyExists, Ok }

    public async Task<AddUserTagResult> AddUserTagAsync(Guid workId, string value, CancellationToken ct = default)
    {
        var work = await _dbContext.Works
            .Include(w => w.MetadataFields)
            .FirstOrDefaultAsync(w => w.Id == workId, ct);
        if (work == null) return AddUserTagResult.NotFound;

        var trimmed = value.Trim();
        if (work.MetadataFields.Any(m => m.FieldName == "UserTag" && m.Value == trimmed))
            return AddUserTagResult.AlreadyExists;

        var field = new MetadataField("UserTag", trimmed, "User", true, 100);
        field.SetWorkId(work.Id);
        _dbContext.MetadataFields.Add(field);
        await _dbContext.SaveChangesAsync(ct);
        return AddUserTagResult.Ok;
    }

    public async Task<bool> DeleteUserTagAsync(Guid workId, string tagValue, CancellationToken ct = default)
    {
        var field = await _dbContext.MetadataFields
            .FirstOrDefaultAsync(m => m.WorkId == workId && m.FieldName == "UserTag" && m.Value == tagValue, ct);
        if (field == null) return false;

        _dbContext.MetadataFields.Remove(field);
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteGenreTagAsync(Guid workId, string tagValue, CancellationToken ct = default)
    {
        var genreFields = await _dbContext.MetadataFields
            .Where(m => m.WorkId == workId && m.FieldName == "Genre")
            .ToListAsync(ct);

        bool changed = false;
        foreach (var gf in genreFields)
        {
            var allTags = gf.Value.Split('|').Select(t => t.Trim()).Where(t => t != "").ToList();
            var remaining = allTags.Where(t => t != tagValue).ToList();
            if (remaining.Count != allTags.Count)
            {
                changed = true;
                if (remaining.Count == 0)
                    _dbContext.MetadataFields.Remove(gf);
                else
                    gf.UpdateValue(string.Join("|", remaining), gf.ConfidenceScore, gf.ProviderId);
            }
        }

        if (!changed) return false;
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }
}
