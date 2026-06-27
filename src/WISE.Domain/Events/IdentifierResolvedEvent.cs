using System;

namespace WISE.Domain.Events;

public record IdentifierResolvedEvent(Guid AssetId, Guid? TargetWorkId, string Decision, int Confidence) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
