using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Events;

namespace WISE.Domain.Interfaces;

public interface IEventBus
{
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
