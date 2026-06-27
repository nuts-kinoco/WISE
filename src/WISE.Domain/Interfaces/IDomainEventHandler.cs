using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Events;

namespace WISE.Domain.Interfaces;

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
