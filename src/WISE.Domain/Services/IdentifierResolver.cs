using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;
using WISE.Domain.Interfaces;
using WISE.Domain.ValueObjects;

namespace WISE.Domain.Services;

/// <summary>
/// Evidence → Confidence → IdentifierResult を生成する唯一の場所。
///
/// パイプライン:
///   PhysicalFile → Normalizer（将来）→ IEvidenceProvider[] → Confidence → IdentifierResult
///
/// Evidence の追加は IEvidenceProvider の新規実装で行い、Resolver は変更しない。
/// </summary>
public class IdentifierResolver : IIdentifierResolver
{
    private readonly IEnumerable<IEvidenceProvider> _providers;

    // Confidence >= この値なら Identifier が確定したとみなす
    private const int IdentifiedThreshold = 60;

    public IdentifierResolver(IEnumerable<IEvidenceProvider> providers)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    }

    public async Task<IdentifierResult> ResolveAsync(
        Asset asset,
        CancellationToken cancellationToken = default)
    {
        var allEvidences = new List<Evidence>();

        // --- 1. 全 EvidenceProvider を順番に実行 ---
        foreach (var provider in _providers)
        {
            var evidences = await provider.CollectEvidencesAsync(asset, cancellationToken);
            allEvidences.AddRange(evidences);
        }

        // --- 2. Confidence 計算 ---
        var totalScore = allEvidences.Sum(e => e.Score.Value);
        var confidence = new ConfidenceScore(Math.Min(100, totalScore));

        // --- 3. Identifier 文字列の決定 ---
        // Evidence の Value の中から最もスコアの高いものを採用
        var bestEvidence = allEvidences.OrderByDescending(e => e.Score.Value).FirstOrDefault();

        if (confidence.Value >= IdentifiedThreshold && bestEvidence != null)
        {
            // --- 3a. Identifier 確定 ---
            var identifier = bestEvidence.Value;

            // TODO: 将来は DB を検索し既存 Work があれば Decision.Existing を返す
            // v1.0 では常に Decision.New として扱う（重複チェックは UseCase 側で担う）
            return new IdentifierResult(
                decision: Decision.New,
                confidence: confidence,
                workId: null,
                evidences: allEvidences,
                extractedIdentifier: identifier);
        }
        else
        {
            // --- 3b. UNKNOWN 生成（決定論的） ---
            // SHA256(OriginalFilename + FileSize) の上位8文字を使用
            // 同じファイルなら必ず同じ UNKNOWN-XXXXXXXX になる
            var unknownId = GenerateDeterministicUnknown(asset.OriginalFilename, asset.FileSize);
            var rejectReason = allEvidences.Any()
                ? $"Confidence {confidence.Value} < threshold {IdentifiedThreshold}"
                : "No evidence collected. No pattern matched.";

            return new IdentifierResult(
                decision: Decision.Unknown,
                confidence: confidence,
                workId: null,
                evidences: allEvidences,
                extractedIdentifier: unknownId,
                rejectReason: rejectReason);
        }
    }

    /// <summary>
    /// SHA256(FileName + FileSize) の先頭8文字から決定論的に UNKNOWN ID を生成する。
    /// 同じファイル名・サイズであれば常に同じ値になる。
    /// </summary>
    private static string GenerateDeterministicUnknown(string fileName, long fileSize)
    {
        var input = $"{fileName}:{fileSize}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hashHex = Convert.ToHexString(hashBytes).Substring(0, 8);
        return $"UNKNOWN-{hashHex}";
    }
}
