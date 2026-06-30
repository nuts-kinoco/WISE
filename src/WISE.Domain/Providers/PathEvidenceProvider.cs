using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;
using WISE.Domain.Interfaces;
using WISE.Domain.Services;
using WISE.Domain.ValueObjects;

namespace WISE.Domain.Providers;

/// <summary>
/// Scans parent directory names for identifier patterns.
/// Doujin works are often organized as "[RJ123456] Title/file.cbz" or "RJ123456/file.cbz".
/// Gives a supporting score (60) — not as authoritative as the filename itself (90),
/// but sufficient to push confidence over the threshold when the filename alone is ambiguous.
/// </summary>
public class PathEvidenceProvider : IEvidenceProvider
{
    public string ProviderId => "Core.PathProvider";

    private const int PathMatchScore = 60;

    public Task<IEnumerable<Evidence>> CollectEvidencesAsync(
        Asset asset,
        CancellationToken cancellationToken = default)
    {
        var evidences = new List<Evidence>();

        if (string.IsNullOrWhiteSpace(asset.FilePath))
            return Task.FromResult<IEnumerable<Evidence>>(evidences);

        var directory = Path.GetDirectoryName(asset.FilePath);
        if (string.IsNullOrEmpty(directory)) return Task.FromResult<IEnumerable<Evidence>>(evidences);

        // Walk up at most 2 levels so we don't scan the entire root tree
        var dirName = Path.GetFileName(directory);
        TryExtractFromFolder(dirName, evidences);

        var parent = Path.GetDirectoryName(directory);
        if (parent != null)
        {
            var parentName = Path.GetFileName(parent);
            TryExtractFromFolder(parentName, evidences);
        }

        return Task.FromResult<IEnumerable<Evidence>>(evidences);
    }

    private static void TryExtractFromFolder(string? folderName, List<Evidence> evidences)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return;

        var candidates = IdentifierParser.ExtractCandidates(folderName);
        foreach (var candidate in candidates)
        {
            evidences.Add(new Evidence(
                type: $"Path.{candidate.PatternName}",
                value: candidate.ExtractedValue,
                score: new ConfidenceScore(PathMatchScore),
                providerId: "Core.PathProvider"));
        }
    }
}
