using System;
using System.Collections.Generic;
using System.Linq;
using WISE.Domain.Enums;
using WISE.Domain.SeedWork;

namespace WISE.Domain.Entities;

public class Work : Entity, IAggregateRoot
{
    public string? PrimaryIdentifier { get; private set; }
    public Guid? MergedIntoId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public ProcessingStatus Status { get; private set; }
    public bool Favorite { get; private set; }
    public int? Rating { get; private set; }
    public MediaType MediaType { get; private set; }

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

    public Work(string? primaryIdentifier = null, MediaType mediaType = MediaType.Video)
    {
        Id = Guid.NewGuid();
        PrimaryIdentifier = primaryIdentifier;
        CreatedAt = DateTime.UtcNow;
        Status = ProcessingStatus.ScanPending;
        MediaType = mediaType;
    }

    public void SetMediaType(MediaType mediaType) => MediaType = mediaType;

    public void UpdateStatus(ProcessingStatus status)
    {
        Status = status;
    }

    public void SetFavorite(bool value) => Favorite = value;
    public void SetRating(int? value) => Rating = value;

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

    public void ApplyResolvedMetadata(IEnumerable<WISE.Domain.Models.ResolvedMetadataCandidate> resolvedCandidates)
    {
        if (resolvedCandidates == null) throw new ArgumentNullException(nameof(resolvedCandidates));

        // Mark everything as non-primary first to prepare for the new state
        foreach (var field in _metadataFields)
        {
            field.SetPrimary(false);
        }

        foreach (var resolved in resolvedCandidates)
        {
            var candidate = resolved.Candidate;
            var existingField = _metadataFields.FirstOrDefault(m => 
                m.FieldName == candidate.FieldName && m.ProviderId == candidate.ProviderId);

            if (existingField != null)
            {
                existingField.UpdateValue(candidate.Value, candidate.Confidence, candidate.ProviderId);
                existingField.SetPrimary(resolved.IsPrimary);
            }
            else
            {
                var newField = new MetadataField(
                    candidate.FieldName, 
                    candidate.Value, 
                    candidate.ProviderId, 
                    resolved.IsPrimary, 
                    candidate.Confidence);
                _metadataFields.Add(newField);
            }
        }

        // Primary保証: 各FieldNameで1つもprimaryが無ければ、最高ConfidenceScoreのものをprimaryにする
        var fieldNames = _metadataFields.Select(m => m.FieldName).Distinct();
        foreach (var fieldName in fieldNames)
        {
            var fieldsForName = _metadataFields.Where(m => m.FieldName == fieldName).ToList();
            if (!fieldsForName.Any(m => m.IsPrimary))
            {
                var best = fieldsForName.OrderByDescending(m => m.ConfidenceScore).First();
                best.SetPrimary(true);
            }
        }

        AddDomainEvent(new WISE.Domain.Events.MetadataUpdatedEvent(this.Id));
    }
}
