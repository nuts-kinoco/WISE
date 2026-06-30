using System;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;

namespace WISE.Domain.Interfaces;

public record CoverResult(
    string FilePath,
    string ContentType,
    string ProviderName,
    DateTime? ExpiresAt = null);

public interface ICoverProvider
{
    string ProviderName { get; }
    int Priority { get; }
    Task<bool> CanHandleAsync(Work work, CancellationToken ct = default);
    Task<CoverResult?> GetCoverAsync(Work work, CancellationToken ct = default);
}
