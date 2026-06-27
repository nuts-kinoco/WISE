using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Events;

namespace WISE.Application.Jobs;

public interface IJobFactory
{
    Task CreateAndEnqueueAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
