using System;
using WISE.Domain.SeedWork;

namespace WISE.Domain.Entities;

public class MetadataField : Entity
{
    public Guid WorkId { get; private set; }
    public string FieldName { get; private set; }
    public string Value { get; private set; }
    public string ProviderId { get; private set; }
    public bool IsPrimary { get; private set; }
    public int ConfidenceScore { get; private set; }
    public DateTime FetchedAt { get; private set; }

    public virtual Work Work { get; private set; } = null!;

    protected MetadataField()
    {
        FieldName = string.Empty;
        Value = string.Empty;
        ProviderId = string.Empty;
    }

    public MetadataField(string fieldName, string value, string providerId, bool isPrimary, int confidenceScore)
    {
        Id = Guid.NewGuid();
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        IsPrimary = isPrimary;
        ConfidenceScore = confidenceScore;
        FetchedAt = DateTime.UtcNow;
    }

    public void UpdateValue(string value, int confidenceScore, string providerId)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        ConfidenceScore = confidenceScore;
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        FetchedAt = DateTime.UtcNow;
    }
}
