using System;
using System.Collections.Generic;

namespace WISE.Domain.ValueObjects;

public enum Decision
{
    Existing,   // 既存 Work に紐付く
    New,        // 新規 Work を作成する
    Unknown     // Identifier が解決できなかった（UNKNOWN-XXXX を生成）
}

public record IdentifierResult
{
    public Decision Decision { get; init; }
    public ConfidenceScore Confidence { get; init; }
    public Guid? WorkId { get; init; }
    public IReadOnlyList<Evidence> Evidences { get; init; }

    /// <summary>最終的に決定された Identifier 文字列（UNKNOWN-XXXX を含む）</summary>
    public string ExtractedIdentifier { get; init; }

    /// <summary>Unknown 判定になった場合の理由（正常時は null）</summary>
    public string? RejectReason { get; init; }

    public IdentifierResult(
        Decision decision,
        ConfidenceScore confidence,
        Guid? workId,
        IReadOnlyList<Evidence> evidences,
        string extractedIdentifier,
        string? rejectReason = null)
    {
        Decision = decision;
        Confidence = confidence ?? throw new ArgumentNullException(nameof(confidence));
        WorkId = workId;
        Evidences = evidences ?? throw new ArgumentNullException(nameof(evidences));
        ExtractedIdentifier = extractedIdentifier ?? throw new ArgumentNullException(nameof(extractedIdentifier));
        RejectReason = rejectReason;

        if (decision == Decision.Existing && workId == null)
            throw new ArgumentException("WorkId must be provided when decision is Existing.");
    }
}
