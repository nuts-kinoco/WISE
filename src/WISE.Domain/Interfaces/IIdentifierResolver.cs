using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;
using WISE.Domain.ValueObjects;

namespace WISE.Domain.Interfaces;

public interface IIdentifierResolver
{
    Task<IdentifierResult> ResolveAsync(Asset asset, CancellationToken cancellationToken = default);
}
