using System;
using System.Collections.Generic;

namespace WISE.Application.UseCases;

public record ProcessNewAssetResult
{
    public Guid WorkId { get; init; }
    public bool IsNewWork { get; init; }
    public int Confidence { get; init; }
    public IReadOnlyList<string> EvidenceSummary { get; init; } = new List<string>();
}
