using System;
using WISE.Domain.Interfaces;

namespace WISE.Domain.Events;

public class MetadataUpdatedEvent : IDomainEvent
{
    public Guid EventId { get; }
    public DateTime OccurredAt { get; }
    public Guid WorkId { get; }

    public MetadataUpdatedEvent(Guid workId)
    {
        EventId = Guid.NewGuid();
        OccurredAt = DateTime.UtcNow;
        WorkId = workId;
    }
}
