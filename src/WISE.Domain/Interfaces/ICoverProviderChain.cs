using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;

namespace WISE.Domain.Interfaces;

public interface ICoverProviderChain
{
    Task<CoverResult?> ResolveAsync(Work work, CancellationToken ct = default);
}
