using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WISE.Domain.Entities;
using WISE.Domain.Interfaces;
using WISE.Infrastructure.Data;

namespace WISE.Api.UseCases;

public class ImportUseCase
{
    private readonly WiseDbContext _dbContext;
    private readonly IIdentifierResolver _identifierResolver;
    private readonly ILogger<ImportUseCase> _logger;

    public ImportUseCase(
        WiseDbContext dbContext,
        IIdentifierResolver identifierResolver,
        ILogger<ImportUseCase> logger)
    {
        _dbContext = dbContext;
        _identifierResolver = identifierResolver;
        _logger = logger;
    }

    public class AnalyzeResultDto
    {
        public string ScannedDirectory { get; set; } = string.Empty;
        public int TotalFiles { get; set; }
        public List<CandidateDto> Candidates { get; set; } = new();
    }

    public class CandidateDto
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string? ExtractedIdentifier { get; set; }
        public bool IsExisting { get; set; }
        public int Confidence { get; set; }
        public List<EvidenceDto> Evidences { get; set; } = new();
    }

    public class EvidenceDto
    {
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public int Score { get; set; }
        public string Provider { get; set; } = string.Empty;
    }

    public async Task<AnalyzeResultDto> AnalyzeDirectoryAsync(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            throw new ArgumentException("Invalid or inaccessible directory path.");

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".mkv", ".avi", ".zip", ".cbz", ".rar", ".cbr", ".7z", ".epub", ".pdf", ".jpg", ".jpeg", ".png", ".webp" };

        var existingIdentifiers = await _dbContext.Works
            .Select(w => w.PrimaryIdentifier)
            .ToListAsync();
        var existingSet = new HashSet<string>(
            existingIdentifiers.Where(i => i != null).Cast<string>());

        var candidates = new List<CandidateDto>();
        var totalFiles = 0;

        // アクセス拒否ディレクトリは警告を出してスキップ（例外で全件失敗しない）
        foreach (var file in EnumerateSafe(directoryPath, extensions, _logger))
        {
            totalFiles++;
            var fileName = Path.GetFileName(file);
            FileInfo fileInfo;
            try { fileInfo = new FileInfo(file); }
            catch (UnauthorizedAccessException) { continue; }

            var tempAsset = new Asset(file, fileName, fileInfo.Length);
            var identifierResult = await _identifierResolver.ResolveAsync(tempAsset);
            var isUnknown = identifierResult.Decision == WISE.Domain.ValueObjects.Decision.Unknown;

            candidates.Add(new CandidateDto
            {
                FilePath = file,
                FileName = fileName,
                FileSize = fileInfo.Length,
                ExtractedIdentifier = isUnknown ? null : identifierResult.ExtractedIdentifier,
                IsExisting = !isUnknown && existingSet.Contains(identifierResult.ExtractedIdentifier),
                Confidence = identifierResult.Confidence.Value,
                Evidences = identifierResult.Evidences.Select(e => new EvidenceDto
                {
                    Type = e.Type,
                    Value = e.Value,
                    Score = e.Score.Value,
                    Provider = e.ProviderId
                }).ToList()
            });
        }

        return new AnalyzeResultDto
        {
            ScannedDirectory = directoryPath,
            TotalFiles = totalFiles,
            Candidates = candidates
        };
    }

    // アクセス拒否・IO エラーのディレクトリをスキップしながら再帰列挙する。
    // Directory.GetFiles と異なり全件を一度にメモリに展開しない。
    private static IEnumerable<string> EnumerateSafe(
        string root, HashSet<string> extensions, ILogger logger)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var dir = queue.Dequeue();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir); }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning("[Import] スキップ（アクセス拒否）: {Dir} — {Msg}", dir, ex.Message);
                continue;
            }
            catch (IOException ex)
            {
                logger.LogWarning("[Import] スキップ（IO エラー）: {Dir} — {Msg}", dir, ex.Message);
                continue;
            }

            foreach (var entry in entries)
            {
                if (Directory.Exists(entry))
                    queue.Enqueue(entry);
                else if (extensions.Contains(Path.GetExtension(entry)))
                    yield return entry;
            }
        }
    }
}
