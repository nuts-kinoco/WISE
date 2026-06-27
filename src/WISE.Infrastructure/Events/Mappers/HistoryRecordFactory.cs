using System;
using System.Text.Json;
using WISE.Domain.Events;
using WISE.Infrastructure.Data.Models;

namespace WISE.Infrastructure.Events.Mappers;

public static class HistoryRecordFactory
{
    public static HistoryRecord Create(IDomainEvent domainEvent)
    {
        if (domainEvent == null) throw new ArgumentNullException(nameof(domainEvent));

        var record = new HistoryRecord
        {
            EventId = domainEvent.EventId,
            OccurredAt = domainEvent.OccurredAt,
            EventType = domainEvent.GetType().Name,
            SchemaVersion = 1,
            Payload = JsonSerializer.Serialize((object)domainEvent)
        };

        if (domainEvent is WorkCreatedEvent workCreatedEvent)
        {
            record.WorkId = workCreatedEvent.WorkId;
        }
        else if (domainEvent is AssetRegisteredEvent assetRegisteredEvent)
        {
            record.AssetId = assetRegisteredEvent.AssetId;
        }
        else if (domainEvent is IdentifierResolvedEvent identifierResolvedEvent)
        {
            record.AssetId = identifierResolvedEvent.AssetId;
            record.WorkId = identifierResolvedEvent.TargetWorkId;
        }

        return record;
    }
}
