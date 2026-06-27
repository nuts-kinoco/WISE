using System;
using WISE.Domain.SeedWork;

namespace WISE.Domain.Entities;

public class EventLog : Entity
{
    public Guid? TargetId { get; private set; }
    public string EventType { get; private set; }
    public string Actor { get; private set; }
    public string Source { get; private set; }
    public string? Payload { get; private set; }
    public Guid? CorrelationId { get; private set; }
    public DateTime OccurredAt { get; private set; }

    public virtual Work? TargetWork { get; private set; }

    protected EventLog()
    {
        EventType = string.Empty;
        Actor = string.Empty;
        Source = string.Empty;
    }

    public EventLog(Guid? targetId, string eventType, string actor, string source, string? payload = null, Guid? correlationId = null)
    {
        Id = Guid.NewGuid();
        TargetId = targetId;
        EventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
        Actor = actor ?? throw new ArgumentNullException(nameof(actor));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Payload = payload;
        CorrelationId = correlationId;
        OccurredAt = DateTime.UtcNow;
    }
}
