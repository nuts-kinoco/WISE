using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WISE.Domain.Interfaces;
using WISE.Infrastructure.Data;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorksController : ControllerBase
    {
        private readonly WiseDbContext _dbContext;
        private readonly ILogger<WorksController> _logger;
        private readonly ICoverProviderChain _coverChain;
        private readonly IEnumerable<IMediaViewer> _viewers;

        public WorksController(WiseDbContext dbContext, ILogger<WorksController> logger,
            ICoverProviderChain coverChain, IEnumerable<IMediaViewer> viewers)
        {
            _dbContext = dbContext;
            _logger = logger;
            _coverChain = coverChain;
            _viewers = viewers;
        }

        [HttpGet]
        public async Task<IActionResult> GetWorks(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? q = null,
            [FromQuery] string? status = null,
            [FromQuery] string? mediaType = null,
            [FromQuery] string? sort = null)
        {
            var query = _dbContext.Works
                .AsNoTracking()
                .Include(w => w.MetadataFields)
                .Include(w => w.Assets)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                var statuses = status.Split(',')
                    .Select(s => Enum.TryParse<WISE.Domain.Enums.ProcessingStatus>(s.Trim(), true, out var ps) ? ps : (WISE.Domain.Enums.ProcessingStatus?)null)
                    .Where(ps => ps.HasValue)
                    .Select(ps => ps!.Value)
                    .ToList();
                if (statuses.Count > 0)
                    query = query.Where(w => statuses.Contains(w.Status));
            }

            if (!string.IsNullOrWhiteSpace(mediaType))
            {
                var types = mediaType.Split(',')
                    .Select(t => Enum.TryParse<WISE.Domain.Enums.MediaType>(t.Trim(), true, out var mt) ? mt : (WISE.Domain.Enums.MediaType?)null)
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
                    w.MetadataFields.Any(m => m.FieldName == "Title"      && m.Value != null && m.Value.ToLower().Contains(lowerQ)) ||
                    w.MetadataFields.Any(m => m.FieldName == "Maker"      && m.Value != null && m.Value.ToLower().Contains(lowerQ)) ||
                    w.MetadataFields.Any(m => m.FieldName == "Actress"    && m.Value != null && m.Value.ToLower().Contains(lowerQ)) ||
                    w.MetadataFields.Any(m => m.FieldName == "ActressTag" && m.Value != null && m.Value.ToLower().Contains(lowerQ)) ||
                    w.MetadataFields.Any(m => m.FieldName == "Label"      && m.Value != null && m.Value.ToLower().Contains(lowerQ)) ||
                    w.MetadataFields.Any(m => m.FieldName == "Genre"      && m.Value != null && m.Value.ToLower().Contains(lowerQ)) ||
                    w.MetadataFields.Any(m => m.FieldName == "Tag"        && m.Value != null && m.Value.ToLower().Contains(lowerQ))
                );
            }

            var totalCount = await query.CountAsync();

            IQueryable<WISE.Domain.Entities.Work> sorted = (sort ?? "added") switch
            {
                "rating"     => query.OrderByDescending(w => w.Rating == null ? -1 : (double)w.Rating)
                                     .ThenByDescending(w => w.CreatedAt),
                "title"      => query.OrderBy(w =>
                                     w.MetadataFields.Where(m => m.FieldName == "Title" && m.IsPrimary).Select(m => m.Value).FirstOrDefault()
                                     ?? w.MetadataFields.Where(m => m.FieldName == "Title").Select(m => m.Value).FirstOrDefault()),
                "identifier" => query.OrderBy(w => w.PrimaryIdentifier),
                "release"    => query.OrderByDescending(w =>
                                     w.MetadataFields.Where(m => m.FieldName == "ReleaseDate" && m.IsPrimary).Select(m => m.Value).FirstOrDefault()
                                     ?? w.MetadataFields.Where(m => m.FieldName == "ReleaseDate" || m.FieldName == "release_date").Select(m => m.Value).FirstOrDefault())
                                     .ThenByDescending(w => w.CreatedAt),
                "random"     => query.OrderBy(_ => EF.Functions.Random()),
                _            => query.OrderByDescending(w => w.CreatedAt), // "added" default
            };

            var rawWorks = await sorted
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var works = rawWorks.Select(w =>
            {
                string? MetaFirst(params string[] names)
                {
                    foreach (var name in names)
                    {
                        var v = w.MetadataFields.FirstOrDefault(m => m.FieldName == name && m.IsPrimary)?.Value
                             ?? w.MetadataFields.FirstOrDefault(m => m.FieldName == name)?.Value;
                        if (v != null) return v;
                    }
                    return null;
                }

                return new
                {
                    w.Id,
                    w.PrimaryIdentifier,
                    MediaType = w.MediaType.ToString(),
                    Title       = MetaFirst("Title"),
                    Actress     = MetaFirst("Actress", "actress"),
                    Maker       = MetaFirst("Maker", "maker"),
                    Label       = MetaFirst("Label", "label"),
                    ReleaseDate = MetaFirst("ReleaseDate", "release_date"),
                    // Comic-specific
                    Author      = MetaFirst("author", "Author", "Writer"),
                    Circle      = MetaFirst("circle", "Circle"),
                    PageCount   = MetaFirst("page_count", "PageCount"),
                    Language    = MetaFirst("language", "Language", "LanguageISO"),
                    CoverUrl = ResolveMediaUrl(
                        MetaFirst("PortraitCover", "Cover") ?? $"/api/works/{w.Id}/cover",
                        w.Assets),
                    CoverLandscapeUrl = ResolveMediaUrl(
                        MetaFirst("LandscapeCover", "CoverLandscape"),
                        w.Assets),
                    MetadataStatus = w.Status.ToString(),
                    w.Favorite,
                    w.Rating
                };
            }).ToList();

            return Ok(new
            {
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                Items = works
            });
        }

        private static string? ResolveMediaUrl(string? value, IEnumerable<WISE.Domain.Entities.Asset> assets)
            => WorkItemMapper.ResolveMediaUrl(value, assets);

        [HttpGet("{id}")]
        public async Task<IActionResult> GetWorkDetail(string id)
        {
            if (!System.Guid.TryParse(id, out var workId))
            {
                return BadRequest("Invalid Work ID format.");
            }

            var work = await _dbContext.Works
                .AsNoTracking()
                .Include(w => w.MetadataFields)
                .Include(w => w.Assets)
                .FirstOrDefaultAsync(w => w.Id == workId);

            if (work == null)
            {
                return NotFound();
            }

            var history = await _dbContext.EventLogs
                .Where(e => e.TargetId == workId)
                .OrderByDescending(e => e.OccurredAt)
                .Select(e => new { e.EventType, e.OccurredAt, e.Actor, e.Payload })
                .ToListAsync();

            var createEvent = history.FirstOrDefault(e => e.EventType == "Work Created");
            object? diagnostic = null;
            if (createEvent != null && !string.IsNullOrEmpty(createEvent.Payload))
            {
                try { diagnostic = System.Text.Json.JsonSerializer.Deserialize<object>(createEvent.Payload); } catch {}
            }

            // Try to read userMemo and sampleImages from metadata.json
            string? userMemo = null;
            List<string> sampleImages = new();
            var videoAsset = work.Assets.FirstOrDefault(a =>
                a.FilePath != null && (a.FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                    || a.FilePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)));
            if (videoAsset?.FilePath != null)
            {
                var metaJsonPath = Path.Combine(Path.GetDirectoryName(videoAsset.FilePath)!, "metadata.json");
                if (System.IO.File.Exists(metaJsonPath))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(metaJsonPath));
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

            string? MetaFirstDetail(params string[] names)
            {
                foreach (var name in names)
                {
                    var v = work.MetadataFields.FirstOrDefault(m => m.FieldName == name && m.IsPrimary)?.Value
                         ?? work.MetadataFields.FirstOrDefault(m => m.FieldName == name)?.Value;
                    if (v != null) return v;
                }
                return null;
            }

            return Ok(new
            {
                work.Id,
                work.PrimaryIdentifier,
                work.Favorite,
                work.Rating,
                UserMemo = userMemo,
                SampleImages = sampleImages,
                CoverUrl = ResolveMediaUrl(
                    MetaFirstDetail("PortraitCover", "Cover") ?? $"/api/works/{work.Id}/cover",
                    work.Assets),
                CoverLandscapeUrl = ResolveMediaUrl(
                    MetaFirstDetail("LandscapeCover", "CoverLandscape"),
                    work.Assets),
                Metadata = work.MetadataFields.Select(m => new { m.FieldName, m.Value, m.IsPrimary, m.ProviderId, m.ConfidenceScore }),
                Assets = work.Assets.Select(a => new { a.Id, a.OriginalFilename, a.FileSize, a.Sha256, AssetType = a.AssetType.ToString() }),
                History = history,
                Diagnostic = diagnostic
            });
        }

        public record UserDataDto(bool Favorite, int? Rating, string? Memo);

        [HttpPatch("{id}/user-data")]
        public async Task<IActionResult> PatchUserData(string id, [FromBody] UserDataDto dto)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest("Invalid Work ID format.");

            var work = await _dbContext.Works
                .Include(w => w.Assets)
                .FirstOrDefaultAsync(w => w.Id == workId);
            if (work == null) return NotFound();

            work.SetFavorite(dto.Favorite);
            work.SetRating(dto.Rating);
            await _dbContext.SaveChangesAsync();

            // Update metadata.json userFavorite/userRating/userMemo
            var videoAsset = work.Assets.FirstOrDefault(a =>
                a.FilePath != null && (a.FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                    || a.FilePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)));
            if (videoAsset?.FilePath != null)
            {
                var workDir = Path.GetDirectoryName(videoAsset.FilePath)!;
                var metaJsonPath = Path.Combine(workDir, "metadata.json");
                try
                {
                    Dictionary<string, object?> meta;
                    if (System.IO.File.Exists(metaJsonPath))
                    {
                        var raw = await System.IO.File.ReadAllTextAsync(metaJsonPath);
                        meta = JsonSerializer.Deserialize<Dictionary<string, object?>>(raw)
                               ?? new Dictionary<string, object?>();
                    }
                    else
                    {
                        meta = new Dictionary<string, object?>();
                    }
                    meta["userFavorite"] = dto.Favorite;
                    meta["userRating"] = dto.Rating;
                    meta["userMemo"] = dto.Memo ?? "";
                    var opts = new JsonSerializerOptions { WriteIndented = true };
                    await System.IO.File.WriteAllTextAsync(metaJsonPath, JsonSerializer.Serialize(meta, opts));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[UserData] Failed to update metadata.json for Work {WorkId}", workId);
                }
            }

            return Ok(new { favorite = dto.Favorite, rating = dto.Rating, memo = dto.Memo });
        }

        /// <summary>
        /// トリアージ用: Work のメタデータフィールドを手動で上書きする。
        /// 既存の同名 primary フィールドを置き換え、Work の status を Organized にする。
        /// </summary>
        public record ManualMetadataDto(
            string? Title, string? Actress, string? Maker, string? Label,
            string? Series, string? ReleaseDate, string? Genre, string? Runtime);

        [HttpPatch("{id}/metadata")]
        public async Task<IActionResult> PatchMetadata(string id, [FromBody] ManualMetadataDto dto)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest("Invalid Work ID format.");

            var work = await _dbContext.Works
                .Include(w => w.MetadataFields)
                .FirstOrDefaultAsync(w => w.Id == workId);
            if (work == null) return NotFound();

            var fields = new Dictionary<string, string?>
            {
                ["Title"]       = dto.Title,
                ["Actress"]     = dto.Actress,
                ["Maker"]       = dto.Maker,
                ["Label"]       = dto.Label,
                ["Series"]      = dto.Series,
                ["ReleaseDate"] = dto.ReleaseDate,
                ["Genre"]       = dto.Genre,
                ["Runtime"]     = dto.Runtime,
            };

            foreach (var (fieldName, value) in fields)
            {
                if (value is null) continue;

                // 既存 primary を非 primary に降格
                foreach (var existing in work.MetadataFields.Where(m => m.FieldName == fieldName && m.IsPrimary))
                    existing.SetPrimary(false);

                // Manual エントリを追加（最高優先度 999）
                var newField = new WISE.Domain.Entities.MetadataField(fieldName, value, "Manual", true, 999);
                newField.SetWorkId(work.Id);
                _dbContext.MetadataFields.Add(newField);
            }

            work.UpdateStatus(WISE.Domain.Enums.ProcessingStatus.Organized);
            await _dbContext.SaveChangesAsync();
            return Ok(new { id = workId, status = "Organized" });
        }

        /// <summary>
        /// .thumbnails/ 内の画像アセット（Thumbnail/SampleImage）一覧を返す。
        /// </summary>
        [HttpGet("{id}/thumbnail-assets")]
        public async Task<IActionResult> GetThumbnailAssets(string id)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest("Invalid Work ID.");
            var work = await _dbContext.Works.AsNoTracking().Include(w => w.Assets).Include(w => w.MetadataFields)
                .FirstOrDefaultAsync(w => w.Id == workId);
            if (work == null) return NotFound();

            var allowed = new[]
            {
                WISE.Domain.Enums.AssetType.PortraitCover,
                WISE.Domain.Enums.AssetType.LandscapeCover,
                WISE.Domain.Enums.AssetType.Thumbnail,
                WISE.Domain.Enums.AssetType.SampleImage,
            };

            // 現在のPortraitCover primary field の assetId を特定（Manualかどうか判定用）
            var currentCoverUrl = work.MetadataFields
                .Where(m => m.FieldName == "PortraitCover" && m.IsPrimary && m.ProviderId == "Manual")
                .Select(m => m.Value)
                .FirstOrDefault();

            var assets = work.Assets
                .Where(a => allowed.Contains(a.AssetType) && a.FilePath != null && System.IO.File.Exists(a.FilePath))
                .OrderBy(a => a.AssetType) // PortraitCover → LandscapeCover → Thumbnail → SampleImage
                .Select(a => new
                {
                    a.Id,
                    a.OriginalFilename,
                    AssetType = a.AssetType.ToString(),
                    Url = $"/api/assets/{a.Id}/content",
                    IsCurrentCover = currentCoverUrl != null && currentCoverUrl.Contains(a.Id.ToString()),
                })
                .ToList();
            return Ok(assets);
        }

        /// <summary>
        /// .thumbnails/ 内の画像をポートレートカバーとして設定する。
        /// </summary>
        [HttpPost("{id}/set-cover")]
        public async Task<IActionResult> SetCover(string id, [FromBody] System.Text.Json.JsonElement body)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest("Invalid Work ID.");
            string? assetIdStr = null;
            if (body.TryGetProperty("assetId", out var el)) assetIdStr = el.GetString();
            if (!Guid.TryParse(assetIdStr, out var assetId)) return BadRequest("assetId is required.");

            var work = await _dbContext.Works.Include(w => w.Assets).Include(w => w.MetadataFields)
                .FirstOrDefaultAsync(w => w.Id == workId);
            if (work == null) return NotFound();

            var asset = work.Assets.FirstOrDefault(a => a.Id == assetId);
            if (asset == null) return NotFound(new { Error = "Asset not found." });

            var assetApiUrl = $"/api/assets/{assetId}/content";

            // Demote all PortraitCover primary fields
            foreach (var f in work.MetadataFields.Where(m => m.FieldName == "PortraitCover" && m.IsPrimary))
                f.SetPrimary(false);

            // Check if the selected asset is a PortraitCover (= revert to original provider)
            var isOriginalCover = asset.AssetType == WISE.Domain.Enums.AssetType.PortraitCover;
            if (isOriginalCover)
            {
                // Remove any Manual PortraitCover field and promote the field whose value points to this asset
                var manualField = work.MetadataFields.FirstOrDefault(m => m.FieldName == "PortraitCover" && m.ProviderId == "Manual");
                if (manualField != null) _dbContext.MetadataFields.Remove(manualField);

                var originalField = work.MetadataFields.FirstOrDefault(m =>
                    m.FieldName == "PortraitCover" && m.ProviderId != "Manual" && m.Value == assetApiUrl);
                if (originalField != null)
                    originalField.SetPrimary(true);
                else
                {
                    // Promote highest confidence non-manual field
                    var best = work.MetadataFields
                        .Where(m => m.FieldName == "PortraitCover" && m.ProviderId != "Manual")
                        .OrderByDescending(m => m.ConfidenceScore).FirstOrDefault();
                    if (best != null) best.SetPrimary(true);
                }
            }
            else
            {
                // Add or update Manual PortraitCover pointing to thumbnail/sample
                var existing = work.MetadataFields.FirstOrDefault(m => m.FieldName == "PortraitCover" && m.ProviderId == "Manual");
                if (existing != null)
                {
                    existing.UpdateValue(assetApiUrl, 999, "Manual");
                    existing.SetPrimary(true);
                }
                else
                {
                    var newField = new WISE.Domain.Entities.MetadataField("PortraitCover", assetApiUrl, "Manual", true, 999);
                    newField.SetWorkId(workId);
                    _dbContext.MetadataFields.Add(newField);
                }
            }

            await _dbContext.SaveChangesAsync();
            return Ok(new { coverUrl = assetApiUrl });
        }

        /// <summary>
        /// 画像ファイルをD&Dアップロードし、PortraitCoverとして登録する。
        /// .thumbnails/ フォルダに保存し、Manualフィールドで即時カバーとして設定する。
        /// </summary>
        [HttpPost("{id}/upload-cover")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadCover(string id, IFormFile file)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest("Invalid Work ID.");
            if (file == null || file.Length == 0) return BadRequest("No file provided.");

            var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" }.Contains(ext))
                return BadRequest("Unsupported image format.");

            var work = await _dbContext.Works.Include(w => w.Assets).Include(w => w.MetadataFields)
                .FirstOrDefaultAsync(w => w.Id == workId);
            if (work == null) return NotFound();

            // .thumbnails/ フォルダをビデオアセット横に確保する
            var videoAsset = work.Assets.FirstOrDefault(a =>
                a.FilePath != null && (a.FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                    || a.FilePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)));
            if (videoAsset?.FilePath == null)
                return StatusCode(500, new { Error = "Video asset not found." });

            var coverDir = System.IO.Path.GetDirectoryName(videoAsset.FilePath) ?? string.Empty;
            var thumbDir = System.IO.Path.Combine(coverDir, ".thumbnails");
            System.IO.Directory.CreateDirectory(thumbDir);

            // ユニーク名でファイル保存
            var safeBase = Regex.Replace(System.IO.Path.GetFileNameWithoutExtension(file.FileName), @"[^\w\-]", "_");
            var newFileName = $"upload_{DateTime.UtcNow:yyyyMMddHHmmss}_{safeBase}{ext}";
            var destPath = System.IO.Path.Combine(thumbDir, newFileName);

            await using (var stream = System.IO.File.Create(destPath))
                await file.CopyToAsync(stream);

            // Asset レコード作成
            var assetId = Guid.NewGuid();
            var newAsset = new WISE.Domain.Entities.Asset(
                destPath, newFileName, new System.IO.FileInfo(destPath).Length, null, assetId,
                WISE.Domain.Enums.AssetType.PortraitCover);
            work.AddAsset(newAsset);

            var assetApiUrl = $"/api/assets/{assetId}/content";

            // 既存のPortraitCover primaryを降格
            foreach (var f in work.MetadataFields.Where(m => m.FieldName == "PortraitCover" && m.IsPrimary))
                f.SetPrimary(false);

            // Manual MetadataField として設定（confidence 999）
            var existingManual = work.MetadataFields.FirstOrDefault(m => m.FieldName == "PortraitCover" && m.ProviderId == "Manual");
            if (existingManual != null)
            {
                existingManual.UpdateValue(assetApiUrl, 999, "Manual");
                existingManual.SetPrimary(true);
            }
            else
            {
                var newField = new WISE.Domain.Entities.MetadataField("PortraitCover", assetApiUrl, "Manual", true, 999);
                newField.SetWorkId(workId);
                _dbContext.MetadataFields.Add(newField);
            }

            await _dbContext.SaveChangesAsync();
            return Ok(new { assetId, url = assetApiUrl });
        }

        /// <summary>
        /// 動画ファイルの場所をエクスプローラーで開く（Windows専用）。
        /// </summary>
        [HttpPost("{id}/open-folder")]
        public async Task<IActionResult> OpenFolder(string id)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest("Invalid Work ID format.");
            var work = await _dbContext.Works.Include(w => w.Assets).FirstOrDefaultAsync(w => w.Id == workId);
            if (work == null) return NotFound();

            string? pathToOpen = null;

            // ビデオファイルを優先
            var videoAsset = work.Assets.FirstOrDefault(a =>
                a.FilePath != null && (a.FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                    || a.FilePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)));
            if (videoAsset?.FilePath != null && System.IO.File.Exists(videoAsset.FilePath))
            {
                pathToOpen = videoAsset.FilePath;
            }
            else
            {
                // ビデオがない場合は任意のアセットを探す
                var anyAsset = work.Assets.FirstOrDefault(a => !string.IsNullOrEmpty(a.FilePath));
                if (anyAsset?.FilePath != null && System.IO.File.Exists(anyAsset.FilePath))
                {
                    pathToOpen = anyAsset.FilePath;
                }
            }

            if (string.IsNullOrEmpty(pathToOpen))
                return NotFound(new { Error = "No accessible files found." });

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{pathToOpen}\"");
                return Ok(new { path = pathToOpen });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        public record AddUserTagDto(string Value);

        [HttpPost("{id}/user-tags")]
        public async Task<IActionResult> AddUserTag(string id, [FromBody] AddUserTagDto dto)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest();
            if (string.IsNullOrWhiteSpace(dto.Value)) return BadRequest("Tag value required");

            var work = await _dbContext.Works
                .Include(w => w.MetadataFields)
                .FirstOrDefaultAsync(w => w.Id == workId);
            if (work == null) return NotFound();

            if (work.MetadataFields.Any(m => m.FieldName == "UserTag" && m.Value == dto.Value.Trim()))
                return Conflict("Tag already exists");

            var field = new WISE.Domain.Entities.MetadataField("UserTag", dto.Value.Trim(), "User", true, 100);
            field.SetWorkId(work.Id);
            _dbContext.MetadataFields.Add(field);
            await _dbContext.SaveChangesAsync();
            return Ok(new { fieldName = "UserTag", value = dto.Value.Trim() });
        }

        [HttpDelete("{id}/user-tags/{tagValue}")]
        public async Task<IActionResult> DeleteUserTag(string id, string tagValue)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest();

            var field = await _dbContext.MetadataFields
                .FirstOrDefaultAsync(m => m.WorkId == workId && m.FieldName == "UserTag" && m.Value == tagValue);
            if (field == null) return NotFound();

            _dbContext.MetadataFields.Remove(field);
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}/genre-tags/{tagValue}")]
        public async Task<IActionResult> DeleteGenreTag(string id, string tagValue)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest();

            var genreFields = await _dbContext.MetadataFields
                .Where(m => m.WorkId == workId && m.FieldName == "Genre")
                .ToListAsync();

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

            if (!changed) return NotFound();
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// Workとその関連データを削除する。
        /// deleteFiles=true の場合、ディスク上の物理ファイル（動画・カバー等）も削除する。
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteWork(string id, [FromQuery] bool deleteFiles = false)
        {
            if (!Guid.TryParse(id, out var workId))
                return BadRequest("Invalid Work ID format.");

            var work = await _dbContext.Works
                .Include(w => w.Assets)
                .Include(w => w.MetadataFields)
                .FirstOrDefaultAsync(w => w.Id == workId);

            if (work == null)
                return NotFound();

            var filePaths = deleteFiles
                ? work.Assets.Select(a => a.FilePath).OfType<string>().ToList()
                : new List<string>();

            // DB から関連レコードをすべて削除
            var workTarget = $"Work_{workId}";
            var jobs = await _dbContext.Jobs.Where(j => j.Target == workTarget).ToListAsync();
            var events = await _dbContext.EventLogs.Where(e => e.TargetId == workId).ToListAsync();

            _dbContext.Jobs.RemoveRange(jobs);
            _dbContext.EventLogs.RemoveRange(events);
            _dbContext.Works.Remove(work); // MetadataFields と Assets は cascade delete

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("[Delete] Work {WorkId} ({Id}) deleted from DB.", workId, work.PrimaryIdentifier);

            // 物理ファイル削除
            if (deleteFiles)
            {
                int deleted = 0, failed = 0;
                foreach (var path in filePaths)
                {
                    if (path == null) continue;
                    try
                    {
                        if (System.IO.File.Exists(path)) { System.IO.File.Delete(path); deleted++; }
                        // カバー画像等が入ったフォルダが空になったら削除
                        var dir = Path.GetDirectoryName(path);
                        if (dir != null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                            Directory.Delete(dir);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[Delete] Failed to delete file: {Path}", path);
                        failed++;
                    }
                }
                _logger.LogInformation("[Delete] Files: {Deleted} deleted, {Failed} failed.", deleted, failed);
                return Ok(new { deleted = true, filesDeleted = deleted, filesFailed = failed });
            }

            return Ok(new { deleted = true });
        }

        [HttpGet("{id}/cover")]
        public async Task<IActionResult> GetCover(string id)
        {
            if (!Guid.TryParse(id, out var workId))
                return BadRequest("Invalid Work ID format.");

            var work = await _dbContext.Works
                .Include(w => w.Assets)
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == workId);

            if (work == null) return NotFound();

            var result = await _coverChain.ResolveAsync(work);
            if (result == null || !System.IO.File.Exists(result.FilePath))
                return NotFound();

            // ETag = ファイルの最終更新時刻をハッシュとして使用
            // RFC 7232: ETag / Cache-Control は 304 応答にも含める必要があるため、
            // ヘッダーを先にセットしてから If-None-Match チェックを行う。
            var fileInfo = new System.IO.FileInfo(result.FilePath);
            var etag = $"\"{fileInfo.LastWriteTimeUtc.Ticks:x}\"";
            Response.Headers["ETag"] = etag;
            Response.Headers["Cache-Control"] = "public, max-age=86400";

            if (Request.Headers.TryGetValue("If-None-Match", out var inm) && inm == etag)
                return StatusCode(304);

            var stream = System.IO.File.OpenRead(result.FilePath);
            return File(stream, result.ContentType, enableRangeProcessing: false);
        }

        [HttpGet("{id}/viewer-info")]
        public async Task<IActionResult> GetViewerInfo(string id)
        {
            if (!Guid.TryParse(id, out var workId))
                return BadRequest("Invalid Work ID format.");

            var work = await _dbContext.Works
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == workId);

            if (work == null) return NotFound();

            var viewer = _viewers.FirstOrDefault(v => v.MediaType == work.MediaType);
            if (viewer == null)
                return Ok(new { viewerType = "None", route = (string?)null, capabilities = (object?)null });

            var route = viewer.GetViewerRoute(work);
            return Ok(new
            {
                viewerType = route.ViewerType,
                route = route.RouteTemplate,
                capabilities = new
                {
                    supportsPageNavigation = route.Capabilities.SupportsPageNavigation,
                    supportsDoublePage = route.Capabilities.SupportsDoublePage,
                    supportsPrefetch = route.Capabilities.SupportsPrefetch,
                    supportsTimeSeek = route.Capabilities.SupportsTimeSeek,
                    supportsResume = route.Capabilities.SupportsResume,
                }
            });
        }

        [HttpGet("{id}/related")]
        public async Task<IActionResult> GetRelated(
            string id,
            [FromQuery] string? field = null,
            [FromQuery] int limit = 8,
            CancellationToken ct = default)
        {
            if (!Guid.TryParse(id, out var workId))
                return BadRequest("Invalid Work ID format.");

            // Determine which fields to search for related works
            var targetFields = string.IsNullOrWhiteSpace(field)
                ? new[] { "Actress", "ActressTag", "Series", "Circle", "Author", "Maker" }
                : new[] { field };

            // Get values of those fields from the source work
            var sourceValues = await _dbContext.MetadataFields
                .AsNoTracking()
                .Where(m => m.WorkId == workId && targetFields.Contains(m.FieldName) && m.Value != null && m.Value != "")
                .Select(m => new { m.FieldName, m.Value })
                .ToListAsync(ct);

            if (sourceValues.Count == 0)
                return Ok(new object[0]);

            var fieldNames = sourceValues.Select(v => v.FieldName).Distinct().ToList();
            var values     = sourceValues.Select(v => v.Value).Distinct().ToList();

            var related = await _dbContext.Works
                .AsNoTracking()
                .Include(w => w.MetadataFields)
                .Include(w => w.Assets)
                .Where(w => w.Id != workId
                    && w.MetadataFields.Any(m => fieldNames.Contains(m.FieldName) && values.Contains(m.Value)))
                .OrderByDescending(w => w.CreatedAt)
                .Take(limit)
                .ToListAsync(ct);

            return Ok(related.Select(WorkItemMapper.Map));
        }

        [HttpGet("{id}/epub")]
        public async Task<IActionResult> GetEpub(string id, CancellationToken ct)
        {
            if (!Guid.TryParse(id, out var workId))
                return BadRequest("Invalid Work ID format.");

            var work = await _dbContext.Works
                .AsNoTracking()
                .Include(w => w.Assets)
                .FirstOrDefaultAsync(w => w.Id == workId, ct);

            if (work == null) return NotFound();

            var epubAsset = work.Assets.FirstOrDefault(a =>
                System.IO.Path.GetExtension(a.FilePath).Equals(".epub", StringComparison.OrdinalIgnoreCase)
                && System.IO.File.Exists(a.FilePath));

            if (epubAsset == null) return NotFound("No EPUB asset found for this work.");

            var stream = System.IO.File.OpenRead(epubAsset.FilePath);
            Response.Headers["Content-Disposition"] =
                $"inline; filename=\"{System.IO.Path.GetFileName(epubAsset.FilePath)}\"";
            return File(stream, "application/epub+zip", enableRangeProcessing: true);
        }
    }
}
