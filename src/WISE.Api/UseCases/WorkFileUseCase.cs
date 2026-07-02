using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WISE.Infrastructure.Data;

namespace WISE.Api.UseCases;

/// <summary>
/// P1 リファクタリング Phase5: WorksController から WiseDbContext 直接参照を排除するための UseCase。
/// Work削除（物理ファイル削除含む）とファイルの場所を開く操作を担う。
/// </summary>
public class WorkFileUseCase
{
    private readonly WiseDbContext _dbContext;
    private readonly ILogger<WorkFileUseCase> _logger;

    public WorkFileUseCase(WiseDbContext dbContext, ILogger<WorkFileUseCase> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public enum DeleteResult { NotFound, FilesLocked, Ok }

    public async Task<(DeleteResult Result, int FilesDeleted, IReadOnlyList<string> LockedFiles)> DeleteWorkAsync(
        Guid workId, bool deleteFiles, CancellationToken ct = default)
    {
        var work = await _dbContext.Works
            .Include(w => w.Assets)
            .Include(w => w.MetadataFields)
            .FirstOrDefaultAsync(w => w.Id == workId, ct);

        if (work == null) return (DeleteResult.NotFound, 0, Array.Empty<string>());

        var filePaths = deleteFiles
            ? work.Assets.Select(a => a.FilePath).OfType<string>().ToList()
            : new List<string>();

        // 物理ファイル削除を DB 削除より先に試行する。
        // ロック中のファイル等で削除に失敗した場合は DB を変更せず中断し、
        // 呼び出し元がリトライできるようにする（孤立ファイル化を防ぐ）。
        if (deleteFiles)
        {
            var lockedPaths = new List<string>();
            foreach (var path in filePaths)
            {
                if (path == null || !File.Exists(path)) continue;
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "[Delete] File locked or inaccessible: {Path}", path);
                    lockedPaths.Add(path);
                }
            }

            if (lockedPaths.Count > 0)
                return (DeleteResult.FilesLocked, 0, lockedPaths);

            // 空になったフォルダを整理
            foreach (var path in filePaths)
            {
                if (path == null) continue;
                var dir = Path.GetDirectoryName(path);
                if (dir != null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    try { Directory.Delete(dir); }
                    catch (Exception ex) { _logger.LogWarning(ex, "[Delete] Failed to remove empty dir: {Dir}", dir); }
                }
            }
        }

        // DB から関連レコードをすべて削除
        var workTarget = $"Work_{workId}";
        var jobs = await _dbContext.Jobs.Where(j => j.Target == workTarget).ToListAsync(ct);
        var events = await _dbContext.EventLogs.Where(e => e.TargetId == workId).ToListAsync(ct);

        _dbContext.Jobs.RemoveRange(jobs);
        _dbContext.EventLogs.RemoveRange(events);
        _dbContext.Works.Remove(work); // MetadataFields と Assets は cascade delete

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("[Delete] Work {WorkId} ({Id}) deleted from DB.", workId, work.PrimaryIdentifier);

        return (DeleteResult.Ok, filePaths.Count, Array.Empty<string>());
    }

    public enum OpenFolderResult { WorkNotFound, NoAccessibleFile, Ok }

    public async Task<(OpenFolderResult Result, string? Path)> ResolveOpenableFilePathAsync(Guid workId, CancellationToken ct = default)
    {
        var work = await _dbContext.Works.Include(w => w.Assets).AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workId, ct);
        if (work == null) return (OpenFolderResult.WorkNotFound, null);

        var videoAsset = work.Assets.FirstOrDefault(a =>
            a.FilePath != null && (a.FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                || a.FilePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)));
        if (videoAsset?.FilePath != null && File.Exists(videoAsset.FilePath))
            return (OpenFolderResult.Ok, videoAsset.FilePath);

        var anyAsset = work.Assets.FirstOrDefault(a => !string.IsNullOrEmpty(a.FilePath));
        if (anyAsset?.FilePath != null && File.Exists(anyAsset.FilePath))
            return (OpenFolderResult.Ok, anyAsset.FilePath);

        return (OpenFolderResult.NoAccessibleFile, null);
    }
}
