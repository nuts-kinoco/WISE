using System;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;

namespace WISE.Domain.Interfaces;

public interface ICoverCacheRepository
{
    Task<CoverCache?> GetAsync(Guid workId, string? providerName = null, CancellationToken ct = default);
    Task UpsertAsync(CoverCache cache, CancellationToken ct = default);
    Task DeleteAsync(Guid workId, CancellationToken ct = default);
}
