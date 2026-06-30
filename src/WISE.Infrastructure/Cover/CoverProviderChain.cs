using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WISE.Domain.Entities;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Cover;

public class CoverProviderChain : ICoverProviderChain
{
    private readonly IReadOnlyList<ICoverProvider> _providers;
    private readonly ILogger<CoverProviderChain> _logger;

    public CoverProviderChain(IEnumerable<ICoverProvider> providers, ILogger<CoverProviderChain> logger)
    {
        _providers = providers.OrderBy(p => p.Priority).ToList();
        _logger = logger;
    }

    public async Task<CoverResult?> ResolveAsync(Work work, CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            if (!await provider.CanHandleAsync(work, ct))
                continue;

            var result = await provider.GetCoverAsync(work, ct);
            if (result != null)
            {
                _logger.LogDebug("[CoverChain] {Provider} resolved cover for work {WorkId}", provider.ProviderName, work.Id);
                return result;
            }
        }

        _logger.LogDebug("[CoverChain] No provider resolved cover for work {WorkId}", work.Id);
        return null;
    }
}
