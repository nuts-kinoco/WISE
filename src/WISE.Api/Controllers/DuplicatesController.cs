using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WISE.Infrastructure.Data;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/duplicates")]
    public class DuplicatesController : ControllerBase
    {
        private readonly WiseDbContext _dbContext;
        private readonly ILogger<DuplicatesController> _logger;

        public DuplicatesController(WiseDbContext dbContext, ILogger<DuplicatesController> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// 重複作品グループを返す。
        /// detectionType: "identifier" = PrimaryIdentifier完全一致, "title" = タイトル正規化一致
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetDuplicates()
        {
            var works = await _dbContext.Works
                .AsNoTracking()
                .Include(w => w.MetadataFields)
                .Include(w => w.Assets)
                .ToListAsync();

            var seen = new HashSet<Guid>();
            var result = new List<object>();

            // 1. PrimaryIdentifier 完全一致
            var identifierGroups = works
                .Where(w => w.PrimaryIdentifier != null)
                .GroupBy(w => w.PrimaryIdentifier!.ToUpperInvariant())
                .Where(g => g.Count() >= 2);

            foreach (var g in identifierGroups.OrderBy(g => g.Key))
            {
                var ids = g.Select(w => w.Id).ToList();
                foreach (var id in ids) seen.Add(id);
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

            return Ok(result);
        }

        private static string? NormalizeTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            // 小文字化・記号除去・空白正規化
            var s = title.ToLowerInvariant();
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[\s　]+", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[【】「」『』（）()【】\[\]!！?？。、,\.…・★☆♥♡◆◇■□▲△▼▽]", "");
            return s.Trim();
        }

        private static object BuildGroup(string key, string detectionType, IEnumerable<WISE.Domain.Entities.Work> works)
        {
            return new
            {
                identifier = key,
                detectionType,
                works = works.Select(w => new
                {
                    w.Id,
                    w.PrimaryIdentifier,
                    Status = w.Status.ToString(),
                    Title = w.MetadataFields.FirstOrDefault(m => m.FieldName == "Title" && m.IsPrimary)?.Value
                         ?? w.MetadataFields.FirstOrDefault(m => m.FieldName == "Title")?.Value,
                    Actress = w.MetadataFields.FirstOrDefault(m => m.FieldName == "Actress" && m.IsPrimary)?.Value
                           ?? w.MetadataFields.FirstOrDefault(m => m.FieldName == "Actress")?.Value,
                    Maker = w.MetadataFields.FirstOrDefault(m => m.FieldName == "Maker" && m.IsPrimary)?.Value
                         ?? w.MetadataFields.FirstOrDefault(m => m.FieldName == "Maker")?.Value,
                    w.Favorite,
                    w.Rating,
                    UserMemo = GetUserMemo(w.Assets),
                    Assets = w.Assets.Select(a => new
                    {
                        a.Id,
                        a.OriginalFilename,
                        a.FileSize,
                        AssetType = a.AssetType.ToString()
                    }).ToList()
                }).ToList()
            };
        }

        private static string? GetUserMemo(IEnumerable<WISE.Domain.Entities.Asset> assets)
        {
            var videoAsset = assets.FirstOrDefault(a =>
                a.FilePath != null && (a.FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                    || a.FilePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)));
            if (videoAsset?.FilePath == null) return null;

            var metaJsonPath = Path.Combine(Path.GetDirectoryName(videoAsset.FilePath)!, "metadata.json");
            if (!System.IO.File.Exists(metaJsonPath)) return null;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(metaJsonPath));
                if (doc.RootElement.TryGetProperty("userMemo", out var memoEl))
                    return memoEl.GetString();
            }
            catch { }
            return null;
        }

        public record ResolveRequest(
            Guid KeepWorkId,
            Guid[] DeleteWorkIds,        // 複数削除対応（3件以上の重複グループに対応）
            bool DeleteFiles,
            bool MergeRating,
            bool MergeMemo,
            bool MergeUserTags,
            bool MergeFavorite
        );

        /// <summary>
        /// 重複を解決する: keepWork を保持し、DeleteWorkIds を全て削除する。
        /// 3件以上の重複グループにも対応。
        /// </summary>
        [HttpPost("resolve")]
        public async Task<IActionResult> Resolve([FromBody] ResolveRequest req)
        {
            if (req.DeleteWorkIds == null || req.DeleteWorkIds.Length == 0)
                return BadRequest(new { Error = "DeleteWorkIds must not be empty." });

            var keepWork = await _dbContext.Works
                .Include(w => w.Assets)
                .Include(w => w.MetadataFields)
                .FirstOrDefaultAsync(w => w.Id == req.KeepWorkId);
            if (keepWork == null) return NotFound(new { Error = "keepWork not found." });

            int filesDeleted = 0, filesFailed = 0;

            foreach (var deleteWorkId in req.DeleteWorkIds)
            {
                var deleteWork = await _dbContext.Works
                    .Include(w => w.Assets)
                    .Include(w => w.MetadataFields)
                    .FirstOrDefaultAsync(w => w.Id == deleteWorkId);
                if (deleteWork == null) continue;

                // Rating: keepWork が null の場合のみ引き継ぐ
                if (req.MergeRating && keepWork.Rating == null && deleteWork.Rating != null)
                    keepWork.SetRating(deleteWork.Rating);

                // Favorite: 削除される側が true なら引き継ぐ（片方でもお気に入りなら残す）
                if (req.MergeFavorite && !keepWork.Favorite && deleteWork.Favorite)
                    keepWork.SetFavorite(true);

                // Memo: keepWork が空の場合のみ引き継ぐ
                if (req.MergeMemo)
                {
                    var keepMemo = GetUserMemo(keepWork.Assets);
                    var deleteMemo = GetUserMemo(deleteWork.Assets);
                    if (string.IsNullOrWhiteSpace(keepMemo) && !string.IsNullOrWhiteSpace(deleteMemo))
                        await WriteUserMemo(keepWork.Assets, deleteMemo);
                }

                // UserTags: 重複なしで移す
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

                await _dbContext.SaveChangesAsync();

                var filePaths = req.DeleteFiles
                    ? deleteWork.Assets.Select(a => a.FilePath).Where(p => !string.IsNullOrEmpty(p)).ToList()
                    : [];

                var workTarget = $"Work_{deleteWorkId}";
                _dbContext.Jobs.RemoveRange(await _dbContext.Jobs.Where(j => j.Target == workTarget).ToListAsync());
                _dbContext.EventLogs.RemoveRange(await _dbContext.EventLogs.Where(e => e.TargetId == deleteWorkId).ToListAsync());
                _dbContext.Works.Remove(deleteWork);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("[Duplicates] Work {DeleteId} deleted (kept {KeepId}).", deleteWorkId, req.KeepWorkId);

                if (req.DeleteFiles)
                {
                    foreach (var path in filePaths)
                    {
                        if (path == null) continue;
                        try
                        {
                            if (System.IO.File.Exists(path)) { System.IO.File.Delete(path); filesDeleted++; }
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
            }

            return Ok(new { resolved = true, filesDeleted, filesFailed });
        }

        private static async Task WriteUserMemo(IEnumerable<WISE.Domain.Entities.Asset> assets, string memo)
        {
            var videoAsset = assets.FirstOrDefault(a =>
                a.FilePath != null && (a.FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                    || a.FilePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)));
            if (videoAsset?.FilePath == null) return;

            var workDir = Path.GetDirectoryName(videoAsset.FilePath)!;
            var metaJsonPath = Path.Combine(workDir, "metadata.json");
            try
            {
                Dictionary<string, object?> meta;
                if (System.IO.File.Exists(metaJsonPath))
                {
                    var raw = await System.IO.File.ReadAllTextAsync(metaJsonPath);
                    meta = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(raw)
                           ?? new Dictionary<string, object?>();
                }
                else
                {
                    meta = new Dictionary<string, object?>();
                }
                meta["userMemo"] = memo;
                var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                await System.IO.File.WriteAllTextAsync(metaJsonPath, System.Text.Json.JsonSerializer.Serialize(meta, opts));
            }
            catch { }
        }
    }
}
