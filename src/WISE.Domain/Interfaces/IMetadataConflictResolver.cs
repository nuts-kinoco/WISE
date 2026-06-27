using System.Collections.Generic;
using WISE.Domain.Models;

namespace WISE.Domain.Interfaces;

public interface IMetadataConflictResolver
{
    IEnumerable<ResolvedMetadataCandidate> Resolve(IEnumerable<MetadataCandidate> candidates);
}
