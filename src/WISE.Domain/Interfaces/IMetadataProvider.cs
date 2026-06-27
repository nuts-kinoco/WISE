using System.Collections.Generic;
using System.Threading.Tasks;
using WISE.Domain.Models;

namespace WISE.Domain.Interfaces;

public interface IMetadataProvider
{
    string ProviderId { get; }
    int Priority { get; }
    Task<IEnumerable<MetadataCandidate>> FetchAsync(MetadataProviderContext context);
}
