using System.Collections.Generic;
using System.Linq;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;

namespace WISE.Application.Services;

public class MetadataConflictResolver : IMetadataConflictResolver
{
    public IEnumerable<ResolvedMetadataCandidate> Resolve(IEnumerable<MetadataCandidate> candidates)
    {
        var resolved = new List<ResolvedMetadataCandidate>();

        if (candidates == null) return resolved;

        // Group by FieldName
        var groupedByField = candidates.GroupBy(c => c.FieldName);

        foreach (var group in groupedByField)
        {
            // Primary判定ロジック:
            // 1. Confidence が最も高いもの
            // 2. 同点の場合は Timestamp が新しいもの
            var ordered = group.OrderByDescending(c => c.Confidence)
                               .ThenByDescending(c => c.Priority)
                               .ThenByDescending(c => c.FetchedAt)
                               .ToList();

            bool isFirst = true;
            foreach (var candidate in ordered)
            {
                resolved.Add(new ResolvedMetadataCandidate(candidate, isFirst));
                isFirst = false;
            }
        }

        return resolved;
    }
}
