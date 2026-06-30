using System.Collections.Generic;
using System.Threading.Tasks;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;

namespace WISE.Infrastructure.Providers;

public class DummyMetadataProvider : IMetadataProvider
{
    public string ProviderId => "Dummy";
    public int Priority => 10;

    public Task<MetadataResult> FetchAsync(MetadataProviderContext context)
    {
        var result = new List<MetadataCandidate>
        {
            new MetadataCandidate(ProviderId, "Title", "Dummy Video A", 80, Priority, "local")
        };
        return Task.FromResult(MetadataResult.Succeeded(ProviderId, result, TimeSpan.Zero));
    }
}
