using System;

namespace WISE.Domain.Models;

public record MetadataCandidate(
    string ProviderId,
    string FieldName,
    string Value,
    int Confidence,
    int Priority,
    string? Evidence = null,
    string? SourceUrl = null
)
{
    public DateTime FetchedAt { get; init; } = DateTime.UtcNow;
}
