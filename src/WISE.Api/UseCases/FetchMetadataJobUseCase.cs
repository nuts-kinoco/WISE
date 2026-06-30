using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;
using WISE.Domain.Entities;
using WISE.Application.Services;
using WISE.Infrastructure.Services;
using WISE.Infrastructure.Data;
using System.IO;

namespace WISE.Api.UseCases;

public class FetchMetadataJobUseCase
{
    private readonly IWorkRepository _workRepository;
    private readonly MetadataService _metadataService;
    private readonly IMetadataConflictResolver _conflictResolver;
    private readonly WISE.Domain.SeedWork.IUnitOfWork _unitOfWork;
    private readonly FFmpegThumbnailService _ffmpegService;
    private readonly IOutputPathResolver _outputPathResolver;
    private readonly WiseDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FetchMetadataJobUseCase> _logger;

    public FetchMetadataJobUseCase(
        IWorkRepository workRepository,
        MetadataService metadataService,
        IMetadataConflictResolver conflictResolver,
        WISE.Domain.SeedWork.IUnitOfWork unitOfWork,
        FFmpegThumbnailService ffmpegService,
        IOutputPathResolver outputPathResolver,
        WiseDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        ILogger<FetchMetadataJobUseCase> logger)
    {
        _workRepository = workRepository ?? throw new ArgumentNullException(nameof(workRepository));
        _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
        _conflictResolver = conflictResolver ?? throw new ArgumentNullException(nameof(conflictResolver));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
        _outputPathResolver = outputPathResolver ?? throw new ArgumentNullException(nameof(outputPathResolver));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> ExecuteAsync(Guid workId, CancellationToken cancellationToken = default)
    {
        var work = await _workRepository.GetByIdAsync(workId, cancellationToken);
        if (work == null)
            throw new Exception($"Work with ID {workId} not found.");

        // 孤立アセット（ディスク上に存在しないファイル）を DB から削除
        var orphanAssets = work.Assets
            .Where(a => a.FilePath != null
                     && !a.FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                     && !a.FilePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)
                     && !File.Exists(a.FilePath))
            .ToList();
        if (orphanAssets.Count > 0)
        {
            _dbContext.Set<Asset>().RemoveRange(orphanAssets);
            await _dbContext.SaveChangesAsync(cancellationToken);
            // Reload work so _assets list is in sync
            work = await _workRepository.GetByIdAsync(workId, cancellationToken)
                   ?? throw new Exception($"Work {workId} not found after orphan cleanup.");
            _logger.LogInformation("[AssetCleanup] Removed {Count} orphan assets for Work {WorkId}", orphanAssets.Count, workId);
        }

        work.UpdateStatus(ProcessingStatus.MetadataFetching);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var context = new MetadataProviderContext(work.Id, work.PrimaryIdentifier ?? "", work.MetadataFields, "ja", cancellationToken);

        // 1. Fetch candidates from all providers
        var metadataResults = await _metadataService.CollectResultsAsync(context);

        // Record provider diagnostics
        foreach (var result in metadataResults)
        {
            var diagnostic = await _dbContext.ProviderDiagnostics
                .FirstOrDefaultAsync(d => d.ProviderId == result.ProviderName, cancellationToken);

            if (diagnostic == null)
            {
                diagnostic = new ProviderDiagnostic(result.ProviderName, result.Strategy);
                _dbContext.ProviderDiagnostics.Add(diagnostic);
            }

            if (result.Success)
                diagnostic.RecordSuccess((long)result.Elapsed.TotalMilliseconds);
            else
                diagnostic.RecordFailure((long)result.Elapsed.TotalMilliseconds, result.FailureReason);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        var allCandidates = metadataResults.SelectMany(r => r.Candidates);

        if (!allCandidates.Any())
        {
            _logger.LogWarning("No metadata candidates found for Work {WorkId}", work.Id);
            work.UpdateStatus(ProcessingStatus.NotFound);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // 2. Conflict resolution
        var resolvedCandidates = _conflictResolver.Resolve(allCandidates).ToList();

        // 3. Download PortraitCover / LandscapeCover from remote URLs to local disk
        //    複数候補を信頼度順に試し、最小解像度(MinCoverFileSizeBytes)を満たす最初のURLを採用する。
        //    Store /api/assets/{id}/content URL in MetadataField (not local path) so frontend uses directly.
        const int MinCoverFileSizeBytes = 20_000; // 20KB 未満は低解像度として却下
        var coverFields = new[] { "PortraitCover", "LandscapeCover" };
        var coverDir = DetermineWorkDirectory(work);
        if (coverDir != null)
        {
            foreach (var fieldName in coverFields)
            {
                // 信頼度の高い順に候補を取り出す
                var coverCandidates = resolvedCandidates
                    .Where(c => c.Candidate.FieldName == fieldName
                             && c.Candidate.Value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(c => c.Candidate.Confidence)
                    .ToList();

                if (coverCandidates.Count == 0) continue;

                // すべての候補を resolvedCandidates から一旦除去（勝者だけ追加し直す）
                foreach (var c in coverCandidates) resolvedCandidates.Remove(c);

                var suffix = fieldName == "LandscapeCover" ? "pl" : "ps";
                var fileName = $"{work.PrimaryIdentifier?.ToLower()}_{suffix}.jpg";
                var localPath = Path.Combine(coverDir, fileName);

                bool downloaded = false;
                foreach (var candidate in coverCandidates)
                {
                    try
                    {
                        var ok = await DownloadFileAsync(candidate.Candidate.Value, localPath,
                            cancellationToken, MinCoverFileSizeBytes,
                            referer: candidate.Candidate.SourceUrl);
                        if (!ok)
                        {
                            // 小さすぎる / 404 → 既存ファイルがあれば削除して次 URL を試す
                            if (File.Exists(localPath)) File.Delete(localPath);
                            continue;
                        }

                        var assetType = fieldName == "LandscapeCover" ? AssetType.LandscapeCover : AssetType.PortraitCover;
                        var existingCoverAsset = work.Assets.FirstOrDefault(a =>
                            string.Equals(a.FilePath, localPath, StringComparison.OrdinalIgnoreCase));
                        var assetId = existingCoverAsset?.Id ?? Guid.NewGuid();
                        if (existingCoverAsset == null)
                        {
                            var coverAsset = new Asset(localPath, fileName, new FileInfo(localPath).Length, null, assetId, assetType);
                            work.AddAsset(coverAsset);
                        }

                        var assetApiUrl = $"/api/assets/{assetId}/content";
                        resolvedCandidates.Add(new ResolvedMetadataCandidate(
                            new MetadataCandidate(candidate.Candidate.ProviderId, fieldName,
                                assetApiUrl, candidate.Candidate.Confidence, candidate.Candidate.Priority,
                                SourceUrl: candidate.Candidate.SourceUrl),
                            true));

                        var eventType = fieldName == "PortraitCover"
                            ? "Portrait Cover Downloaded" : "Landscape Cover Downloaded";
                        _dbContext.EventLogs.Add(new EventLog(work.Id, eventType, "System", "FetchMetadataJobUseCase",
                            payload: $"{assetApiUrl} (from {candidate.Candidate.Value})"));

                        _logger.LogInformation("[Cover] {Field} ← {Url} → {Path}",
                            fieldName, candidate.Candidate.Value, localPath);
                        downloaded = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[Cover] Failed {Url}", candidate.Candidate.Value);
                        if (File.Exists(localPath)) try { File.Delete(localPath); } catch { }
                    }
                }

                if (!downloaded)
                    _logger.LogWarning("[Cover] All {Count} candidates failed for {Field}", coverCandidates.Count, fieldName);
            }
        }

        // 3.5. Sample images — only when setting downloadSampleImages = true
        var downloadSamples = false;
        var sampleSetting = await _dbContext.AppSettings.FindAsync(new object[] { "downloadSampleImages" }, cancellationToken);
        if (sampleSetting != null)
            bool.TryParse(sampleSetting.Value, out downloadSamples);

        var downloadedSampleUrls = new List<string>(); // /api/assets/{id}/content URLs for metadata.json

        if (downloadSamples && coverDir != null)
        {
            const int MaxSamples = 10;
            var sampleCandidates = resolvedCandidates
                .Where(c => c.Candidate.FieldName == "SampleImage"
                         && c.Candidate.Value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                .Take(MaxSamples)
                .ToList();

            // Remove SampleImage candidates from resolvedCandidates (we replace with local asset URLs)
            foreach (var sc in sampleCandidates) resolvedCandidates.Remove(sc);

            var thumbnailsDir = Path.Combine(coverDir, ".thumbnails");
            Directory.CreateDirectory(thumbnailsDir);

            int idx = 0;
            foreach (var sc in sampleCandidates)
            {
                var ext = Path.GetExtension(new Uri(sc.Candidate.Value).AbsolutePath);
                if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                var sampleFileName = $"sample_{idx:D2}{ext}";
                var samplePath = Path.Combine(thumbnailsDir, sampleFileName);
                try
                {
                    var ok = await DownloadFileAsync(sc.Candidate.Value, samplePath, cancellationToken, minFileSizeBytes: 5_000);
                    if (!ok) continue;

                    var existingSample = work.Assets.FirstOrDefault(a =>
                        string.Equals(a.FilePath, samplePath, StringComparison.OrdinalIgnoreCase));
                    var sampleAssetId = existingSample?.Id ?? Guid.NewGuid();
                    if (existingSample == null)
                        work.AddAsset(new Asset(samplePath, sampleFileName, new FileInfo(samplePath).Length, null, sampleAssetId, AssetType.SampleImage));

                    var sampleApiUrl = $"/api/assets/{sampleAssetId}/content";
                    downloadedSampleUrls.Add(sampleApiUrl);

                    resolvedCandidates.Add(new ResolvedMetadataCandidate(
                        new MetadataCandidate(sc.Candidate.ProviderId, "SampleImage",
                            sampleApiUrl, sc.Candidate.Confidence, sc.Candidate.Priority,
                            SourceUrl: sc.Candidate.SourceUrl),
                        true));

                    _logger.LogInformation("[Sample] {Url} → {Path}", sc.Candidate.Value, samplePath);
                    idx++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Sample] Failed to download {Url}", sc.Candidate.Value);
                }
            }
        }

        // 4. FFmpeg thumbnail — only when no PortraitCover at all
        bool hasCover = resolvedCandidates.Any(c => c.Candidate.FieldName == "PortraitCover");
        if (!hasCover && work.Assets.Any())
        {
            try
            {
                var videoAsset = work.Assets.FirstOrDefault(a =>
                    a.FilePath != null && (a.FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                       || a.FilePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)));
                if (videoAsset?.FilePath != null)
                {
                    var thumbnailsDir = Path.Combine(Path.GetDirectoryName(videoAsset.FilePath) ?? string.Empty, ".thumbnails");
                    var thumbnailPath = await _ffmpegService.GenerateThumbnailAsync(videoAsset.FilePath, thumbnailsDir);

                    // 同じパスのAssetが既に存在する場合は再利用してIDを固定する
                    var existingThumb = work.Assets.FirstOrDefault(a =>
                        string.Equals(a.FilePath, thumbnailPath, StringComparison.OrdinalIgnoreCase));
                    var thumbAssetId = existingThumb?.Id ?? Guid.NewGuid();
                    if (existingThumb == null)
                        work.AddAsset(new Asset(thumbnailPath, "thumbnail.jpg", new FileInfo(thumbnailPath).Length, null, thumbAssetId, AssetType.Thumbnail));

                    resolvedCandidates.Add(new ResolvedMetadataCandidate(
                        new MetadataCandidate("FFmpeg", "PortraitCover", $"/api/assets/{thumbAssetId}/content", 50, 50, "local"),
                        true));

                    _dbContext.EventLogs.Add(new EventLog(work.Id, "Thumbnail Generated", "System", "FetchMetadataJobUseCase"));
                    _logger.LogInformation("Generated fallback thumbnail via FFmpeg for Work {WorkId}", work.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate thumbnail for Work {WorkId}", work.Id);
            }
        }

        // 5. Multiple-actress normalization (before ApplyResolvedMetadata):
        //   1名確定 → Actress フィールドをそのまま使用
        //   2名以上 → 全員を ActressTag に昇格し、Actress は空文字マーカーで上書き
        var actressCandidates = resolvedCandidates
            .Where(c => c.Candidate.FieldName == "Actress")
            .Select(c => c.Candidate.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (actressCandidates.Count > 1)
        {
            var toRemove = resolvedCandidates.Where(c => c.Candidate.FieldName == "Actress").ToList();
            foreach (var r in toRemove) resolvedCandidates.Remove(r);

            // 空文字 primary → ApplyResolvedMetadata の primary-guarantee が古い Actress を浮上させない
            resolvedCandidates.Add(new ResolvedMetadataCandidate(
                new MetadataCandidate("System", "Actress", "", 999, 999), true));

            foreach (var name in actressCandidates)
                resolvedCandidates.Add(new ResolvedMetadataCandidate(
                    new MetadataCandidate("System", "ActressTag", name, 80, 100), true));

            _logger.LogInformation("[MultiActress] {Count} actresses → ActressTag. Actress cleared.", actressCandidates.Count);
        }

        // 5.5. Apply resolved metadata to Work entity
        work.ApplyResolvedMetadata(resolvedCandidates);

        // 5.5b. Reorganize files by actress name (LibraryRoot/女優名/識別子/ファイル名)
        //        複数女優の場合は「複数女優」フォルダ
        string? primaryActress;
        if (actressCandidates.Count > 1)
            primaryActress = "複数女優";
        else
            primaryActress = resolvedCandidates
                .FirstOrDefault(c => c.Candidate.FieldName == "Actress" && c.IsPrimary && !string.IsNullOrEmpty(c.Candidate.Value))
                ?.Candidate.Value;

        if (!string.IsNullOrWhiteSpace(primaryActress))
            ReorganizeByActress(work, primaryActress);

        // 5.6. Write metadata.json / movie.nfo / page.html to work directory
        var workDirAfterOrganize = DetermineWorkDirectory(work);
        if (workDirAfterOrganize != null)
            await WriteMetadataFilesAsync(work, workDirAfterOrganize, resolvedCandidates, metadataResults, downloadedSampleUrls, cancellationToken);

        // 6. Validate required fields
        bool hasTitle = resolvedCandidates.Any(c => c.Candidate.FieldName == "Title" && c.IsPrimary);
        // 1名の場合は Actress、複数の場合は ActressTag（Actress は空文字マーカー）
        bool hasActress = resolvedCandidates.Any(c => c.Candidate.FieldName == "Actress" && c.IsPrimary && !string.IsNullOrEmpty(c.Candidate.Value))
                       || resolvedCandidates.Any(c => c.Candidate.FieldName == "ActressTag" && c.IsPrimary);
        bool hasMaker = resolvedCandidates.Any(c => c.Candidate.FieldName == "Maker" && c.IsPrimary);
        bool hasPortraitCover = resolvedCandidates.Any(c => c.Candidate.FieldName == "PortraitCover");
        bool hasLandscapeCover = resolvedCandidates.Any(c => c.Candidate.FieldName == "LandscapeCover");
        bool hasIdentifier = !string.IsNullOrWhiteSpace(work.PrimaryIdentifier);

        // Actress / Maker / LandscapeCover は任意（FC2-PPV では構造的に取得できないケースが多い）。
        // 必須: Title + PortraitCover + Identifier
        bool isComplete = hasTitle && hasPortraitCover && hasIdentifier;

        if (isComplete)
        {
            work.UpdateStatus(ProcessingStatus.Organizing);
            _dbContext.EventLogs.Add(new EventLog(work.Id, "Metadata Fetched", "System", "FetchMetadataJobUseCase",
                payload: $"Title={hasTitle} Actress={hasActress} Maker={hasMaker} PortraitCover={hasPortraitCover} LandscapeCover={hasLandscapeCover}"));
        }
        else
        {
            _logger.LogWarning("[Validation] Metadata incomplete. Title={T} Actress={A} Maker={M} PortraitCover={PC} LandscapeCover={LC}",
                hasTitle, hasActress, hasMaker, hasPortraitCover, hasLandscapeCover);
            work.UpdateStatus(ProcessingStatus.Failed);
        }

        // 7. Save
        await _workRepository.UpdateAsync(work, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (!isComplete)
        {
            throw new InvalidOperationException(
                $"Metadata incomplete — Title:{hasTitle} Maker:{hasMaker} PortraitCover:{hasPortraitCover} (Actress:{hasActress} LandscapeCover:{hasLandscapeCover} optional)");
        }

        return $"Fetched {resolvedCandidates.Count} metadata fields. All required fields present.";
    }

    private async Task WriteMetadataFilesAsync(
        Work work,
        string workDir,
        List<ResolvedMetadataCandidate> resolvedCandidates,
        IEnumerable<MetadataResult> metadataResults,
        List<string> downloadedSampleUrls,
        CancellationToken ct)
    {
        try
        {
            // --- Build data from resolved candidates ---
            string GetPrimary(string field) =>
                resolvedCandidates.FirstOrDefault(c => c.Candidate.FieldName == field && c.IsPrimary)?.Candidate.Value ?? "";

            var actresses = resolvedCandidates
                .Where(c => c.Candidate.FieldName == "Actress" && c.IsPrimary)
                .Select(c => c.Candidate.Value)
                .ToList();

            var genreRaw = GetPrimary("Genre");
            var genres = string.IsNullOrWhiteSpace(genreRaw)
                ? new List<string>()
                : genreRaw.Split('|').Select(g => g.Trim()).Where(g => !string.IsNullOrEmpty(g)).ToList();

            var identifier = work.PrimaryIdentifier ?? "";
            var idLower = identifier.ToLower();
            var portraitFile = File.Exists(Path.Combine(workDir, $"{idLower}_ps.jpg")) ? $"{idLower}_ps.jpg" : (string?)null;
            var landscapeFile = File.Exists(Path.Combine(workDir, $"{idLower}_pl.jpg")) ? $"{idLower}_pl.jpg" : (string?)null;

            var successProviders = metadataResults.Where(r => r.Success).Select(r => r.ProviderName).ToList();
            var primaryProvider = resolvedCandidates
                .FirstOrDefault(c => c.Candidate.FieldName == "Title" && c.IsPrimary)?.Candidate.ProviderId;
            var primaryUrl = resolvedCandidates
                .Where(c => c.IsPrimary && !string.IsNullOrEmpty(c.Candidate.SourceUrl))
                .Select(c => c.Candidate.SourceUrl)
                .FirstOrDefault();

            int? runtime = int.TryParse(GetPrimary("Runtime"), out var rt) ? rt : null;
            var releaseDate = GetPrimary("ReleaseDate");

            // --- metadata.json ---
            // Preserve existing user data if metadata.json already exists
            bool existingFavorite = false;
            int? existingRating = null;
            string existingMemo = "";
            var existingJsonPath = Path.Combine(workDir, "metadata.json");
            if (File.Exists(existingJsonPath))
            {
                try
                {
                    using var existing = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(existingJsonPath, ct));
                    if (existing.RootElement.TryGetProperty("userFavorite", out var favEl)) existingFavorite = favEl.GetBoolean();
                    if (existing.RootElement.TryGetProperty("userRating", out var ratEl) && ratEl.ValueKind != JsonValueKind.Null) existingRating = ratEl.GetInt32();
                    if (existing.RootElement.TryGetProperty("userMemo", out var memEl)) existingMemo = memEl.GetString() ?? "";
                }
                catch { }
            }

            var metadata = new
            {
                identifier,
                provider = primaryProvider,
                providers = successProviders,
                url = primaryUrl,
                scrapedAt = DateTime.UtcNow,
                title = GetPrimary("Title"),
                maker = GetPrimary("Maker"),
                label = GetPrimary("Label"),
                series = GetPrimary("Series"),
                releaseDate,
                runtime,
                actresses = actresses.Select(a => new { name = a }).ToList(),
                genres,
                covers = new { portrait = portraitFile, landscape = landscapeFile },
                sampleImages = downloadedSampleUrls,
                userFavorite = existingFavorite,
                userRating = existingRating,
                userMemo = existingMemo,
            };

            var jsonOpts = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var metadataJson = JsonSerializer.Serialize(metadata, jsonOpts);
            await File.WriteAllTextAsync(Path.Combine(workDir, "metadata.json"), metadataJson, System.Text.Encoding.UTF8, ct);
            _logger.LogInformation("[MetadataFiles] Wrote metadata.json → {Dir}", workDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MetadataFiles] Failed to write metadata files for Work {WorkId}", work.Id);
        }
    }

    private void ReorganizeByActress(WISE.Domain.Entities.Work work, string actress)
    {
        var identifier = work.PrimaryIdentifier ?? "Unknown";
        var safeActress = SanitizeDirName(actress);

        foreach (var asset in work.Assets.ToList())
        {
            if (string.IsNullOrEmpty(asset.FilePath)) continue;

            var currentDir = Path.GetDirectoryName(asset.FilePath);
            if (currentDir == null) continue;

            // Determine library root from current file location.
            // Supported structures:
            //   LibraryRoot\Identifier\file         → initial state
            //   LibraryRoot\ActressName\Identifier\file → already organized (retry safe)
            //   LibraryRoot\ActressName\Identifier\.thumbnails\file → cover/thumb in subdirectory
            var currentDirName = Path.GetFileName(currentDir);
            string libraryRoot;
            if (string.Equals(currentDirName, identifier, StringComparison.OrdinalIgnoreCase))
            {
                // Parent of currentDir is either LibraryRoot or ActressName folder.
                var candidate = Path.GetDirectoryName(currentDir) ?? currentDir;
                // If already actress-organized: parent's name = safeActress → go up one more level
                if (string.Equals(Path.GetFileName(candidate), safeActress, StringComparison.OrdinalIgnoreCase))
                    libraryRoot = Path.GetDirectoryName(candidate) ?? candidate;
                else
                    libraryRoot = candidate;
            }
            else
            {
                // File may be inside a subdirectory (e.g. .thumbnails) under identifier folder
                var parentDir = Path.GetDirectoryName(currentDir);
                if (parentDir == null) continue;
                var parentDirName = Path.GetFileName(parentDir);
                if (!string.Equals(parentDirName, identifier, StringComparison.OrdinalIgnoreCase)) continue;
                var candidate = Path.GetDirectoryName(parentDir) ?? parentDir;
                if (string.Equals(Path.GetFileName(candidate), safeActress, StringComparison.OrdinalIgnoreCase))
                    libraryRoot = Path.GetDirectoryName(candidate) ?? candidate;
                else
                    libraryRoot = candidate;
            }

            var targetDir = Path.Combine(libraryRoot, safeActress, identifier);
            if (string.Equals(Path.GetFullPath(currentDir), Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                continue; // already in correct place

            var fileName = Path.GetFileName(asset.FilePath);
            var targetPath = Path.Combine(targetDir, fileName);

            try
            {
                Directory.CreateDirectory(targetDir);
                if (!File.Exists(targetPath))
                    File.Move(asset.FilePath, targetPath);
                var oldDir = Path.GetDirectoryName(asset.FilePath);
                asset.UpdateFilePath(targetPath);
                _logger.LogInformation("[Organize] {From} → {To}", Path.GetFileName(asset.FilePath), targetPath);

                // Clean up empty old directory
                if (oldDir != null && Directory.Exists(oldDir) && !Directory.EnumerateFileSystemEntries(oldDir).Any())
                    Directory.Delete(oldDir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Organize] Failed to move {Path}", asset.FilePath);
            }
        }

        _dbContext.EventLogs.Add(new EventLog(work.Id, "Files Organized", "System", "FetchMetadataJobUseCase",
            payload: $"Actress={actress} Identifier={identifier}"));
    }

    private static string SanitizeDirName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
    }

    private string? DetermineWorkDirectory(WISE.Domain.Entities.Work work)
    {
        var videoAsset = work.Assets.FirstOrDefault(a =>
            a.FilePath != null && (a.FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                || a.FilePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)));
        return videoAsset?.FilePath != null ? Path.GetDirectoryName(videoAsset.FilePath) : null;
    }

    private async Task<bool> DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken,
        int minFileSizeBytes = 1000, string? referer = null)
    {
        if (File.Exists(localPath)) return true;

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        // Use the page URL as Referer (site-aware) rather than a hardcoded DMM referer
        if (!string.IsNullOrWhiteSpace(referer))
            client.DefaultRequestHeaders.Add("Referer", referer);

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[Cover] HTTP {Status} for {Url}", response.StatusCode, url);
            return false;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length < minFileSizeBytes)
        {
            _logger.LogWarning("[Cover] Too small ({Size}B < {Min}B): {Url}", bytes.Length, minFileSizeBytes, url);
            return false;
        }

        await File.WriteAllBytesAsync(localPath, bytes, cancellationToken);
        _logger.LogInformation("[Cover] Saved {Size}B → {Path}", bytes.Length, localPath);
        return true;
    }
}
