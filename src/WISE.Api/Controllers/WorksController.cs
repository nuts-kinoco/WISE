using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WISE.Api.UseCases;
using WISE.Application.Queries;
using WISE.Domain.Interfaces;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorksController : ControllerBase
    {
        private readonly IWorksQueryService _query;
        private readonly WorkUserDataUseCase _userDataUseCase;
        private readonly WorkMetadataUseCase _metadataUseCase;
        private readonly WorkCoverUseCase _coverUseCase;
        private readonly WorkFileUseCase _fileUseCase;
        private readonly ICoverProviderChain _coverChain;
        private readonly IEnumerable<IMediaViewer> _viewers;

        public WorksController(
            IWorksQueryService query,
            WorkUserDataUseCase userDataUseCase,
            WorkMetadataUseCase metadataUseCase,
            WorkCoverUseCase coverUseCase,
            WorkFileUseCase fileUseCase,
            ICoverProviderChain coverChain,
            IEnumerable<IMediaViewer> viewers)
        {
            _query = query;
            _userDataUseCase = userDataUseCase;
            _metadataUseCase = metadataUseCase;
            _coverUseCase = coverUseCase;
            _fileUseCase = fileUseCase;
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
            [FromQuery] string? sort = null,
            CancellationToken ct = default)
        {
            var (rawWorks, totalCount) = await _query.GetListAsync(page, pageSize, q, status, mediaType, sort, ct);

            // WorkItemMapper.Map と同一のロジックを使用する（ActressTag 優先処理を含む）。
            // 以前この一覧エンドポイントは独自の簡易マッピングを持っており、複数女優作品の
            // ActressTag フォールバックが適用されず「ライブラリで女優名が空になる」不具合があった。
            var works = rawWorks.Select(WorkItemMapper.Map).ToList();

            return Ok(new
            {
                TotalCount = totalCount,
                Page = Math.Max(1, page),
                PageSize = Math.Clamp(pageSize, 1, 100),
                Items = works
            });
        }

        private static string? ResolveMediaUrl(string? value, IEnumerable<WISE.Domain.Entities.Asset> assets)
            => WorkItemMapper.ResolveMediaUrl(value, assets);

        [HttpGet("{id}")]
        public async Task<IActionResult> GetWorkDetail(string id, CancellationToken ct)
        {
            if (!Guid.TryParse(id, out var workId))
                return BadRequest("Invalid Work ID format.");

            var detail = await _query.GetDetailAsync(workId, ct);
            if (detail == null) return NotFound();

            var work = detail.Work;

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
                UserMemo = detail.UserMemo,
                SampleImages = detail.SampleImages,
                CoverUrl = ResolveMediaUrl(
                    MetaFirstDetail("PortraitCover", "Cover") ?? $"/api/works/{work.Id}/cover",
                    work.Assets),
                CoverLandscapeUrl = ResolveMediaUrl(
                    MetaFirstDetail("LandscapeCover", "CoverLandscape"),
                    work.Assets),
                Metadata = work.MetadataFields.Select(m => new { m.FieldName, m.Value, m.IsPrimary, m.ProviderId, m.ConfidenceScore }),
                Assets = work.Assets.Select(a => new { a.Id, a.OriginalFilename, a.FileSize, a.Sha256, AssetType = a.AssetType.ToString() }),
                History = detail.History,
                Diagnostic = detail.Diagnostic
            });
        }

        public record UserDataDto(bool Favorite, int? Rating, string? Memo);

        [HttpPatch("{id}/user-data")]
        public async Task<IActionResult> PatchUserData(string id, [FromBody] UserDataDto dto, CancellationToken ct)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest("Invalid Work ID format.");

            var success = await _userDataUseCase.PatchUserDataAsync(workId, dto.Favorite, dto.Rating, dto.Memo, ct);
            if (!success) return NotFound();

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
        public async Task<IActionResult> PatchMetadata(string id, [FromBody] ManualMetadataDto dto, CancellationToken ct)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest("Invalid Work ID format.");

            var success = await _metadataUseCase.PatchManualMetadataAsync(
                workId,
                new WorkMetadataUseCase.ManualFields(
                    dto.Title, dto.Actress, dto.Maker, dto.Label, dto.Series, dto.ReleaseDate, dto.Genre, dto.Runtime),
                ct);
            if (!success) return NotFound();

            return Ok(new { id = workId, status = "Organized" });
        }

        /// <summary>
        /// .thumbnails/ 内の画像アセット（Thumbnail/SampleImage）一覧を返す。
        /// </summary>
        [HttpGet("{id}/thumbnail-assets")]
        public async Task<IActionResult> GetThumbnailAssets(string id, CancellationToken ct)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest("Invalid Work ID.");

            var assets = await _query.GetThumbnailAssetsAsync(workId, ct);
            if (assets == null) return NotFound();

            return Ok(assets);
        }

        /// <summary>
        /// .thumbnails/ 内の画像をポートレートカバーとして設定する。
        /// </summary>
        [HttpPost("{id}/set-cover")]
        public async Task<IActionResult> SetCover(string id, [FromBody] System.Text.Json.JsonElement body, CancellationToken ct)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest("Invalid Work ID.");
            string? assetIdStr = null;
            if (body.TryGetProperty("assetId", out var el)) assetIdStr = el.GetString();
            if (!Guid.TryParse(assetIdStr, out var assetId)) return BadRequest("assetId is required.");

            var (result, coverUrl) = await _coverUseCase.SetCoverAsync(workId, assetId, ct);
            return result switch
            {
                WorkCoverUseCase.SetCoverResult.WorkNotFound => NotFound(),
                WorkCoverUseCase.SetCoverResult.AssetNotFound => NotFound(new { Error = "Asset not found." }),
                _ => Ok(new { coverUrl }),
            };
        }

        /// <summary>
        /// 画像ファイルをD&Dアップロードし、PortraitCoverとして登録する。
        /// .thumbnails/ フォルダに保存し、Manualフィールドで即時カバーとして設定する。
        /// </summary>
        [HttpPost("{id}/upload-cover")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadCover(string id, IFormFile file, CancellationToken ct)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest("Invalid Work ID.");
            if (file == null || file.Length == 0) return BadRequest("No file provided.");

            var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" }.Contains(ext))
                return BadRequest("Unsupported image format.");

            var (result, assetId, url) = await _coverUseCase.UploadCoverAsync(workId, file, ct);
            return result switch
            {
                WorkCoverUseCase.UploadCoverResult.WorkNotFound => NotFound(),
                WorkCoverUseCase.UploadCoverResult.VideoAssetNotFound => StatusCode(500, new { Error = "Video asset not found." }),
                _ => Ok(new { assetId, url }),
            };
        }

        /// <summary>
        /// 動画ファイルの場所をエクスプローラーで開く（Windows専用）。
        /// </summary>
        [HttpPost("{id}/open-folder")]
        public async Task<IActionResult> OpenFolder(string id, CancellationToken ct)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest("Invalid Work ID format.");

            var (result, path) = await _fileUseCase.ResolveOpenableFilePathAsync(workId, ct);
            if (result == WorkFileUseCase.OpenFolderResult.WorkNotFound) return NotFound();
            if (result == WorkFileUseCase.OpenFolderResult.NoAccessibleFile)
                return NotFound(new { Error = "No accessible files found." });

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                return Ok(new { path });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        public record AddUserTagDto(string Value);

        [HttpPost("{id}/user-tags")]
        public async Task<IActionResult> AddUserTag(string id, [FromBody] AddUserTagDto dto, CancellationToken ct)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest();
            if (string.IsNullOrWhiteSpace(dto.Value)) return BadRequest("Tag value required");

            var result = await _metadataUseCase.AddUserTagAsync(workId, dto.Value, ct);
            return result switch
            {
                WorkMetadataUseCase.AddUserTagResult.NotFound => NotFound(),
                WorkMetadataUseCase.AddUserTagResult.AlreadyExists => Conflict("Tag already exists"),
                _ => Ok(new { fieldName = "UserTag", value = dto.Value.Trim() }),
            };
        }

        [HttpDelete("{id}/user-tags/{tagValue}")]
        public async Task<IActionResult> DeleteUserTag(string id, string tagValue, CancellationToken ct)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest();

            var success = await _metadataUseCase.DeleteUserTagAsync(workId, tagValue, ct);
            return success ? NoContent() : NotFound();
        }

        [HttpDelete("{id}/genre-tags/{tagValue}")]
        public async Task<IActionResult> DeleteGenreTag(string id, string tagValue, CancellationToken ct)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest();

            var success = await _metadataUseCase.DeleteGenreTagAsync(workId, tagValue, ct);
            return success ? NoContent() : NotFound();
        }

        /// <summary>
        /// Workとその関連データを削除する。
        /// deleteFiles=true の場合、ディスク上の物理ファイル（動画・カバー等）も削除する。
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteWork(string id, [FromQuery] bool deleteFiles = false, CancellationToken ct = default)
        {
            if (!Guid.TryParse(id, out var workId))
                return BadRequest("Invalid Work ID format.");

            var (result, filesDeleted, lockedFiles) = await _fileUseCase.DeleteWorkAsync(workId, deleteFiles, ct);
            return result switch
            {
                WorkFileUseCase.DeleteResult.NotFound => NotFound(),
                WorkFileUseCase.DeleteResult.FilesLocked => Conflict(new
                {
                    error = "一部のファイルが使用中のため削除できませんでした。ファイルを閉じてから再試行してください。",
                    lockedFiles,
                }),
                _ => Ok(new { deleted = true, filesDeleted }),
            };
        }

        [HttpGet("{id}/cover")]
        public async Task<IActionResult> GetCover(string id, CancellationToken ct)
        {
            if (!Guid.TryParse(id, out var workId))
                return BadRequest("Invalid Work ID format.");

            var work = await _query.GetWorkWithAssetsAsync(workId, ct);
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
        public async Task<IActionResult> GetViewerInfo(string id, CancellationToken ct)
        {
            if (!Guid.TryParse(id, out var workId))
                return BadRequest("Invalid Work ID format.");

            var work = await _query.GetForViewerInfoAsync(workId, ct);
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

            var related = await _query.GetRelatedAsync(workId, field, limit, ct);
            return Ok(related.Select(WorkItemMapper.Map));
        }

        [HttpGet("{id}/epub")]
        public async Task<IActionResult> GetEpub(string id, CancellationToken ct)
        {
            if (!Guid.TryParse(id, out var workId))
                return BadRequest("Invalid Work ID format.");

            var work = await _query.GetWorkWithAssetsAsync(workId, ct);
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
