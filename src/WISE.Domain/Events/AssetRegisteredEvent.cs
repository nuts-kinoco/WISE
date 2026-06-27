using System;

namespace WISE.Domain.Events;

public record AssetRegisteredEvent(Guid AssetId, string FilePath) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
