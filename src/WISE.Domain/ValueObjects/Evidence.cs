using System;

namespace WISE.Domain.ValueObjects;

public record Evidence
{
    public string Type { get; init; }
    public string Value { get; init; }
    public ConfidenceScore Score { get; init; }
    public string ProviderId { get; init; }

    public Evidence(string type, string value, ConfidenceScore score, string providerId)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Score = score ?? throw new ArgumentNullException(nameof(score));
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
    }
}
