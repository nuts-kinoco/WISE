using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;
using WISE.Domain.Interfaces;
using WISE.Domain.Services;
using WISE.Domain.ValueObjects;

namespace WISE.Domain.Providers;

/// <summary>
/// ファイル名から IdentifierCandidate を抽出し、Evidence に変換する Provider。
/// Score の決定はここで行う（Candidate はスコアを持たない）。
///
/// 拡張例:
///   PathEvidenceProvider - フォルダパスからも候補を抽出してスコア加算
///   MetadataHintProvider - 既存メタデータからスコア加算
/// </summary>
public class FileNameEvidenceProvider : IEvidenceProvider
{
    public string ProviderId => "Core.FileNameProvider";

    /// <summary>
    /// CommercialVideoPattern / FC2Pattern / RJPattern / DatePattern に一致した場合は Score=90。
    /// 候補なし（UNKNOWN予定）の場合は Evidence なし（Score=0として扱われる）。
    /// </summary>
    private const int PatternMatchScore = 90;

    public Task<IEnumerable<Evidence>> CollectEvidencesAsync(
        Asset asset,
        CancellationToken cancellationToken = default)
    {
        var evidences = new List<Evidence>();

        if (string.IsNullOrWhiteSpace(asset.OriginalFilename))
            return Task.FromResult<IEnumerable<Evidence>>(evidences);

        var candidates = IdentifierParser.ExtractCandidates(asset.OriginalFilename);

        foreach (var candidate in candidates)
        {
            evidences.Add(new Evidence(
                type: candidate.PatternName,
                value: candidate.ExtractedValue,
                score: new ConfidenceScore(PatternMatchScore),
                providerId: ProviderId));
        }

        return Task.FromResult<IEnumerable<Evidence>>(evidences);
    }
}
