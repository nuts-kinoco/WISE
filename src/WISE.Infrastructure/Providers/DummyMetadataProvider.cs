using System.Collections.Generic;
using System.Threading.Tasks;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;

namespace WISE.Infrastructure.Providers;

public class DummyMetadataProvider : IMetadataProvider
{
    public string ProviderId => "Dummy";
    public int Priority => 10;

    public Task<IEnumerable<MetadataCandidate>> FetchAsync(MetadataProviderContext context)
    {
        return Task.FromResult<IEnumerable<MetadataCandidate>>(new[]
        {
            new MetadataCandidate(ProviderId, "Title", "Dummy Title", 80),
            new MetadataCandidate(ProviderId, "Actress", "Dummy Actress", 80)
        });
    }
}
