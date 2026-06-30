using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Application.DTOs;
using WISE.Domain.Entities;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Infrastructure.Data;

namespace WISE.Api.UseCases;

public class ExecuteImportJobUseCase
{
    private readonly WiseDbContext _dbContext;
    private readonly IOutputPathResolver _outputPathResolver;
    private readonly IIdentifierResolver _identifierResolver;

    public ExecuteImportJobUseCase(
        WiseDbContext dbContext,
        IOutputPathResolver outputPathResolver,
        IIdentifierResolver identifierResolver)
    {
        _dbContext = dbContext;
        _outputPathResolver = outputPathResolver;
        _identifierResolver = identifierResolver;
    }

    public async Task<ExecuteImportJobResult> ExecuteAsync(
        ImportJobRequest request,
        Action<int, int>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var extensions = new[] { ".mp4", ".mkv", ".avi", ".zip", ".jpg", ".png" };
        var files = new List<string>();

        foreach (var directoryPath in request.InputFolders)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                continue;

            files.AddRange(Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower())));
        }

        if (request.InputFiles != null)
        {
            files.AddRange(request.InputFiles
                .Where(f => File.Exists(f) && extensions.Contains(Path.GetExtension(f).ToLower())));
        }

        files = files.Distinct().ToList();

        int addedWorksCount = 0;
        int addedAssetsCount = 0;
        int duplicatesMergedCount = 0;
        var newWorksCache = new Dictionary<string, Work>();

        int totalFiles = files.Count;
        int processedFiles = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(processedFiles, totalFiles);

            var fileName = Path.GetFileName(file);
            var fileInfo = new FileInfo(file);

            // --- 1. Asset を仮生成（Resolver への入力用）---
            // 実際の DB 保存は Work 確定後に行う
            var tempAsset = new Asset(file, fileName, fileInfo.Length);

            // --- 2. IdentifierResolver で Evidence → Confidence → IdentifierResult ---
            var identifierResult = await _identifierResolver.ResolveAsync(tempAsset, cancellationToken);
            var identifier = identifierResult.ExtractedIdentifier;

            // --- 3. Diagnostic を JSON で構築 ---
            var diagnosticPayload = JsonSerializer.Serialize(new
            {
                file = fileName,
                normalizedFileName = fileName, // Normalizer は v1.0 未実装のため原文のまま
                candidates = identifierResult.Evidences.Select(e => new
                {
                    pattern = e.Type,
                    value = e.Value,
                    provider = e.ProviderId
                }),
                evidences = identifierResult.Evidences.Select(e => new
                {
                    type = e.Type,
                    value = e.Value,
                    score = e.Score.Value,
                    provider = e.ProviderId
                }),
                confidence = identifierResult.Confidence.Value,
                decision = identifierResult.Decision.ToString(),
                identifier = identifier,
                rejectReason = identifierResult.RejectReason
            });

            // --- 4. Move / Copy ---
            string finalFilePath = file;
            if (!string.IsNullOrWhiteSpace(request.OutputFolder)
                && (request.ImportMode == "Move" || request.ImportMode == "Copy"))
            {
                var destPath = _outputPathResolver.Resolve(request.OutputFolder, identifier, fileName);
                var destDir = Path.GetDirectoryName(destPath);

                if (destDir != null && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                if (!File.Exists(destPath))
                {
                    try
                    {
                        if (request.ImportMode == "Move")
                            File.Move(file, destPath);
                        else
                            File.Copy(file, destPath);

                        finalFilePath = destPath;
                    }
                    catch
                    {
                        finalFilePath = file;
                    }
                }
                else
                {
                    finalFilePath = destPath;
                }
            }

            // --- 5. Work の検索または作成 ---
            var work = await _dbContext.Works
                .Include(w => w.Assets)
                .FirstOrDefaultAsync(w => w.PrimaryIdentifier == identifier, cancellationToken);

            bool isNewWork = false;

            if (work == null && newWorksCache.TryGetValue(identifier, out var cachedWork))
                work = cachedWork;

            if (work == null)
            {
                work = new Work(identifier, InferMediaType(finalFilePath));
                _dbContext.Works.Add(work);
                newWorksCache[identifier] = work;
                addedWorksCount++;
                isNewWork = true;

                // Work 作成イベント（Diagnostic 込み）
                var createLog = new EventLog(
                    work.Id,
                    "Work Created",
                    "System",
                    "ExecuteImportJob",
                    diagnosticPayload);
                _dbContext.EventLogs.Add(createLog);
            }

            // --- 6. Asset 追加 ---
            if (!work.Assets.Any(a => a.FilePath == finalFilePath))
            {
                var asset = new Asset(finalFilePath, fileName, fileInfo.Length, "sha256-pending");
                work.AddAsset(asset);
                addedAssetsCount++;

                if (!isNewWork)
                {
                    duplicatesMergedCount++;
                    var mergeLog = new EventLog(
                        work.Id,
                        "Duplicate Merged",
                        "System",
                        "ExecuteImportJob",
                        $"Merged duplicated item {fileName} into {identifier}");
                    _dbContext.EventLogs.Add(mergeLog);
                }

                var assetLog = new EventLog(
                    work.Id,
                    "Asset Added",
                    "System",
                    "ExecuteImportJob",
                    $"Added asset: {fileName}");
                _dbContext.EventLogs.Add(assetLog);
            }

            processedFiles++;
            onProgress?.Invoke(processedFiles, totalFiles);
        }

        var completionLog = new EventLog(
            null,
            "Import Completed",
            "System",
            "ExecuteImportJob",
            $"Imported {addedWorksCount} works and {addedAssetsCount} assets");
        _dbContext.EventLogs.Add(completionLog);

        // --- 7. Metadata パイプライン登録 ---
        if (request.UseMetadataPipeline)
        {
            foreach (var w in newWorksCache.Values)
            {
                var payload = JsonSerializer.Serialize(new { WorkId = w.Id });
                var metadataJob = new Job("FetchMetadata", $"Work_{w.Id}", payload);
                metadataJob.MarkAsQueued();
                _dbContext.Jobs.Add(metadataJob);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ExecuteImportJobResult
        {
            WorksAdded = addedWorksCount,
            AssetsAdded = addedAssetsCount,
            DuplicatesMerged = duplicatesMergedCount
        };
    }

    private static MediaType InferMediaType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".mp4" or ".mkv" or ".avi" or ".wmv" or ".mov" or ".m4v" => MediaType.Video,
            ".zip" or ".cbz" or ".rar" or ".cbr" or ".7z" => MediaType.Comic,
            ".epub" => MediaType.Book,
            ".pdf" => MediaType.Book,
            ".jpg" or ".jpeg" or ".png" or ".webp" => MediaType.PhotoBook,
            _ => MediaType.Video
        };
    }
}
