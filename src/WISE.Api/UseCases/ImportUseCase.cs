using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Domain.Interfaces;
using WISE.Infrastructure.Data;

namespace WISE.Api.UseCases;

public class ImportUseCase
{
    private readonly WiseDbContext _dbContext;
    private readonly IIdentifierResolver _identifierResolver;

    public ImportUseCase(WiseDbContext dbContext, IIdentifierResolver identifierResolver)
    {
        _dbContext = dbContext;
        _identifierResolver = identifierResolver;
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

        var extensions = new[] { ".mp4", ".mkv", ".avi", ".zip", ".jpg", ".png" };
        var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
            .ToList();

        var candidates = new List<CandidateDto>();
        var existingIdentifiers = await _dbContext.Works
            .Select(w => w.PrimaryIdentifier)
            .ToListAsync();
        var existingSet = new HashSet<string>(
            existingIdentifiers.Where(i => i != null).Cast<string>());

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var fileInfo = new FileInfo(file);

            // Import と同じ IdentifierResolver を使用
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
            TotalFiles = files.Count,
            Candidates = candidates
        };
    }
}
