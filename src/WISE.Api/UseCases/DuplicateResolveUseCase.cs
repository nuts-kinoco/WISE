using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WISE.Infrastructure.Data;
using WISE.Infrastructure.Services;

namespace WISE.Api.UseCases;

/// <summary>
/// P1 リファクタリング Phase4: DuplicatesController から WiseDbContext 直接参照を排除するための UseCase。
///
/// P3監査 A-2 の是正も兼ねる:
/// 旧実装は DeleteWorkIds をループしながら対象ごとに個別 FirstOrDefaultAsync（N+1）+
/// SaveChangesAsync を2回ずつ発行しており、複数削除対象の途中で例外が起きると
/// 一部だけマージ・削除が確定した部分適用状態が残るリスクがあった。
/// 本実装では対象を一括ロード（N+1解消）した上で、DB書き込み（マージ＋行削除）全体を
/// 単一トランザクションに包んで全部成功/全部失敗を保証する。物理ファイル削除は
/// DBコミット後にベストエフォートで行う（失敗してもDBはロールバックしない＝孤立ファイル
/// のリスクは残るが、これは元実装と同じ許容範囲であり、WorksController.DeleteWork の
/// 「ファイル削除失敗時はDB変更前に中断する」方針とは非対称。今回のスコープでは
/// DB整合性の是正のみを対象とし、ファイル削除順序の統一は別タスクとする）。
/// </summary>
public class DuplicateResolveUseCase
{
    private readonly WiseDbContext _dbContext;
    private readonly ILogger<DuplicateResolveUseCase> _logger;

    public DuplicateResolveUseCase(WiseDbContext dbContext, ILogger<DuplicateResolveUseCase> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public record ResolveRequest(
        Guid KeepWorkId,
        Guid[] DeleteWorkIds,
        bool DeleteFiles,
        bool MergeRating,
        bool MergeMemo,
        bool MergeUserTags,
        bool MergeFavorite
    );

    public enum ResolveOutcome { KeepWorkNotFound, Ok }

    public async Task<(ResolveOutcome Outcome, int FilesDeleted, int FilesFailed)> ResolveAsync(
        ResolveRequest req, CancellationToken ct = default)
    {
        var keepWork = await _dbContext.Works
            .Include(w => w.Assets)
            .Include(w => w.MetadataFields)
            .FirstOrDefaultAsync(w => w.Id == req.KeepWorkId, ct);
        if (keepWork == null) return (ResolveOutcome.KeepWorkNotFound, 0, 0);

        // 一括ロード（N+1回避）。存在しない/keepWorkと同一のIDは無視する。
        var deleteWorks = await _dbContext.Works
            .Include(w => w.Assets)
            .Include(w => w.MetadataFields)
            .Where(w => req.DeleteWorkIds.Contains(w.Id) && w.Id != req.KeepWorkId)
            .ToListAsync(ct);

        var filePathsToDelete = new List<string>();

        // --- DB書き込み全体を単一トランザクションに包む ---
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var deleteWork in deleteWorks)
            {
                if (req.MergeRating && keepWork.Rating == null && deleteWork.Rating != null)
                    keepWork.SetRating(deleteWork.Rating);

                if (req.MergeFavorite && !keepWork.Favorite && deleteWork.Favorite)
                    keepWork.SetFavorite(true);

                if (req.MergeMemo)
                {
                    var keepMemo = WorkMetadataJsonHelper.GetUserMemo(keepWork.Assets);
                    var deleteMemo = WorkMetadataJsonHelper.GetUserMemo(deleteWork.Assets);
                    if (string.IsNullOrWhiteSpace(keepMemo) && !string.IsNullOrWhiteSpace(deleteMemo))
                        await WorkMetadataJsonHelper.WriteUserMemoAsync(keepWork.Assets, deleteMemo!);
                }

                if (req.MergeUserTags)
                {
                    var keepTags = keepWork.MetadataFields
                        .Where(m => m.FieldName == "UserTag")
                        .Select(m => m.Value)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (var tag in deleteWork.MetadataFields.Where(m => m.FieldName == "UserTag" && !keepTags.Contains(m.Value)))
                    {
                        var newField = new WISE.Domain.Entities.MetadataField("UserTag", tag.Value, "User", true, 100);
                        newField.SetWorkId(keepWork.Id);
                        _dbContext.MetadataFields.Add(newField);
                        keepTags.Add(tag.Value);
                    }
                }

                if (req.DeleteFiles)
                    filePathsToDelete.AddRange(deleteWork.Assets.Select(a => a.FilePath).Where(p => !string.IsNullOrEmpty(p))!);

                var workTarget = $"Work_{deleteWork.Id}";
                _dbContext.Jobs.RemoveRange(await _dbContext.Jobs.Where(j => j.Target == workTarget).ToListAsync(ct));
                _dbContext.EventLogs.RemoveRange(await _dbContext.EventLogs.Where(e => e.TargetId == deleteWork.Id).ToListAsync(ct));
                _dbContext.Works.Remove(deleteWork);
            }

            await _dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        foreach (var deleteWork in deleteWorks)
            _logger.LogInformation("[Duplicates] Work {DeleteId} deleted (kept {KeepId}).", deleteWork.Id, req.KeepWorkId);

        // --- 物理ファイル削除（DBコミット後、ベストエフォート） ---
        int filesDeleted = 0, filesFailed = 0;
        if (req.DeleteFiles)
        {
            foreach (var path in filePathsToDelete)
            {
                try
                {
                    if (File.Exists(path)) { File.Delete(path); filesDeleted++; }
                    var dir = Path.GetDirectoryName(path);
                    if (dir != null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Duplicates] Failed to delete file: {Path}", path);
                    filesFailed++;
                }
            }
        }

        return (ResolveOutcome.Ok, filesDeleted, filesFailed);
    }
}
