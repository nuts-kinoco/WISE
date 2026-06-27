using System;

namespace WISE.Infrastructure.Data.Models;

public class HistoryRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public Guid? CorrelationId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public Guid? WorkId { get; set; }
    public Guid? AssetId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string Payload { get; set; } = string.Empty;
}
