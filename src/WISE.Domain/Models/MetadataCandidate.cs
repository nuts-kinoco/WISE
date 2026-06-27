using System;

namespace WISE.Domain.Models;

public record MetadataCandidate(
    string ProviderId,
    string FieldName,
    string Value,
    int Confidence,
    string? Evidence = null
)
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
