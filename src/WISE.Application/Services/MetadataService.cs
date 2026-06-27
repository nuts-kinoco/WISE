using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;

namespace WISE.Application.Services;

public class MetadataService
{
    private readonly IEnumerable<IMetadataProvider> _providers;

    public MetadataService(IEnumerable<IMetadataProvider> providers)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    }

    public async Task<IEnumerable<MetadataCandidate>> CollectCandidatesAsync(MetadataProviderContext context)
    {
        var tasks = _providers.Select(async provider => 
        {
            try
            {
                return await provider.FetchAsync(context);
            }
            catch
            {
                // エラー時はスキップして収集を継続
                return Enumerable.Empty<MetadataCandidate>();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(x => x);
    }
}
