using System;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Events;
using WISE.Domain.Interfaces;

namespace WISE.Application.Jobs;

public class JobFactory : IJobFactory
{
    private readonly IJobQueue _jobQueue;

    public JobFactory(IJobQueue jobQueue)
    {
        _jobQueue = jobQueue ?? throw new ArgumentNullException(nameof(jobQueue));
    }

    public async Task CreateAndEnqueueAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (domainEvent is WorkCreatedEvent workCreatedEvent)
        {
            var payload = $"{{\"WorkId\":\"{workCreatedEvent.WorkId}\"}}";
            await _jobQueue.EnqueueAsync("MetadataFetch", payload, workCreatedEvent.EventId, workCreatedEvent.WorkId, cancellationToken);
        }
        else if (domainEvent is IdentifierResolvedEvent resolvedEvent)
        {
            var payload = $"{{\"AssetId\":\"{resolvedEvent.AssetId}\",\"TargetWorkId\":\"{resolvedEvent.TargetWorkId}\"}}";
            await _jobQueue.EnqueueAsync("MetadataFetch", payload, resolvedEvent.EventId, resolvedEvent.TargetWorkId, cancellationToken);
        }
    }
}
