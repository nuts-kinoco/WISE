using System;
using System.Threading;
using System.Threading.Tasks;

namespace WISE.Domain.Interfaces;

public interface IJobQueue
{
    Task<Guid> EnqueueAsync(string jobType, string payload, Guid? correlationId = null, Guid? targetWorkId = null, CancellationToken cancellationToken = default);
}
