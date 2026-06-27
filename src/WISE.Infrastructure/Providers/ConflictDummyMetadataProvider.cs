using System.Collections.Generic;
using System.Threading.Tasks;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;

namespace WISE.Infrastructure.Providers;

public class ConflictDummyMetadataProvider : IMetadataProvider
{
    public string ProviderId => "ConflictDummy";
    public int Priority => 20;

    public Task<IEnumerable<MetadataCandidate>> FetchAsync(MetadataProviderContext context)
    {
        return Task.FromResult<IEnumerable<MetadataCandidate>>(new[]
        {
            new MetadataCandidate(ProviderId, "Title", "Conflicting Title", 95),
            new MetadataCandidate(ProviderId, "Maker", "Conflict Maker", 90)
        });
    }
}
