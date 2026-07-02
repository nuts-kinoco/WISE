using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WISE.Infrastructure.Data;

namespace WISE.Api.UseCases;

/// <summary>
/// P1 リファクタリング Phase5: WorksController から WiseDbContext 直接参照を排除するための UseCase。
/// Favorite/Rating/Memo（ユーザー入力データ）の更新を担う。
/// </summary>
public class WorkUserDataUseCase
{
    private readonly WiseDbContext _dbContext;
    private readonly ILogger<WorkUserDataUseCase> _logger;

    public WorkUserDataUseCase(WiseDbContext dbContext, ILogger<WorkUserDataUseCase> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<bool> PatchUserDataAsync(Guid workId, bool favorite, int? rating, string? memo, CancellationToken ct = default)
    {
        var work = await _dbContext.Works
            .Include(w => w.Assets)
            .FirstOrDefaultAsync(w => w.Id == workId, ct);
        if (work == null) return false;

        work.SetFavorite(favorite);
        work.SetRating(rating);
        await _dbContext.SaveChangesAsync(ct);

        // metadata.json の userFavorite/userRating/userMemo も同期する
        var videoAsset = work.Assets.FirstOrDefault(a =>
            a.FilePath != null && (a.FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                || a.FilePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)));
        if (videoAsset?.FilePath != null)
        {
            var workDir = System.IO.Path.GetDirectoryName(videoAsset.FilePath)!;
            var metaJsonPath = System.IO.Path.Combine(workDir, "metadata.json");
            try
            {
                System.Collections.Generic.Dictionary<string, object?> meta;
                if (System.IO.File.Exists(metaJsonPath))
                {
                    var raw = await System.IO.File.ReadAllTextAsync(metaJsonPath, ct);
                    meta = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object?>>(raw)
                           ?? new System.Collections.Generic.Dictionary<string, object?>();
                }
                else
                {
                    meta = new System.Collections.Generic.Dictionary<string, object?>();
                }
                meta["userFavorite"] = favorite;
                meta["userRating"] = rating;
                meta["userMemo"] = memo ?? "";
                var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                await System.IO.File.WriteAllTextAsync(metaJsonPath, System.Text.Json.JsonSerializer.Serialize(meta, opts), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[UserData] Failed to update metadata.json for Work {WorkId}", workId);
            }
        }

        return true;
    }
}
