using System;
using System.Collections.Generic;
using System.Linq;
using WISE.Domain.SeedWork;

namespace WISE.Domain.Entities;

public class Work : Entity, IAggregateRoot
{
    public string? PrimaryIdentifier { get; private set; }
    public Guid? MergedIntoId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private readonly List<Asset> _assets = new();
    public virtual IReadOnlyCollection<Asset> Assets => _assets.AsReadOnly();

    private readonly List<MetadataField> _metadataFields = new();
    public virtual IReadOnlyCollection<MetadataField> MetadataFields => _metadataFields.AsReadOnly();

    private readonly List<EventLog> _eventLogs = new();
    public virtual IReadOnlyCollection<EventLog> EventLogs => _eventLogs.AsReadOnly();

    protected Work() 
    {
        CreatedAt = DateTime.UtcNow;
    }

    public Work(string? primaryIdentifier = null)
    {
        Id = Guid.NewGuid();
        PrimaryIdentifier = primaryIdentifier;
        CreatedAt = DateTime.UtcNow;
    }

    public void AddAsset(Asset asset)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        if (!_assets.Any(a => a.Id == asset.Id))
        {
            _assets.Add(asset);
            asset.LinkToWork(this);
        }
    }

    public void AddMetadata(MetadataField field)
    {
        if (field == null) throw new ArgumentNullException(nameof(field));
        _metadataFields.Add(field);
    }

    public void LogEvent(EventLog log)
    {
        if (log == null) throw new ArgumentNullException(nameof(log));
        _eventLogs.Add(log);
    }
}
