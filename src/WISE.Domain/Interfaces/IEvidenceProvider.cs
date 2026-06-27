using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Entities;
using WISE.Domain.ValueObjects;

namespace WISE.Domain.Interfaces;

public interface IEvidenceProvider
{
    string ProviderId { get; }
    Task<IEnumerable<Evidence>> CollectEvidencesAsync(Asset asset, CancellationToken cancellationToken = default);
}
