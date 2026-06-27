using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Interfaces;
using WISE.Domain.Entities;
using WISE.Domain.ValueObjects;

namespace WISE.Domain.Services;

public class IdentifierResolver : IIdentifierResolver
{
    private readonly IEnumerable<IEvidenceProvider> _providers;
    private const int ExistingThreshold = 80;

    public IdentifierResolver(IEnumerable<IEvidenceProvider> providers)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    }

    public async Task<IdentifierResult> ResolveAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        var allEvidences = new List<Evidence>();

        foreach (var provider in _providers)
        {
            var evidences = await provider.CollectEvidencesAsync(asset, cancellationToken);
            allEvidences.AddRange(evidences);
        }

        var totalScore = allEvidences.Sum(e => e.Score.Value);
        var confidence = new ConfidenceScore(Math.Min(100, totalScore));

        if (confidence.Value >= ExistingThreshold)
        {
            // 実際はEvidenceの中の最も有力なWorkIdを採用するが、今回はダミーのGuidを返す
            return new IdentifierResult(Decision.Existing, confidence, Guid.NewGuid(), allEvidences);
        }
        else if (confidence.Value > 0)
        {
            return new IdentifierResult(Decision.New, confidence, null, allEvidences);
        }
        else
        {
            return new IdentifierResult(Decision.Unknown, confidence, null, allEvidences);
        }
    }
}
