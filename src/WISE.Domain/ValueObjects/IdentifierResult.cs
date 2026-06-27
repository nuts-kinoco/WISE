using System;
using System.Collections.Generic;

namespace WISE.Domain.ValueObjects;

public enum Decision
{
    Existing,
    New,
    Unknown
}

public record IdentifierResult
{
    public Decision Decision { get; init; }
    public ConfidenceScore Confidence { get; init; }
    public Guid? WorkId { get; init; }
    public IReadOnlyList<Evidence> Evidences { get; init; }

    public IdentifierResult(Decision decision, ConfidenceScore confidence, Guid? workId, IReadOnlyList<Evidence> evidences)
    {
        Decision = decision;
        Confidence = confidence ?? throw new ArgumentNullException(nameof(confidence));
        WorkId = workId;
        Evidences = evidences ?? throw new ArgumentNullException(nameof(evidences));

        if (decision == Decision.Existing && workId == null)
        {
            throw new ArgumentException("WorkId must be provided when decision is Existing.");
        }
    }
}
