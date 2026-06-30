using System.Collections.Generic;
using System.Threading.Tasks;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;

namespace WISE.Infrastructure.Providers;

public class ConflictDummyMetadataProvider : IMetadataProvider
{
    public string ProviderId => "ConflictDummy";
    public int Priority => 20;

    public Task<MetadataResult> FetchAsync(MetadataProviderContext context)
    {
        var result = new List<MetadataCandidate>
        {
            new MetadataCandidate(ProviderId, "Title", "Dummy Conflict Video A", 80, Priority, "local")
        };
        return Task.FromResult(MetadataResult.Succeeded(ProviderId, result, TimeSpan.Zero));
    }
}
