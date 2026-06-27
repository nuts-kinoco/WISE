using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Application.DTOs;
using WISE.Domain.Entities;
using WISE.Domain.Interfaces;
using WISE.Domain.Services;
using WISE.Infrastructure.Data;

namespace WISE.Api.UseCases;

public class ExecuteImportJobUseCase
{
    private readonly WiseDbContext _dbContext;
    private readonly IOutputPathResolver _outputPathResolver;

    public ExecuteImportJobUseCase(WiseDbContext dbContext, IOutputPathResolver outputPathResolver)
    {
        _dbContext = dbContext;
        _outputPathResolver = outputPathResolver;
    }

    public async Task<ExecuteImportJobResult> ExecuteAsync(ImportJobRequest request, Action<int, int>? onProgress = null, System.Threading.CancellationToken cancellationToken = default)
    {
        var extensions = new[] { ".mp4", ".mkv", ".avi", ".zip", ".jpg", ".png" };
        var files = new List<string>();

        foreach (var directoryPath in request.InputFolders)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                continue; // Skip invalid directories in job execution
            }

            files.AddRange(Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower())));
        }

        if (request.InputFiles != null)
        {
            files.AddRange(request.InputFiles.Where(f => File.Exists(f) && extensions.Contains(Path.GetExtension(f).ToLower())));
        }
        
        // Ensure uniqueness
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
            var identifier = IdentifierParser.Parse(fileName);
            var fileInfo = new FileInfo(file);

            // Handle Move/Copy
            string finalFilePath = file;
            if (!string.IsNullOrWhiteSpace(request.OutputFolder) && (request.ImportMode == "Move" || request.ImportMode == "Copy"))
            {
                var destPath = _outputPathResolver.Resolve(request.OutputFolder, identifier, fileName);
                var destDir = Path.GetDirectoryName(destPath);
                
                if (destDir != null && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                if (!File.Exists(destPath))
                {
                    try
                    {
                        if (request.ImportMode == "Move")
                        {
                            File.Move(file, destPath);
                        }
                        else if (request.ImportMode == "Copy")
                        {
                            File.Copy(file, destPath);
                        }
                        finalFilePath = destPath;
                    }
                    catch (Exception)
                    {
                        // Fallback to original file path if move/copy fails due to permissions etc.
                        finalFilePath = file;
                    }
                }
                else
                {
                    // Destination already exists, map to existing file
                    finalFilePath = destPath;
                }
            }

            var work = await _dbContext.Works.Include(w => w.Assets).FirstOrDefaultAsync(w => w.PrimaryIdentifier == identifier);
            bool isNewWork = false;

            if (work == null && newWorksCache.TryGetValue(identifier, out var cachedWork))
            {
                work = cachedWork;
            }

            if (work == null)
            {
                work = new Work(identifier);
                _dbContext.Works.Add(work);
                newWorksCache[identifier] = work;
                addedWorksCount++;
                isNewWork = true;

                var createLog = new EventLog(work.Id, "Work Created", "System", "ExecuteImportJob", $"New work created from identifier: {identifier}");
                _dbContext.EventLogs.Add(createLog);
            }

            if (!work.Assets.Any(a => a.FilePath == finalFilePath))
            {
                var asset = new Asset(finalFilePath, fileName, fileInfo.Length, "sha256-pending");
                work.AddAsset(asset);
                addedAssetsCount++;

                if (!isNewWork)
                {
                    duplicatesMergedCount++;
                    var mergeLog = new EventLog(work.Id, "Duplicate Merged", "System", "ExecuteImportJob", $"Merged duplicated item {fileName} into {identifier}");
                    _dbContext.EventLogs.Add(mergeLog);
                }

                var assetLog = new EventLog(work.Id, "Asset Added", "System", "ExecuteImportJob", $"Added asset: {fileName}");
                _dbContext.EventLogs.Add(assetLog);
            }
            
            processedFiles++;
            onProgress?.Invoke(processedFiles, totalFiles);
        }

        var completionLog = new EventLog(null, "Import Completed", "System", "ExecuteImportJob", $"Imported {addedWorksCount} works and {addedAssetsCount} assets from {request.InputFolders.Count} folders and {request.InputFiles.Count} files");
        _dbContext.EventLogs.Add(completionLog);

        if (request.UseMetadataPipeline)
        {
            foreach (var w in newWorksCache.Values)
            {
                var payload = System.Text.Json.JsonSerializer.Serialize(new { WorkId = w.Id });
                var metadataJob = new Job("FetchMetadata", $"Work_{w.Id}", payload);
                _dbContext.Jobs.Add(metadataJob);
            }
        }

        await _dbContext.SaveChangesAsync();

        return new ExecuteImportJobResult
        {
            WorksAdded = addedWorksCount,
            AssetsAdded = addedAssetsCount,
            DuplicatesMerged = duplicatesMergedCount
        };
    }
}
