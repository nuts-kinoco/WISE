using System;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Events;
using WISE.Domain.Interfaces;
using WISE.Infrastructure.Data.Repositories;
using WISE.Infrastructure.Events.Mappers;

namespace WISE.Infrastructure.Events.Handlers;

public class HistoryLoggingEventHandler : 
    IDomainEventHandler<WorkCreatedEvent>,
    IDomainEventHandler<AssetRegisteredEvent>,
    IDomainEventHandler<IdentifierResolvedEvent>
{
    private readonly IHistoryRepository _repository;

    public HistoryLoggingEventHandler(IHistoryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    private async Task LogEventAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var record = HistoryRecordFactory.Create(domainEvent);
        await _repository.AddAsync(record, cancellationToken);
    }

    public Task HandleAsync(WorkCreatedEvent domainEvent, CancellationToken cancellationToken = default) => LogEventAsync(domainEvent, cancellationToken);
    public Task HandleAsync(AssetRegisteredEvent domainEvent, CancellationToken cancellationToken = default) => LogEventAsync(domainEvent, cancellationToken);
    public Task HandleAsync(IdentifierResolvedEvent domainEvent, CancellationToken cancellationToken = default) => LogEventAsync(domainEvent, cancellationToken);
}
