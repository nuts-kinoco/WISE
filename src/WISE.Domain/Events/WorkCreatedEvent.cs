using System;

namespace WISE.Domain.Events;

public record WorkCreatedEvent(Guid WorkId, string? PrimaryIdentifier) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
